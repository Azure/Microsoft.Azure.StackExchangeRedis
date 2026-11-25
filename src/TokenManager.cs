// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Microsoft.Azure.StackExchangeRedis;

/// <summary>
/// Manages Entra tokens used to authenticate one or more Azure Redis connections.
/// </summary>
internal class TokenManager : ITokenEvents
{
    internal ITokenServiceClient TokenServiceClient; // internal so unit tests can inject a fake

    private readonly AuthenticationOptions _authenticationOptions;
    private int _connectionCount = 0;
    private readonly System.Timers.Timer? _heartbeatTimer = new();
    private string? _identityObjectId;
    private string? _token;
    private DateTime _tokenAcquiredTime = DateTime.MinValue;
    private DateTime _tokenExpiry = DateTime.MinValue;

    private readonly ConcurrentDictionary<int, Connection> _connections = new();
    private int _nextConnectionId = 0;
    private readonly ILogger? _log;

    internal TokenManager(
        AuthenticationOptions authenticationOptions,
        ILoggerFactory? loggerFactory)
        : base()
    {
        _authenticationOptions = authenticationOptions;
        _log = loggerFactory?.CreateLogger<TokenManager>();

        TokenServiceClient = GetIdentityClient(authenticationOptions);

        _heartbeatTimer.Interval = authenticationOptions.TokenHeartbeatInterval.TotalMilliseconds;
        _heartbeatTimer.Elapsed += async (s, e) =>
        {
            try
            {
                await EnsureAuthenticationAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Throwing exceptions inside an async void Timer handler would crash the process, do don't allow them to propagate
                // Any exceptions thrown during token retrieval will be reported to the client application via the TokenRefreshFailed or ConnectionReauthenticationFailed events
                _log?.LogError(ex, $"Failed to ensure that connection authentication is current. Next attempt in {authenticationOptions.TokenHeartbeatInterval.TotalSeconds} seconds");
            }
        };
        _heartbeatTimer.AutoReset = true;
        _heartbeatTimer.Start();
    }

    /// <summary>
    /// Instantiates a <see cref="StackExchangeRedis.TokenServiceClient"/> configured for a specific authentication type, depending on the properties supplied in <paramref name="authenticationOptions"/>
    /// </summary>
    /// <param name="authenticationOptions">Options including details of the managed identity or service principal used for authentication</param>
    private ITokenServiceClient GetIdentityClient(AuthenticationOptions authenticationOptions)
    {
        if (authenticationOptions.TokenCredential is not null) // DefaultAzureCredential (or other TokenCredential)
        {
            return StackExchangeRedis.TokenServiceClient.CreateForTokenCredential(authenticationOptions.TokenCredential, authenticationOptions.Scope);
        }
        else if (authenticationOptions.ServicePrincipalTenantId is not null || authenticationOptions.ServicePrincipalSecret is not null || authenticationOptions.ServicePrincipalCertificate is not null) // Service Principal
        {
            if (authenticationOptions.ClientId is null || authenticationOptions.ServicePrincipalTenantId is null)
            {
                throw new ArgumentException($"To use a service principal, {nameof(authenticationOptions.ClientId)} and {nameof(authenticationOptions.ServicePrincipalTenantId)} must be specified");
            }

            if (authenticationOptions.ServicePrincipalSecret is null && authenticationOptions.ServicePrincipalCertificate is null)
            {
                throw new ArgumentException($"To use a service principal, {nameof(authenticationOptions.ServicePrincipalSecret)} or {nameof(authenticationOptions.ServicePrincipalCertificate)} must be specified");
            }

            return StackExchangeRedis.TokenServiceClient.CreateForServicePrincipal(authenticationOptions, _log);
        }
        else // Managed identity
        {
            return StackExchangeRedis.TokenServiceClient.CreateForManagedIdentity(authenticationOptions, _log);
        }
    }

    /// <summary>
    /// The Object ID of the identity used to acquire the token.
    /// </summary>
    public string? IdentityObjectId => _identityObjectId;

    /// <inheritdoc/>
    public string? Token
    {
        get
        {
            if (_tokenExpiry <= DateTime.UtcNow)
            {
                // The heartbeat has failed to refresh the token for some reason (process was suspended, resource exhaustion, etc.). 
                // Kick off a token refresh now so we're not stuck with this expired token until the next heartbeat.
                // To avoid potential sync-over-async issues we don't block here awaiting the token refresh to complete. 
                // The current (expired) token will be returned this time, but a subsequent connection attempt will get a fresh token. 
                _log?.LogWarning($"Current token expired at {_tokenExpiry:s} UTC. Ensuring that a background token refresh is in progress...");
                _ = EnsureAuthenticationAsync().ConfigureAwait(false);
            }

            return _token;
        }
    }

    /// <inheritdoc/>
    public event EventHandler<TokenResult>? TokenRefreshed;

    /// <inheritdoc/>
    public event EventHandler<TokenRefreshFailedEventArgs>? TokenRefreshFailed;

    /// <inheritdoc/>
    public event EventHandler<string>? ConnectionReauthenticated;

    /// <inheritdoc/>
    public event EventHandler<ConnectionReauthenticationFailedEventArgs>? ConnectionReauthenticationFailed;

    /// <summary>
    /// Add a new Redis connection to the list of connections using this token. 
    /// Each StackExchange.Redis.ConnectionMultiplexer instance using these options will call this method when it first connects to Redis
    /// </summary>
    /// <param name="connectionMultiplexer">The multiplexer that just connected.</param>
    public void StartManagingConnection(ConnectionMultiplexer connectionMultiplexer)
    {
        // This is a new connection, so assume it was authenticated with the current token
        _connections.TryAdd(Interlocked.Increment(ref _nextConnectionId), new(connectionMultiplexer, _tokenExpiry));

        if (Interlocked.Increment(ref _connectionCount) == 1)
        {
            // Start token refresh timer when connection count moves from 0 to 1
            _heartbeatTimer?.Start();
            _log?.LogInformation("Redis connection established. Starting token refresh heartbeat.");
        }
    }

    private int _ensureAuthenticationInProgress = 0;
    private async Task EnsureAuthenticationAsync()
    {
        // Take the lock
        if (1 == Interlocked.Exchange(ref _ensureAuthenticationInProgress, 1))
        {
            _log?.LogTrace($"{nameof(EnsureAuthenticationAsync)} is already in progress. Skipping this call.");
            return;
        }

        try
        {
            if (_tokenExpiry <= DateTime.UtcNow // Token has expired
                || (_tokenExpiry - _tokenAcquiredTime) <= TimeSpan.Zero // Current expiry is not valid
                || _authenticationOptions.ShouldTokenBeRefreshed(_tokenAcquiredTime, _tokenExpiry)) // Token is due for refresh
            {
                await AcquireTokenAsync(throwOnFailure: false).ConfigureAwait(false);
            }

            // Re-authenticate any connections that need it, regardless of whether a new token was acquired.
            // Some connections may have previously failed to re-authenticate with the latest token.
            await ReauthenticateConnectionsAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log?.LogError(ex, $"Failed to ensure that authentication is current on all connections.");
        }
        finally
        {
            // Release the lock
            Interlocked.Exchange(ref _ensureAuthenticationInProgress, 0);
        }
    }

    /// <summary>
    /// Acquires a token to authenticate connections to Azure Redis
    /// </summary>
    /// <param name="throwOnFailure"><see langword="true"/> to throw on failure, <see langword="false"/> to suppress handle exceptions and retry</param>
    internal async Task AcquireTokenAsync(bool throwOnFailure)
    {
        TokenResult? tokenResult = null;
        Exception? lastException = null;

        for (var attemptCount = 0; attemptCount < _authenticationOptions.MaxTokenRefreshAttempts; ++attemptCount)
        {
            // Cancel call to acquire token after 10+ seconds. Fail fast (10s) on initial call to evade a transient hang,
            // then increase timeout on subsequent attempts in case it legitimately needs more time. 
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10 + (attemptCount * 5)));

            try
            {
                _log?.LogTrace($"Requesting token...");
                tokenResult = await TokenServiceClient.GetTokenAsync(cts.Token).ConfigureAwait(false);

                break;
            }
            catch (Exception ex)
            {
                lastException = ex;
                _log?.LogWarning(ex, $"Failed to acquire token on attempt {attemptCount + 1} of {_authenticationOptions.MaxTokenRefreshAttempts}.");
            }

            await Task.Delay(_authenticationOptions.TokenRefreshBackoff.Invoke(attemptCount, lastException)).ConfigureAwait(false);
        }

        if (tokenResult is null)
        {
            _log?.LogError(lastException, $"Failed all {_authenticationOptions.MaxTokenRefreshAttempts} attempts to acquire a new token. Current token expiry is {_tokenExpiry:s} UTC");
            await InvokeHandlerWithTimeoutAsync(() => TokenRefreshFailed?.Invoke(this, new(lastException, _tokenExpiry)), nameof(TokenRefreshFailed)).ConfigureAwait(false);

            if (throwOnFailure && lastException is not null)
            {
                throw lastException;
            }

            return;
        }

        if (tokenResult.ExpiresOn.UtcDateTime <= _tokenExpiry)
        {
            _log?.LogInformation($"Received a token with an expiry of {tokenResult.ExpiresOn.UtcDateTime:s} UTC, which is not later than the current token's expiry of {_tokenExpiry:s} UTC. Most likely we got a copy of the current token from the local cache because it's not close enough to expiration to qualify for a refresh.");

            return;
        }

        // Successfully acquired a fresh token
        _tokenAcquiredTime = DateTime.UtcNow;
        _token = tokenResult.Token;
        _tokenExpiry = tokenResult.ExpiresOn.UtcDateTime;
        if (_identityObjectId is null)
        {
            // Extract user name from the first token acquired for an identity
            _identityObjectId = _authenticationOptions.GetUserName(_token);
            _log?.LogInformation($"Acquired token for identity with Object ID: '{_identityObjectId}'");
        }
        _log?.LogInformation($"Acquired token with expiration: {tokenResult.ExpiresOn.UtcDateTime:s} UTC");
        await InvokeHandlerWithTimeoutAsync(() => TokenRefreshed?.Invoke(this, tokenResult), nameof(TokenRefreshed)).ConfigureAwait(false);
    }

    /// <summary>
    /// Re-authenticate all connections with the current token.
    /// </summary>
    private async Task ReauthenticateConnectionsAsync()
    {
        var connectionTasks = new List<Task>(_connections.Count);

        foreach (var connection in _connections)
        {
            if (connection.Value.TokenExpiry >= _tokenExpiry)
            {
                // This connection has already been authenticated with the current token
                continue;
            }

            if (!connection.Value.ConnectionMultiplexerReference.TryGetTarget(out var connectionMultiplexer))
            {
                // The ConnectionMultiplexer reference is no longer valid
                _log?.LogInformation("A Redis connection has been released and will no longer be managed.");
                StopManagingConnection(connection.Key);
                continue;
            }

            connectionTasks.Add(Task.Run(async () =>
            {
                try
                {
                    var servers = connectionMultiplexer.GetServers();
                    var serverTasks = new List<Task>(servers.Length);
                    var allServersReauthenticated = true;

                    _log?.LogTrace($"Re-authenticating connections in client '{connectionMultiplexer.ClientName}' to update expiry from {connection.Value.TokenExpiry:s} UTC to {_tokenExpiry:s} UTC...");

                    foreach (var server in servers)
                    {
                        if (!server.IsConnected)
                        {
                            // Avoid backlogging an AUTH command for a server that's not currently connected.
                            _log?.LogWarning($"Skipping re-authentication for '{server.EndPoint}' because it is not currently connected. It will use the new token when it reconnects.");
                            continue;
                        }

                        serverTasks.Add(Task.Run(async () =>
                        {
                            try
                            {
                                // NOTE that this will only re-authenticate RESP3 and interactive RESP2 connections. RESP2 subscription connections (where the SUBSCRIBE command has been run) cannot be re-authenticated. 
                                // When a subscription connection's token expires, the server will close it and StackExchange.Redis will immediately restore the connection using the current token.
                                // This may result in brief gaps where subscription connections are not available and published messages aren't received by clients that are in the process of recovering their connection. 
                                _log?.LogTrace($"Re-authenticating connection to '{server.EndPoint}'...");
                                await server.ExecuteAsync("AUTH", IdentityObjectId!, Token!).ConfigureAwait(false);
                                _log?.LogInformation($"Re-authenticated connection to '{server.EndPoint}' with a token that will expire at {_tokenExpiry:s} UTC");

                                await InvokeHandlerWithTimeoutAsync(() => ConnectionReauthenticated?.Invoke(this, server.EndPoint.ToString()!), nameof(ConnectionReauthenticated)).ConfigureAwait(false);
                            }
                            catch (ObjectDisposedException)
                            {
                                // This ConnectionMultiplexer has been disposed. Abort re-authentication of all its server connections.
                                throw;
                            }
                            catch (Exception ex)
                            {
                                _log?.LogError(ex, $"Failed to re-authenticate connection to '{server.EndPoint}'. Current token will expire at {connection.Value.TokenExpiry:s} UTC. Potential causes include high Redis server load or short command timeout configuration. Will try again on the next heartbeat.");
                                allServersReauthenticated = false;

                                await InvokeHandlerWithTimeoutAsync(() => ConnectionReauthenticationFailed?.Invoke(this, new(ex, server.EndPoint.ToString()!)), nameof(ConnectionReauthenticationFailed)).ConfigureAwait(false);
                            }
                        }));
                    }
                    await Task.WhenAll(serverTasks).ConfigureAwait(false);

                    if (allServersReauthenticated)
                    {
                        connection.Value.TokenExpiry = _tokenExpiry;
                    }
                }
                catch (ObjectDisposedException)
                {
                    _log?.LogInformation("A Redis connection has been disposed and will no longer be managed.");
                    StopManagingConnection(connection.Key);
                }
                catch (Exception ex)
                {
                    _log?.LogError(ex, $"Failed to re-authenticate connections in client '{connectionMultiplexer.ClientName}'. Current token will expire at {connection.Value.TokenExpiry:s} UTC.");
                    await InvokeHandlerWithTimeoutAsync(() => ConnectionReauthenticationFailed?.Invoke(this, new(ex, connectionMultiplexer.ClientName)), nameof(ConnectionReauthenticationFailed)).ConfigureAwait(false);
                }
                finally
                {
                    // Explicitly release this strong reference so only the WeakReference remains and the ConnectionMultiplexer can be garbage collected if the client app releases it.
                    connectionMultiplexer = null;
                }
            }));
        }
        await Task.WhenAll(connectionTasks).ConfigureAwait(false);
    }

    private void StopManagingConnection(int key)
    {
        if (_connections.TryRemove(key, out _)
            && Interlocked.Decrement(ref _connectionCount) == 0)
        {
            _heartbeatTimer?.Stop();
            _log?.LogWarning("No more Redis connections to manage. Stopping token refresh.");
        }
    }

    private static readonly TimeSpan HandlerTimeout = TimeSpan.FromSeconds(5);
    private async Task InvokeHandlerWithTimeoutAsync(Action handler, string name)
    {
        try
        {
            var invocation = Task.Run(() => handler.Invoke());

            if (invocation != await Task.WhenAny(invocation, Task.Delay(HandlerTimeout)).ConfigureAwait(false))
            {
                _log?.LogError($"Handler for {name} timed out after 5 seconds");
            }
        }
        catch (Exception ex)
        {
            _log?.LogError(ex, $"Handler for {name} failed");
        }
    }

}

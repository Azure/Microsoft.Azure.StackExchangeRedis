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
/// Redis connection configuration options provider for using Microsoft Entra ID authentication with Azure Cache for Redis.
/// </summary>
internal class AzureCacheOptionsProviderWithToken : AzureCacheOptionsProvider, IAzureCacheTokenEvents
{
    internal ICacheIdentityClient IdentityClient; // internal so unit tests can inject a fake

    private readonly AzureCacheOptions _azureCacheOptions;
    private int _currentActiveConnections = 0;
    private readonly System.Timers.Timer? _heartbeatTimer = new();
    private string? _user;
    private string? _token;
    private DateTime _tokenAcquiredTime = DateTime.MinValue;
    private DateTime _tokenExpiry = DateTime.MinValue;

    private readonly ConcurrentDictionary<int, CacheConnection> _cacheConnections = new();
    private int _nextConnectionId = 0;
    private readonly ILogger? _log;

    internal AzureCacheOptionsProviderWithToken(
        AzureCacheOptions azureCacheOptions,
        ILoggerFactory? loggerFactory)
        : base()
    {
        _azureCacheOptions = azureCacheOptions;
        _log = loggerFactory?.CreateLogger<AzureCacheOptionsProviderWithToken>();

        IdentityClient = GetIdentityClient(azureCacheOptions);

        _heartbeatTimer.Interval = azureCacheOptions.TokenHeartbeatInterval.TotalMilliseconds;
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
                _log?.LogError(ex, $"Failed to ensure that connection authentication is current. Next attempt in {azureCacheOptions.TokenHeartbeatInterval.TotalSeconds} seconds");
            }
        };
        _heartbeatTimer.AutoReset = true;
        _heartbeatTimer.Start();
    }

    /// <summary>
    /// Instantiates a <see cref="CacheIdentityClient"/> configured for a specific authentication type, depending on the properties supplied in <paramref name="azureCacheOptions"/>
    /// </summary>
    /// <param name="azureCacheOptions">Options including details of the managed identity or service principal used for authentication</param>
    private ICacheIdentityClient GetIdentityClient(AzureCacheOptions azureCacheOptions)
    {
        if (azureCacheOptions.TokenCredential is not null) // DefaultAzureCredential (or other TokenCredential)
        {
            return CacheIdentityClient.CreateForTokenCredential(azureCacheOptions.TokenCredential, azureCacheOptions.Scope);
        }
        else if (azureCacheOptions.ServicePrincipalTenantId is not null || azureCacheOptions.ServicePrincipalSecret is not null || azureCacheOptions.ServicePrincipalCertificate is not null) // Service Principal
        {
            if (azureCacheOptions.ClientId is null || azureCacheOptions.ServicePrincipalTenantId is null)
            {
                throw new ArgumentException($"To use a service principal, {nameof(azureCacheOptions.ClientId)} and {nameof(azureCacheOptions.ServicePrincipalTenantId)} must be specified");
            }

            if (azureCacheOptions.ServicePrincipalSecret is null && azureCacheOptions.ServicePrincipalCertificate is null)
            {
                throw new ArgumentException($"To use a service principal, {nameof(azureCacheOptions.ServicePrincipalSecret)} or {nameof(azureCacheOptions.ServicePrincipalCertificate)} must be specified");
            }

            return CacheIdentityClient.CreateForServicePrincipal(azureCacheOptions);
        }
        else // Managed identity
        {
            return CacheIdentityClient.CreateForManagedIdentity(azureCacheOptions);
        }
    }

    /// <summary>
    /// Require SSL on all connections using Microsoft Entra ID tokens for authentication.
    /// </summary>
    /// <returns>True for all cases.</returns>
    public override bool GetDefaultSsl(EndPointCollection _) => true;

    /// <summary>
    /// The username used to authenticate with the server. 
    /// For Entra ID authentication, this should be the Object ID of the identity used to acquire the token.
    /// </summary>
    public override string? User => _user;

    /// <inheritdoc/>
    public override string? Password
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
    /// Initialize new Redis connections managed by this instance of the extension. 
    /// Each StackExchange.Redis.ConnectionMultiplexer instance using these options will call this method when it first connects to Redis
    /// </summary>
    /// <param name="connectionMultiplexer">The multiplexer that just connected.</param>
    /// <param name="log">The logger for the connection, to emit to the connection output log.</param>
    public override async Task AfterConnectAsync(ConnectionMultiplexer connectionMultiplexer, Action<string> log)
    {
        try
        {
            // This is a new connection, so assume it was authenticated with the current token
            _cacheConnections.TryAdd(Interlocked.Increment(ref _nextConnectionId), new(connectionMultiplexer, _tokenExpiry));

            if (Interlocked.Increment(ref _currentActiveConnections) == 1)
            {
                // Start token refresh timer when connection count moves from 0 to 1
                _heartbeatTimer?.Start();
                _log?.LogInformation("Redis connection established. Starting token refresh.");
            }

            await base.AfterConnectAsync(connectionMultiplexer, log).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log?.LogError($"Failed to initialize new connection: {ex}");
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
                || _azureCacheOptions.ShouldTokenBeRefreshed(_tokenAcquiredTime, _tokenExpiry)) // Token is due for refresh
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
    /// Acquires a token to authenticate connections to Azure Cache for Redis
    /// </summary>
    /// <param name="throwOnFailure"><see langword="true"/> to throw on failure, <see langword="false"/> to suppress handle exceptions and retry</param>
    internal async Task AcquireTokenAsync(bool throwOnFailure)
    {
        TokenResult? tokenResult = null;
        Exception? lastException = null;

        for (var attemptCount = 0; attemptCount < _azureCacheOptions.MaxTokenRefreshAttempts; ++attemptCount)
        {
            // Cancel call to acquire token after 10+ seconds. Fail fast (10s) on initial call to evade a transient hang,
            // then increase timeout on subsequent attempts in case it legitimately needs more time. 
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10 + (attemptCount * 5)));

            try
            {
                _log?.LogTrace($"Requesting token...");
                tokenResult = await IdentityClient.GetTokenAsync(cts.Token).ConfigureAwait(false);

                break;
            }
            catch (Exception ex)
            {
                lastException = ex;
                _log?.LogWarning(ex, $"Failed to acquire token on attempt {attemptCount + 1} of {_azureCacheOptions.MaxTokenRefreshAttempts}.");
            }

            await Task.Delay(_azureCacheOptions.TokenRefreshBackoff.Invoke(attemptCount, lastException)).ConfigureAwait(false);
        }

        if (tokenResult is null)
        {
            _log?.LogError(lastException, $"Failed all {_azureCacheOptions.MaxTokenRefreshAttempts} attempts to acquire a new token. Current token expiry is {_tokenExpiry:s} UTC");
            await InvokeHandlerWithTimeoutAsync(() => TokenRefreshFailed?.Invoke(this, new(lastException, _tokenExpiry)), nameof(TokenRefreshFailed));

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
        if (_user is null)
        {
            // Extract user name from the first token acquired for an identity
            _user = _azureCacheOptions.GetUserName(_token);
            _log?.LogInformation($"Acquired token for identity with Object ID: '{_user}'");
        }
        _log?.LogInformation($"Acquired token with expiration: {tokenResult.ExpiresOn.UtcDateTime:s} UTC");
        await InvokeHandlerWithTimeoutAsync(() => TokenRefreshed?.Invoke(this, tokenResult), nameof(TokenRefreshed));
    }

    /// <summary>
    /// Re-authenticate all connections with the current token.
    /// </summary>
    private async Task ReauthenticateConnectionsAsync()
    {
        var connectionTasks = new List<Task>(_cacheConnections.Count);

        foreach (var connection in _cacheConnections)
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
                                await server.ExecuteAsync("AUTH", User!, Password!).ConfigureAwait(false);
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
        if (_cacheConnections.TryRemove(key, out _)
            && Interlocked.Decrement(ref _currentActiveConnections) == 0)
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

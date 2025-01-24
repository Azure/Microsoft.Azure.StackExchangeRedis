// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis;

namespace Microsoft.Azure.StackExchangeRedis;

/// <summary>
/// Redis connection configuration options provider for using Microsoft Entra ID authentication with Azure Cache for Redis.
/// </summary>
internal class AzureCacheOptionsProviderWithToken : AzureCacheOptionsProvider, IAzureCacheTokenEvents
{
    internal ICacheIdentityClient IdentityClient; // internal so unit tests can inject a fake

    private readonly System.Timers.Timer _tokenRefreshTimer = new();
    private readonly AzureCacheOptions _azureCacheOptions;
    internal string? _user;
    private string? _token;
    private DateTime _tokenAcquiredTime = DateTime.MinValue;
    private DateTime _tokenExpiry = DateTime.UtcNow; // Setting a valid DateTime value to allow us to subtract a leeway

    private readonly ConcurrentDictionary<int, CacheConnection> _cacheConnections = new();
    private int _nextConnectionId = 0;

    internal AzureCacheOptionsProviderWithToken(
        AzureCacheOptions azureCacheOptions)
        : base()
    {
        _azureCacheOptions = azureCacheOptions;

        IdentityClient = GetIdentityClient(azureCacheOptions);

        _tokenRefreshTimer.Interval = azureCacheOptions.TokenHeartbeatInterval.TotalMilliseconds;
        _tokenRefreshTimer.Elapsed += async (s, e) =>
        {
            try
            {
                await EnsureAuthenticationAsync(throwOnFailure: false).ConfigureAwait(false);
            }
            catch
            {
                // Throwing exceptions inside an async void Timer handler would crash the process, do don't allow them to propagate
                // Any exceptions thrown during token retrieval will be reported to the client application via the TokenRefreshFailed or ConnectionReauthenticationFailed events
            }
        };
        _tokenRefreshTimer.AutoReset = true;
        _tokenRefreshTimer.Start();
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

    /// <inheritdoc/>
    public override string? User => _user;

    /// <inheritdoc/>
    public override string? Password => _token;

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
            // This is a new connection, so we can assume it was authenticated with the current token
            _cacheConnections.TryAdd(_nextConnectionId++, new(connectionMultiplexer, _tokenExpiry));

            await base.AfterConnectAsync(connectionMultiplexer, log).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            log.Invoke($"Microsoft.Azure.StackExchangeRedis: Failed to initialize new connection: {ex}");
        }
    }

    private int _ensureAuthenticationInProgress = 0;
    private async Task EnsureAuthenticationAsync(bool throwOnFailure = true)
    {
        // Take the lock
        if (1 == Interlocked.Exchange(ref _ensureAuthenticationInProgress, 1))
        {
            // Update is already in progress
            return;
        }

        try
        {
            if (_tokenAcquiredTime == DateTime.MinValue // Initial token has not yet been acquired
                || (_tokenExpiry - _tokenAcquiredTime) <= TimeSpan.Zero // Current expiry is not valid
                || _azureCacheOptions.ShouldTokenBeRefreshed(_tokenAcquiredTime, _tokenExpiry)) // Token is due for refresh
            {
                await AcquireTokenAsync(throwOnFailure).ConfigureAwait(false);
            }

            await ReauthenticateConnectionsAsync().ConfigureAwait(false);
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
        Exception? lastException = null;
        for (var attemptCount = 0; attemptCount < _azureCacheOptions.MaxTokenRefreshAttempts; ++attemptCount)
        {
            try
            {
                TokenResult tokenResult = await IdentityClient.GetTokenAsync().ConfigureAwait(false);
                var leeway = TimeSpan.FromSeconds(30); // Sometimes the updated token may actually have an expiry a few seconds shorter than the original

                if (tokenResult is not null && tokenResult.ExpiresOn.UtcDateTime >= _tokenExpiry.Subtract(leeway))
                {
                    _token = tokenResult.Token;
                    _tokenAcquiredTime = DateTime.UtcNow;
                    _tokenExpiry = tokenResult.ExpiresOn.UtcDateTime;
                    TokenRefreshed?.Invoke(this, tokenResult);
                    return;
                }
            }
            catch (Exception ex)
            {
                lastException = ex;
            }

            await Task.Delay(_azureCacheOptions.TokenRefreshBackoff.Invoke(attemptCount, lastException)).ConfigureAwait(false);
        }

        // If we get here, we never successfully acquired a token
        TokenRefreshFailed?.Invoke(this, new(lastException, _tokenExpiry));
        if (throwOnFailure && lastException is not null)
        {
            throw lastException;
        }
    }

    /// <summary>
    /// Re-authenticate all connections with the current token.
    /// </summary>
    private async Task ReauthenticateConnectionsAsync()
    {
        var connectionTasks = new List<Task>(_cacheConnections.Count);
        foreach (var connection in _cacheConnections)
        {
            connectionTasks.Add(Task.Run(async () =>
            {
                try
                {
                    if (connection.Value.TokenExpiry >= _tokenExpiry)
                    {
                        // This connection has already been authenticated with the current token
                        return;
                    }

                    if (!connection.Value.ConnectionMultiplexerReference.TryGetTarget(out var connectionMultiplexer))
                    {
                        // The IConnectionMultiplexer reference is no longer valid
                        _cacheConnections.TryRemove(connection.Key, out _);
                        return;
                    }

                    var servers = connectionMultiplexer.GetServers();
                    var serverTasks = new List<Task>(servers.Length);
                    var allSucceeded = true;
                    foreach (var server in servers)
                    {
                        serverTasks.Add(Task.Run(async () =>
                        {
                            try
                            {
                                // NOTE that this will only re-authenticate interactive connections. Subscription connections (where the SUBSCRIBE command has been run) cannot be re-authenticated. 
                                // When a subscription connection's token expires, the server will close it and StackExchange.Redis will immediately restore the connection using the current token.
                                // This may result in brief gaps where subscription connections are not available and published messages aren't received by clients that are in the process of recovering their connection. 
                                await server.ExecuteAsync("AUTH", User!, Password!).ConfigureAwait(false);
                                ConnectionReauthenticated?.Invoke(this, server.EndPoint.ToString()!);
                            }
                            catch (Exception ex)
                            {
                                // No need to retry. When the connection is restored it will be authenticated with the AzureCacheOptionsProviderWithToken.Password which has been updated to the new token
                                ConnectionReauthenticationFailed?.Invoke(this, new(ex, server.EndPoint.ToString()!));
                                allSucceeded = false;
                            }
                        }));
                    }
                    await Task.WhenAll(serverTasks).ConfigureAwait(false);

                    if (allSucceeded)
                    {
                        connection.Value.TokenExpiry = _tokenExpiry;
                    }
                }
                catch (Exception ex)
                {
                    ConnectionReauthenticationFailed?.Invoke(this, new(ex, "UNKNOWN"));
                }
            }));
        }
        await Task.WhenAll(connectionTasks).ConfigureAwait(false);
    }

}

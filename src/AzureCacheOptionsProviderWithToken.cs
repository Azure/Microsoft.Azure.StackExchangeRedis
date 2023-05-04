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
/// Redis connection configuration options provider for using Azure Active Directory authentication with Azure Cache for Redis.
/// </summary>
internal class AzureCacheOptionsProviderWithToken : AzureCacheOptionsProvider, IAzureCacheTokenEvents
{
    internal ICacheIdentityClient _identityClient; // internal so unit tests can inject a fake
    private readonly System.Timers.Timer _tokenRefreshTimer = new();

    private readonly AzureCacheOptions _azureCacheOptions;
    private readonly string? _userName;
    private string? _currentToken;
    private DateTime _currentTokenExpiry = DateTime.MinValue;
    private DateTime _nextRefreshTime = DateTime.MinValue;
    private DateTime _tokenAcquiredTime = DateTime.MinValue; // For debugging only

    private readonly ConcurrentDictionary<int, CacheConnection> _cacheConnections = new();
    private int _nextConnectionId = 0;

    internal AzureCacheOptionsProviderWithToken(
        AzureCacheOptions azureCacheOptions)
        : base()
    {
        _azureCacheOptions = azureCacheOptions;

        if (string.IsNullOrWhiteSpace(azureCacheOptions.PrincipalId))
        {
            throw new ArgumentException("Principal ID of a managed identity or service principal must be specified", nameof(azureCacheOptions.PrincipalId));
        }
        _userName = azureCacheOptions.PrincipalId;
        _identityClient = GetIdentityClient(azureCacheOptions);

        _tokenRefreshTimer.Interval = azureCacheOptions.TokenHeartbeatInterval.TotalMilliseconds;
        _tokenRefreshTimer.Elapsed += async (s, e) =>
        {
            try
            {
                await EnsureAuthenticationAsync(forceRefresh: true, throwOnFailure: false).ConfigureAwait(false);
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
    private static ICacheIdentityClient GetIdentityClient(AzureCacheOptions azureCacheOptions)
    {
        var servicePrincipalTenantIdSpecified = !string.IsNullOrWhiteSpace(azureCacheOptions.ServicePrincipalTenantId);
        var servicePrincipalSecretSpecified = !string.IsNullOrWhiteSpace(azureCacheOptions.ServicePrincipalSecret);

        if (servicePrincipalTenantIdSpecified || servicePrincipalSecretSpecified)
        {
            // Service principal details were supplied, so authenticate using a service principal
            if (!(servicePrincipalTenantIdSpecified && servicePrincipalSecretSpecified && !string.IsNullOrWhiteSpace(azureCacheOptions.ClientId)))
            {
                throw new ArgumentException("To use a service principal, all four properties must be specified: Client ID, Principal ID, Tenant ID, and secret");
            }

            return CacheIdentityClient.CreateForServicePrincipal(azureCacheOptions.ClientId!, azureCacheOptions.ServicePrincipalTenantId!, azureCacheOptions.ServicePrincipalSecret!);
        }
        else // No service principal details supplied
        {
            // Authenticate using a managed identity
            return !string.IsNullOrWhiteSpace(azureCacheOptions.ClientId) ?
                CacheIdentityClient.CreateForUserAssignedManagedIdentity(azureCacheOptions.ClientId!) // A Client ID was supplied, so authenticate using a user-assigned managed identity
                : CacheIdentityClient.CreateForSystemAssignedManagedIdentity(); // No Client ID was supplied, so authenticate with a system-assigned managed identity
        }
    }

    /// <summary>
    /// Require SSL on all connections using Azure Active Directory tokens for authentication.
    /// </summary>
    /// <returns>True for all cases.</returns>
    public override bool GetDefaultSsl(EndPointCollection _) => true;

    /// <inheritdoc/>
    public override string? User => _userName;

    /// <inheritdoc/>
    public override string? Password => _currentToken;

    /// <inheritdoc/>
    public event EventHandler<DateTime>? TokenRefreshed;

    /// <inheritdoc/>
    public event EventHandler<TokenRefreshFailedEventArgs>? TokenRefreshFailed;

    /// <inheritdoc/>
    public event EventHandler<string>? ConnectionReauthenticated;

    /// <inheritdoc/>
    public event EventHandler<ConnectionReauthenticationFailedEventArgs>? ConnectionReauthenticationFailed;

    /// <summary>
    /// Initialize new Redis connections managed by this instance of the extension
    /// </summary>
    /// <param name="connectionMultiplexer">The multiplexer that just connected.</param>
    /// <param name="log">The logger for the connection, to emit to the connection output log.</param>
    public override async Task AfterConnectAsync(ConnectionMultiplexer connectionMultiplexer, Action<string> log)
    {
        try
        {
            // This is a new connection, so we can assume it was authenticated with the current token
            _cacheConnections.TryAdd(_nextConnectionId++, new(connectionMultiplexer, _currentTokenExpiry));

            await base.AfterConnectAsync(connectionMultiplexer, log).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            log.Invoke($"Microsoft.Azure.StackExchangeRedis: Failed to initialize new connection: {ex}");
        }
    }

    private int _ensureAuthenticationInProgress = 0;
    private async Task EnsureAuthenticationAsync(bool forceRefresh, bool throwOnFailure = true)
    {
        // Take the lock
        if (1 == Interlocked.Exchange(ref _ensureAuthenticationInProgress, 1))
        {
            // Update is already in progress
            return;
        }

        try
        {
            if (DateTime.UtcNow >= _nextRefreshTime)
            {
                await AcquireTokenAsync(forceRefresh, throwOnFailure).ConfigureAwait(false);
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
    /// <param name="forceRefresh"><see langword="false"/> to use a token from the local cache, or <see langword="true"/> to force a refresh from AAD</param>
    /// <param name="throwOnFailure"><see langword="true"/> to throw on failure, <see langword="false"/> to suppress handle exceptions and retry</param>
    internal async Task AcquireTokenAsync(bool forceRefresh, bool throwOnFailure)
    {
        Exception? lastException = null;
        for (var attemptCount = 0; attemptCount < _azureCacheOptions.MaxTokenRefreshAttempts; ++attemptCount)
        {
            try
            {
                var authenticationResult = await _identityClient.GetTokenAsync(forceRefresh).ConfigureAwait(false);

                if (authenticationResult != null && authenticationResult.ExpiresOn.UtcDateTime >= _currentTokenExpiry)
                {
                    _currentToken = authenticationResult.AccessToken;
                    _currentTokenExpiry = authenticationResult.ExpiresOn.UtcDateTime;
                    _tokenAcquiredTime = DateTime.UtcNow; // Track this time for debugging

                    TokenRefreshed?.Invoke(this, _currentTokenExpiry);

                    // Schedule next refresh
                    _nextRefreshTime = _currentTokenExpiry - _azureCacheOptions.TokenExpirationMargin;

                    return;
                }
            }
            catch (Exception ex)
            {
                lastException = ex;
            }

            // Retry with a linear back-off interval increasing one second per attempt
            await Task.Delay(TimeSpan.FromSeconds(attemptCount)).ConfigureAwait(false);
        }

        // If we get here, we never successfully acquired a token
        TokenRefreshFailed?.Invoke(this, new(lastException, _currentTokenExpiry));
        if (throwOnFailure && lastException != null)
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
                    if (connection.Value.TokenExpiry >= _currentTokenExpiry)
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
                                // No need to retry. When the connection is restored it will be authenticated with the ConfigurationOptions.Password which has been updated to the new token
                                ConnectionReauthenticationFailed?.Invoke(this, new(ex, server.EndPoint.ToString()!));
                                allSucceeded = false;
                            }
                        }));
                    }
                    await Task.WhenAll(serverTasks).ConfigureAwait(false);

                    if (allSucceeded)
                    {
                        connection.Value.TokenExpiry = _currentTokenExpiry;
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

// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using StackExchange.Redis.Configuration;

namespace Microsoft.Azure.StackExchangeRedis;

/// <summary>
/// Connection configuration for use with Azure Redis resources.
/// </summary>
internal class AzureCacheForRedisConfiguration : AzureOptionsProvider, ITokenEvents
{
    internal readonly TokenManager? TokenManager;

    // Create an instance that does not use Entra ID authentication
    internal AzureCacheForRedisConfiguration()
    { }

    // Create an instance that uses Entra ID authentication
    internal AzureCacheForRedisConfiguration(AuthenticationOptions authenticationOptions, ILoggerFactory? loggerFactory)
    {
        TokenManager = new TokenManager(authenticationOptions, loggerFactory);
        TokenManager.TokenRefreshed += (sender, args) => TokenRefreshed?.Invoke(sender, args);
        TokenManager.TokenRefreshFailed += (sender, args) => TokenRefreshFailed?.Invoke(sender, args);
        TokenManager.ConnectionReauthenticated += (sender, args) => ConnectionReauthenticated?.Invoke(sender, args);
        TokenManager.ConnectionReauthenticationFailed += (sender, args) => ConnectionReauthenticationFailed?.Invoke(sender, args);
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
    /// The identifier used to mark connections being managed by this extension package.
    /// Defaults to "MS.Az.SERedis".
    /// </summary>
    public override string LibraryName => AzureRedis.LibraryName;

    /// <summary>
    /// Require SSL on all connections using Microsoft Entra ID tokens for authentication.
    /// </summary>
    /// <returns>True for all cases.</returns>
    public override bool GetDefaultSsl(EndPointCollection endpoints) => TokenManager is null ? base.GetDefaultSsl(endpoints) : true;

    /// <summary>
    /// The username used to authenticate with the server. 
    /// For Entra ID authentication, this will be the Object ID of the identity used to acquire the token.
    /// </summary>
    public override string? User => TokenManager?.IdentityObjectId ?? base.User;

    /// <inheritdoc/>
    public override string? Password => TokenManager?.Token ?? base.Password;

    /// <summary>
    /// Initialize new Redis connections managed by this instance of the extension. 
    /// Each StackExchange.Redis.ConnectionMultiplexer instance using these options will call this method when it first connects to Redis
    /// </summary>
    /// <param name="connectionMultiplexer">The multiplexer that just connected.</param>
    /// <param name="log">The logger for the connection, to emit to the connection output log.</param>
    public override async Task AfterConnectAsync(ConnectionMultiplexer connectionMultiplexer, Action<string> log)
    { 
        TokenManager?.StartManagingConnection(connectionMultiplexer);

        await base.AfterConnectAsync(connectionMultiplexer, log).ConfigureAwait(false);
    }
}

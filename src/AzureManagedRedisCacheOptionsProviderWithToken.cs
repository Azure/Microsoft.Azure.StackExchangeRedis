// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Threading.Tasks;
using StackExchange.Redis;

namespace Microsoft.Azure.StackExchangeRedis;

/// <summary>
/// Redis connection configuration options provider for using Microsoft Entra ID authentication with Azure Managed Redis.
/// </summary>
internal class AzureManagedRedisCacheOptionsProviderWithToken : AzureManagedRedisCacheOptionsProvider, IAzureCacheTokenEvents
{
    private readonly RedisTokenAuthenticationManager _tokenAuthenticationManager;

    internal AzureManagedRedisCacheOptionsProviderWithToken(RedisTokenAuthenticationManager tokenAuthenticationManager)
    {
        _tokenAuthenticationManager = tokenAuthenticationManager ?? throw new ArgumentNullException(nameof(tokenAuthenticationManager));
        _tokenAuthenticationManager.TokenRefreshed += (_, args) => TokenRefreshed?.Invoke(this, args);
        _tokenAuthenticationManager.TokenRefreshFailed += (_, args) => TokenRefreshFailed?.Invoke(this, args);
        _tokenAuthenticationManager.ConnectionReauthenticated += (_, args) => ConnectionReauthenticated?.Invoke(this, args);
        _tokenAuthenticationManager.ConnectionReauthenticationFailed += (_, args) => ConnectionReauthenticationFailed?.Invoke(this, args);
    }

    /// <summary>
    /// Require SSL on all connections using Microsoft Entra ID tokens for authentication.
    /// </summary>
    public override bool GetDefaultSsl(EndPointCollection _) => true;

    /// <summary>
    /// The username used to authenticate with the server. 
    /// For Entra ID authentication, this should be the Object ID of the identity used to acquire the token.
    /// </summary>
    public override string? User => _tokenAuthenticationManager.User;

    /// <inheritdoc/>
    public override string? Password => _tokenAuthenticationManager.Password;

    /// <inheritdoc/>
    public event EventHandler<TokenResult>? TokenRefreshed;

    /// <inheritdoc/>
    public event EventHandler<TokenRefreshFailedEventArgs>? TokenRefreshFailed;

    /// <inheritdoc/>
    public event EventHandler<string>? ConnectionReauthenticated;

    /// <inheritdoc/>
    public event EventHandler<ConnectionReauthenticationFailedEventArgs>? ConnectionReauthenticationFailed;

    /// <inheritdoc/>
    public override Task AfterConnectAsync(ConnectionMultiplexer connectionMultiplexer, Action<string> log)
        => _tokenAuthenticationManager.AfterConnectAsync(
            connectionMultiplexer, () => base.AfterConnectAsync(connectionMultiplexer, log));
}

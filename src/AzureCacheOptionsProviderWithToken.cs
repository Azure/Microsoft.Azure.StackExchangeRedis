// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Threading.Tasks;
using StackExchange.Redis;

namespace Microsoft.Azure.StackExchangeRedis;

/// <summary>
/// Redis connection configuration options provider for using Microsoft Entra ID authentication with Azure Cache for Redis.
/// </summary>
internal class AzureCacheOptionsProviderWithToken : AzureCacheOptionsProvider, IAzureCacheTokenEvents
{
    private readonly TokenOptionsProvider _tokenOptionsProvider;

    internal AzureCacheOptionsProviderWithToken(TokenOptionsProvider tokenOptionsProvider)
    {
        _tokenOptionsProvider = tokenOptionsProvider ?? throw new ArgumentNullException(nameof(tokenOptionsProvider));
        _tokenOptionsProvider.TokenRefreshed += (_, args) => TokenRefreshed?.Invoke(this, args);
        _tokenOptionsProvider.TokenRefreshFailed += (_, args) => TokenRefreshFailed?.Invoke(this, args);
        _tokenOptionsProvider.ConnectionReauthenticated += (_, args) => ConnectionReauthenticated?.Invoke(this, args);
        _tokenOptionsProvider.ConnectionReauthenticationFailed += (_, args) => ConnectionReauthenticationFailed?.Invoke(this, args);
    }

    /// <summary>
    /// Require SSL on all connections using Microsoft Entra ID tokens for authentication.
    /// </summary>
    public override bool GetDefaultSsl(EndPointCollection _) => true;

    /// <inheritdoc/>
    public override string? User => _tokenOptionsProvider.User;

    /// <inheritdoc/>
    public override string? Password => _tokenOptionsProvider.Password;

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
        => _tokenOptionsProvider.AfterConnectAsync(
            connectionMultiplexer, () => base.AfterConnectAsync(connectionMultiplexer, log));
}

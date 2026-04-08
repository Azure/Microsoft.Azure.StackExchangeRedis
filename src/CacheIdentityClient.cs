// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.AppConfig;

namespace Microsoft.Azure.StackExchangeRedis;

/// <summary>
/// Acquires tokens from Microsoft Entra ID for authenticating connections to Azure Cache for Redis.
/// </summary>
internal interface ICacheIdentityClient
{
    /// <summary>
    /// Acquire a token to be used to authenticate a connection.
    /// </summary>
    /// <returns>A TokenResult containing a token and expiration if synchronous token retrieval is supported.</returns>
    TokenResult GetToken(CancellationToken cancellationToken);

    /// <summary>
    /// Acquire a token to be used to authenticate a connection.
    /// </summary>
    /// <returns>A TokenResult containing a token and expiration</returns>
    Task<TokenResult> GetTokenAsync(CancellationToken cancellationToken);
}

internal class CacheIdentityClient : ICacheIdentityClient
{
    private readonly Func<CancellationToken, TokenResult>? _getToken;
    private readonly Func<CancellationToken, Task<TokenResult>> _getTokenAsync;

    private CacheIdentityClient(
            Func<CancellationToken, ValueTask<AccessToken>> getTokenAsync)
        : this(
            getToken: null,
            getTokenAsync)
    { }

    private CacheIdentityClient(
        Func<CancellationToken, AccessToken>? getToken,
        Func<CancellationToken, ValueTask<AccessToken>> getTokenAsync)
    {
        _getToken = getToken is not null ?
            cancellationToken => new TokenResult(getToken(cancellationToken))
            : null;
        _getTokenAsync = async (cancellationToken) => new TokenResult(await getTokenAsync.Invoke(cancellationToken).ConfigureAwait(false));
    }

    private CacheIdentityClient(
            Func<CancellationToken, Task<AuthenticationResult>> getTokenAsync)
        : this(
              getToken: null,
              getTokenAsync)
    { }

    private CacheIdentityClient(
        Func<CancellationToken, AuthenticationResult>? getToken,
        Func<CancellationToken, Task<AuthenticationResult>> getTokenAsync)
    {
        _getToken = getToken is not null ?
            (cancellationToken) => new TokenResult(getToken(cancellationToken))
            : null;
        _getTokenAsync = async (cancellationToken) => new TokenResult(await getTokenAsync.Invoke(cancellationToken).ConfigureAwait(false));
    }

    internal static ICacheIdentityClient CreateForManagedIdentity(AzureCacheOptions options, ILogger? log)
    {
        var clientAppBuilder = ManagedIdentityApplicationBuilder.Create(
            options.ClientId is null ?
                ManagedIdentityId.SystemAssigned
                : Guid.TryParse(options.ClientId, out _) ?
                    ManagedIdentityId.WithUserAssignedClientId(options.ClientId)
                        : ManagedIdentityId.WithUserAssignedResourceId(options.ClientId));

        if (log is not null)
        {
            clientAppBuilder = clientAppBuilder.WithLogging((level, message, containsPii) => IdentityLog(level, message, containsPii, log), Identity.Client.LogLevel.Warning, enablePiiLogging: false, enableDefaultPlatformLogging: false);
        }

        var clientApp = clientAppBuilder.Build();

        return new CacheIdentityClient(getTokenAsync: (cancellationToken) => clientApp.AcquireTokenForManagedIdentity(options.Scope).ExecuteAsync(cancellationToken));
    }

    internal static ICacheIdentityClient CreateForServicePrincipal(AzureCacheOptions options, ILogger? log)
    {
        var clientAppBuilder = ConfidentialClientApplicationBuilder.Create(options.ClientId)
            .WithCloudAuthority(options)
            .WithCredentials(options);

        if (log is not null)
        {
            clientAppBuilder = clientAppBuilder.WithLogging((level, message, containsPii) => IdentityLog(level, message, containsPii, log), Identity.Client.LogLevel.Warning, enablePiiLogging: false, enableDefaultPlatformLogging: false);
        }

        var clientApp = clientAppBuilder.Build();

        return new CacheIdentityClient(getTokenAsync: (cancellationToken) => clientApp.AcquireTokenForClient(new[] { options.Scope }).ExecuteAsync(cancellationToken));
    }

    internal static ICacheIdentityClient CreateForTokenCredential(TokenCredential tokenCredential, string scope)
    {
        var tokenRequestContext = new TokenRequestContext(new[] { scope });

        return new CacheIdentityClient(
            getToken: (cancellationToken) => tokenCredential.GetToken(tokenRequestContext, cancellationToken),
            getTokenAsync: (cancellationToken) => tokenCredential.GetTokenAsync(tokenRequestContext, cancellationToken));
    }

    TokenResult ICacheIdentityClient.GetToken(CancellationToken cancellationToken)
    {
        if (_getToken is not null)
        {
            return _getToken.Invoke(cancellationToken);
        }
        else
        {
            throw new InvalidOperationException("Synchronous token fetch not supported");
        }
    }

    async Task<TokenResult> ICacheIdentityClient.GetTokenAsync(CancellationToken cancellationToken)
        => await _getTokenAsync.Invoke(cancellationToken).ConfigureAwait(false);

    private static void IdentityLog(Identity.Client.LogLevel level, string message, bool containsPii, ILogger log)
    {
        if (containsPii)
        {
            return;
        }

        var logLevel = level switch
        {
            Identity.Client.LogLevel.Always => Extensions.Logging.LogLevel.Information,
            Identity.Client.LogLevel.Error => Extensions.Logging.LogLevel.Error,
            Identity.Client.LogLevel.Warning => Extensions.Logging.LogLevel.Warning,
            Identity.Client.LogLevel.Info => Extensions.Logging.LogLevel.Information,
            Identity.Client.LogLevel.Verbose => Extensions.Logging.LogLevel.Debug,
            _ => Extensions.Logging.LogLevel.None
        };

        log.Log(logLevel, message);
    }

}

internal static class ConfidentialClientApplicationBuilderExtensions
{
    internal static ConfidentialClientApplicationBuilder WithCloudAuthority(this ConfidentialClientApplicationBuilder builder, AzureCacheOptions options)
        => options.CloudUri is null ?
            builder.WithAuthority(options.Cloud, options.ServicePrincipalTenantId)
            : builder.WithAuthority(options.CloudUri, options.ServicePrincipalTenantId);

    internal static ConfidentialClientApplicationBuilder WithCredentials(this ConfidentialClientApplicationBuilder builder, AzureCacheOptions options)
        => options.ServicePrincipalCertificate is null ?
            builder.WithClientSecret(options.ServicePrincipalSecret)
            : builder.WithCertificate(options.ServicePrincipalCertificate, options.SendX5C);

}

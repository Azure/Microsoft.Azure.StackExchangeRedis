// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;
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
    /// <returns>A TokenResult containing a token and expiration</returns>
    Task<TokenResult> GetTokenAsync();
}

internal class CacheIdentityClient : ICacheIdentityClient
{
    private readonly Func<Task<TokenResult>> _getToken;

    private CacheIdentityClient(Func<ValueTask<AccessToken>> getToken)
        => _getToken = async () => new TokenResult(await getToken.Invoke().ConfigureAwait(false));

    private CacheIdentityClient(Func<Task<AuthenticationResult>> getToken)
        => _getToken = async () => new TokenResult(await getToken.Invoke().ConfigureAwait(false));

    internal static ICacheIdentityClient CreateForManagedIdentity(AzureCacheOptions options)
    {
        var clientApp = ManagedIdentityApplicationBuilder.Create(
                  options.ClientId is null ?
                      ManagedIdentityId.SystemAssigned
                      : Guid.TryParse(options.ClientId, out _) ?
                          ManagedIdentityId.WithUserAssignedClientId(options.ClientId)
                          : ManagedIdentityId.WithUserAssignedResourceId(options.ClientId))
                  .Build();

        return new CacheIdentityClient(getToken: () => clientApp.AcquireTokenForManagedIdentity(options.Scope).ExecuteAsync());
    }

    internal static ICacheIdentityClient CreateForServicePrincipal(AzureCacheOptions options)
    {
        var clientApp = ConfidentialClientApplicationBuilder.Create(options.ClientId)
            .WithCloudAuthority(options)
            .WithCredentials(options)
            .Build();

        return new CacheIdentityClient(getToken: () => clientApp.AcquireTokenForClient(new[] { options.Scope }).ExecuteAsync());
    }

    internal static ICacheIdentityClient CreateForTokenCredential(TokenCredential tokenCredential, string scope)
    {
        var tokenRequestContext = new TokenRequestContext(new[] { scope });

        return new CacheIdentityClient(getToken: () => tokenCredential.GetTokenAsync(tokenRequestContext, CancellationToken.None));
    }

    async Task<TokenResult> ICacheIdentityClient.GetTokenAsync()
        => await _getToken.Invoke().ConfigureAwait(false);

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

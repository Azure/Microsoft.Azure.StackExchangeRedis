// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Security.Cryptography.X509Certificates;
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
    private static readonly string[] s_azureCacheForRedisScopes = { "https://redis.azure.com/.default" };

    private readonly Func<Task<TokenResult>> _getToken;

    internal static ICacheIdentityClient CreateForSystemAssignedManagedIdentity()
        => new CacheIdentityClient(ManagedIdentityApplicationBuilder.Create(ManagedIdentityId.SystemAssigned)
            .Build());

    internal static ICacheIdentityClient CreateForUserAssignedManagedIdentity(string id)
        => new CacheIdentityClient(ManagedIdentityApplicationBuilder.Create(Guid.TryParse(id, out _) ? ManagedIdentityId.WithUserAssignedClientId(id) : ManagedIdentityId.WithUserAssignedResourceId(id))
            .Build());

    internal static ICacheIdentityClient CreateForServicePrincipal(string clientId, string tenantId, string secret, AzureCloudInstance cloud)
        => new CacheIdentityClient(ConfidentialClientApplicationBuilder.Create(clientId)
            .WithAuthority(cloud, tenantId)
            .WithClientSecret(secret)
            .Build());

    internal static ICacheIdentityClient CreateForServicePrincipal(string clientId, string tenantId, string secret, string cloudUri)
        => new CacheIdentityClient(ConfidentialClientApplicationBuilder.Create(clientId)
            .WithAuthority(cloudUri, tenantId)
            .WithClientSecret(secret)
            .Build());

    internal static ICacheIdentityClient CreateForServicePrincipal(string clientId, string tenantId, X509Certificate2 certificate, AzureCloudInstance cloud)
        => new CacheIdentityClient(ConfidentialClientApplicationBuilder.Create(clientId)
            .WithAuthority(cloud, tenantId)
            .WithCertificate(certificate)
            .Build());

    internal static ICacheIdentityClient CreateForServicePrincipal(string clientId, string tenantId, X509Certificate2 certificate, string cloudUri)
        => new CacheIdentityClient(ConfidentialClientApplicationBuilder.Create(clientId)
            .WithAuthority(cloudUri, tenantId)
            .WithCertificate(certificate)
            .Build());

    internal static ICacheIdentityClient CreateForTokenCredential(TokenCredential tokenCredential)
        => new CacheIdentityClient(tokenCredential);

    private CacheIdentityClient(IManagedIdentityApplication managedIdentityApplication)
        => _getToken = async () => new TokenResult(await managedIdentityApplication.AcquireTokenForManagedIdentity(s_azureCacheForRedisScopes[0])
            .ExecuteAsync().ConfigureAwait(false));

    private CacheIdentityClient(IConfidentialClientApplication confidentialClientApplication)
        => _getToken = async () => new TokenResult(await confidentialClientApplication.AcquireTokenForClient(s_azureCacheForRedisScopes)
            .ExecuteAsync().ConfigureAwait(false));

    private CacheIdentityClient(TokenCredential tokenCredential)
        => _getToken = async () => new TokenResult(await tokenCredential.GetTokenAsync(new TokenRequestContext(s_azureCacheForRedisScopes), CancellationToken.None));

    public async Task<TokenResult> GetTokenAsync()
        => await _getToken.Invoke().ConfigureAwait(false);

}

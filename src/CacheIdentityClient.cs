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
/// Acquires tokens from Azure Active Directory for authenticating connections to Azure Cache for Redis.
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
    private static readonly string[] s_azureCacheForRedisScopes = { "acca5fbb-b7e4-4009-81f1-37e38fd66d78/.default" };

    private readonly Func<Task<TokenResult>> _getToken;

    internal static ICacheIdentityClient CreateForSystemAssignedManagedIdentity()
        => new CacheIdentityClient(ManagedIdentityApplicationBuilder.Create(ManagedIdentityId.SystemAssigned)
            .Build());

    internal static ICacheIdentityClient CreateForUserAssignedManagedIdentity(string id)
        => new CacheIdentityClient(ManagedIdentityApplicationBuilder.Create(Guid.TryParse(id, out _) ? ManagedIdentityId.WithUserAssignedClientId(id) : ManagedIdentityId.WithUserAssignedResourceId(id))
            .Build());

    internal static ICacheIdentityClient CreateForServicePrincipal(string clientId, string tenantId, string secret)
        => new CacheIdentityClient(ConfidentialClientApplicationBuilder.Create(clientId)
            .WithTenantId(tenantId)
            .WithClientSecret(secret)
            .Build());

    internal static ICacheIdentityClient CreateForTokenCredential(TokenCredential tokenCredential)
        => new CacheIdentityClient(tokenCredential);

    private CacheIdentityClient(IManagedIdentityApplication managedIdentityApplication)
        => _getToken = async () => new TokenResult(await managedIdentityApplication.AcquireTokenForManagedIdentity(s_azureCacheForRedisScopes[0])
            .ExecuteAsync().ConfigureAwait(false));

    private CacheIdentityClient(IConfidentialClientApplication confidentialClientApplication)
        => _getToken = async () => new TokenResult(await confidentialClientApplication!.AcquireTokenForClient(s_azureCacheForRedisScopes)
            .ExecuteAsync().ConfigureAwait(false));

    private CacheIdentityClient(TokenCredential tokenCredential)
        => _getToken = async () => new TokenResult(await tokenCredential.GetTokenAsync(new TokenRequestContext(s_azureCacheForRedisScopes), CancellationToken.None));

    public async Task<TokenResult> GetTokenAsync()
        => await _getToken.Invoke().ConfigureAwait(false);

}

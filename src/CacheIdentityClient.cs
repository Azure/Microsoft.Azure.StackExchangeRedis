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
    /// Acquire a token from the Microsoft Identity Client.
    /// </summary>
    /// <param name="forceRefresh">Pass <see langword="false"/> to quickly acquire a cached token for a new connection, and <see langword="true"/> for subsequent token refreshes.</param>
    /// <returns>Authentication result containing a token.</returns>
    Task<TokenResult> GetTokenAsync(bool forceRefresh);

    /// <summary>
    /// Acquire a token using Token Credential.
    /// </summary>
    /// <returns>Access token.</returns>
    Task<TokenResult> GetTokenAsync();
}

internal class CacheIdentityClient : ICacheIdentityClient
{
    private static readonly string[] s_azureCacheForRedisScopes = { "acca5fbb-b7e4-4009-81f1-37e38fd66d78/.default" };

    private readonly Func<bool, Task<AuthenticationResult>>? _getToken;

    private readonly Func<Task<AccessToken>>? _getTokenCredentialToken;

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

    internal static ICacheIdentityClient CreateForTokenCredential(TokenCredential tokenCredential, CancellationToken cancellationToken)
        => new CacheIdentityClient(tokenCredential, cancellationToken);

    private CacheIdentityClient(IManagedIdentityApplication managedIdentityApplication)
        => _getToken = async (bool forceRefresh) => await managedIdentityApplication.AcquireTokenForManagedIdentity(s_azureCacheForRedisScopes[0])
            .WithForceRefresh(forceRefresh)
            .ExecuteAsync().ConfigureAwait(false);

    private CacheIdentityClient(IConfidentialClientApplication confidentialClientApplication)
        => _getToken = async (bool forceRefresh) => await confidentialClientApplication!.AcquireTokenForClient(s_azureCacheForRedisScopes)
            .WithForceRefresh(forceRefresh)
            .ExecuteAsync().ConfigureAwait(false);

    private CacheIdentityClient(TokenCredential tokenCredential, CancellationToken cancellationToken)
        => _getTokenCredentialToken = async () => await tokenCredential.GetTokenAsync(new TokenRequestContext(s_azureCacheForRedisScopes), cancellationToken);

    public async Task<TokenResult> GetTokenAsync(bool forceRefresh)
    {
        AuthenticationResult result = await _getToken!.Invoke(forceRefresh).ConfigureAwait(false);
        return new TokenResult(result.AccessToken, result.ExpiresOn);
    }

    public async Task<TokenResult> GetTokenAsync()
    {
        AccessToken token = await _getTokenCredentialToken!.Invoke().ConfigureAwait(false);
        return new TokenResult(token.Token, token.ExpiresOn);
    }
}

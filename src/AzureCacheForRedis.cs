// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Threading.Tasks;
using Microsoft.Azure.StackExchangeRedis;

namespace StackExchange.Redis;

/// <summary>
/// An extension for StackExchange.Redis for configuring connections to Azure Cache for Redis resources.
/// </summary>
public static class AzureCacheForRedis
{
    /// <summary>
    /// Configures a Redis connection authenticated with an access key.
    /// </summary>
    public static Action<ConfigurationOptions> ConfigureForAzure
        => (ConfigurationOptions configurationOptions) => configurationOptions.Defaults = new AzureCacheOptionsProvider();

    /// <summary>
    /// Configures a Redis connection authenticated using a system-assigned managed identity.
    /// Throws on connection failure by default (configurable in the <see cref="ConfigureForAzureAsync"/> method).
    /// </summary>
    /// <param name="configurationOptions">The configuration to update.</param>
    /// <param name="principalId">Principal (object) ID of the client resource's system-assigned managed identity.</param>
    public static async Task<ConfigurationOptions> ConfigureForAzureWithSystemAssignedManagedIdentityAsync(this ConfigurationOptions configurationOptions, string principalId)
        => await ConfigureForAzureAsync(
            configurationOptions,
            new AzureCacheOptions()
            {
                PrincipalId = principalId,
            }).ConfigureAwait(false);

    /// <summary>
    /// Configures a Redis connection authenticated using a user-assigned managed identity.
    /// Throws on connection failure by default (configurable in the <see cref="ConfigureForAzureAsync"/> method).
    /// </summary>
    /// <param name="configurationOptions">The configuration to update.</param>
    /// <param name="clientId">Client ID of the user-assigned managed identity.</param>
    /// <param name="principalId">Principal (object) ID of the user-assigned managed identity.</param>
    public static async Task<ConfigurationOptions> ConfigureForAzureWithUserAssignedManagedIdentityAsync(this ConfigurationOptions configurationOptions, string clientId, string principalId)
        => await ConfigureForAzureAsync(
            configurationOptions,
            new AzureCacheOptions()
            {
                ClientId = clientId,
                PrincipalId = principalId,
            }).ConfigureAwait(false);

    /// <summary>
    /// Configures a Redis connection authenticated using a service principal.
    /// NOTE: Service principal authentication should only be used in scenarios where managed identity CANNOT be used.
    /// Throws on connection failure by default (configurable in the <see cref="ConfigureForAzureAsync"/> method).
    /// </summary>
    /// <param name="configurationOptions">The configuration to update.</param>
    /// <param name="clientId">Client ID of the service principal.</param>
    /// <param name="principalId">Principal (object) ID of the service principal.</param>
    /// <param name="tenantId">Tenant ID of the service principal.</param>
    /// <param name="secret">Service principal secret.</param>
    public static async Task<ConfigurationOptions> ConfigureForAzureWithServicePrincipalAsync(this ConfigurationOptions configurationOptions, string clientId, string principalId, string tenantId, string secret)
        => await ConfigureForAzureAsync(
            configurationOptions,
            new AzureCacheOptions()
            {
                ClientId = clientId,
                PrincipalId = principalId,
                ServicePrincipalTenantId = tenantId,
                ServicePrincipalSecret = secret,
            }).ConfigureAwait(false);

    /// <summary>
    /// Configures a connection to an Azure Cache for Redis using advanced options.
    /// </summary>
    /// <param name="configurationOptions">The configuration to update.</param>
    /// <param name="azureCacheOptions">Options for configuring a connection to an Azure Cache for Redis.</param>
    public static async Task<ConfigurationOptions> ConfigureForAzureAsync(
        this ConfigurationOptions configurationOptions,
        AzureCacheOptions azureCacheOptions)
    {
        var optionsProvider = new AzureCacheOptionsProviderWithToken(azureCacheOptions);

        await optionsProvider.AcquireTokenAsync(forceRefresh: false, azureCacheOptions.ThrowOnTokenRefreshFailure).ConfigureAwait(false);
        
        configurationOptions.Defaults = optionsProvider;

        return configurationOptions;
    }

}

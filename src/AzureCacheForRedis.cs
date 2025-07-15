// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Azure.Core;
using Microsoft.Azure.StackExchangeRedis;
using Microsoft.Identity.Client;

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
        => configurationOptions => configurationOptions.Defaults = new AzureCacheOptionsProvider();

    /// <summary>
    /// Configures a Redis connection authenticated using a system-assigned managed identity.
    /// Throws on failure by default (configurable in the <see cref="ConfigureForAzureAsync"/> method).
    /// </summary>
    /// <param name="configurationOptions">The configuration to update.</param>
    /// <exception cref="MsalServiceException">When the token source is not supported or identified incorrectly.</exception>
    /// <exception cref="HttpRequestException">Unable to contact the identity service to acquire a token.</exception>
    public static async Task<ConfigurationOptions> ConfigureForAzureWithSystemAssignedManagedIdentityAsync(this ConfigurationOptions configurationOptions)
        => await ConfigureForAzureAsync(
            configurationOptions,
            new AzureCacheOptions()).ConfigureAwait(false);

    /// <summary>
    /// Configures a Redis connection authenticated using a user-assigned managed identity.
    /// Throws on failure by default (configurable in the <see cref="ConfigureForAzureAsync"/> method).
    /// </summary>
    /// <param name="configurationOptions">The configuration to update.</param>
    /// <param name="clientId">Client ID or resource ID of the user-assigned managed identity.</param>
    /// <exception cref="MsalServiceException">When the token source is not supported or identified incorrectly.</exception>
    /// <exception cref="HttpRequestException">Unable to contact the identity service to acquire a token.</exception>
    public static async Task<ConfigurationOptions> ConfigureForAzureWithUserAssignedManagedIdentityAsync(this ConfigurationOptions configurationOptions, string clientId)
        => await ConfigureForAzureAsync(
            configurationOptions,
            new AzureCacheOptions()
            {
                ClientId = clientId,
            }).ConfigureAwait(false);

    /// <summary>
    /// Configures a Redis connection authenticated using a service principal.
    /// NOTE: Service principal authentication should only be used in scenarios where managed identity CANNOT be used.
    /// Throws on failure by default (configurable in the <see cref="ConfigureForAzureAsync"/> method).
    /// </summary>
    /// <param name="configurationOptions">The configuration to update.</param>
    /// <param name="clientId">Client ID of the service principal.</param>
    /// <param name="tenantId">Tenant ID of the service principal.</param>
    /// <param name="secret">Service principal secret. Either <paramref name="secret"/> or <paramref name="certificate"/> must be provided</param>
    /// <param name="certificate">Service principal certificate. Either <paramref name="certificate"/> or <paramref name="secret"/> must be provided.</param>
    /// <param name="cloud">Optional. Provide a value to use an Azure cloud other than the Public cloud.</param>
    /// <param name="cloudUri">Optional. Provide a value to use an Azure cloud not included in <see cref="AzureCloudInstance"/>. URI format will be similar to <c>https://login.microsoftonline.com)</c></param>
    /// <exception cref="MsalServiceException">When the token source is not supported or identified incorrectly.</exception>
    /// <exception cref="HttpRequestException">Unable to contact the identity service to acquire a token.</exception>
    public static async Task<ConfigurationOptions> ConfigureForAzureWithServicePrincipalAsync(this ConfigurationOptions configurationOptions, string clientId, string tenantId, string? secret = null, X509Certificate2? certificate = null, AzureCloudInstance cloud = AzureCloudInstance.AzurePublic, string? cloudUri = null)
        => await ConfigureForAzureAsync(
            configurationOptions,
            new AzureCacheOptions()
            {
                ClientId = clientId,
                ServicePrincipalTenantId = tenantId,
                ServicePrincipalSecret = secret,
                ServicePrincipalCertificate = certificate,
                Cloud = cloud,
                CloudUri = cloudUri,
            }).ConfigureAwait(false);

    /// <summary>
    /// Configures a Redis connection authenticated using a TokenCredential.
    /// </summary>
    /// <param name="configurationOptions">The configuration to update.</param>
    /// <param name="tokenCredential">The TokenCredential to be used.</param>
    public static async Task<ConfigurationOptions> ConfigureForAzureWithTokenCredentialAsync(this ConfigurationOptions configurationOptions, TokenCredential tokenCredential)
        => await ConfigureForAzureAsync(
            configurationOptions,
            new AzureCacheOptions()
            {
                TokenCredential = tokenCredential
            }).ConfigureAwait(false);

    /// <summary>
    /// Configures a connection to an Azure Cache for Redis using advanced options.
    /// </summary>
    /// <param name="configurationOptions">The configuration to update.</param>
    /// <param name="azureCacheOptions">Options for configuring a connection to an Azure Cache for Redis.</param>
    /// <exception cref="MsalServiceException">When the token source is not supported or identified incorrectly.</exception>
    /// <exception cref="HttpRequestException">Unable to contact the identity service to acquire a token.</exception>
    public static async Task<ConfigurationOptions> ConfigureForAzureAsync(
        this ConfigurationOptions configurationOptions,
        AzureCacheOptions azureCacheOptions)
    {
        var optionsProvider = new AzureCacheOptionsProviderWithToken(azureCacheOptions, configurationOptions.LoggerFactory);

        try
        {
            await optionsProvider.AcquireTokenAsync(throwOnFailure: true).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to acquire token", ex);
        }

        configurationOptions.Defaults = optionsProvider;
        optionsProvider._user = configurationOptions.User ?? azureCacheOptions.GetUserName(configurationOptions.Password);

        return configurationOptions;
    }

}

// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;

namespace Microsoft.Azure.StackExchangeRedis;

/// <summary>
/// Options for configuring connections to Azure Cache for Redis.
/// </summary>
public class AzureCacheOptions
{
    /// <summary>
    /// Principal (object) ID of a managed identity or a service principal used to authenticate a connection to Redis.
    /// Required when connecting with a managed identity or service principal.
    /// </summary>
    public string? PrincipalId;

    /// <summary>
    /// Client ID of a managed identity or a service principal used to authenticate a connection to Redis.
    /// Required when connecting with a user-assigned managed identity or a service principal.
    /// </summary>
    public string? ClientId;

    /// <summary>
    /// Tenant ID of a service principal used to authenticate a connection to Redis.
    /// </summary>
    public string? ServicePrincipalTenantId;

    /// <summary>
    /// Service principal secret used to authenticate a connection to Redis.
    /// </summary>
    public string? ServicePrincipalSecret;

    /// <summary>
    /// Whether or not to throw an exception on failure to refresh an expiring AAD token.
    /// </summary>
    public bool ThrowOnTokenRefreshFailure = true;

    /// <summary>
    /// How long before expiration should a token be refreshed. 
    /// Tokens have a 24hr lifespan by default, and the default margin of 4 hours allows time for recovery of any issues preventing refresh before the token expires.
    /// </summary>
    internal TimeSpan TokenExpirationMargin = TimeSpan.FromHours(4);

    /// <summary>
    /// Periodic interval to check token for expiration, acquire new tokens, and re-authenticate connections.
    /// </summary>
    internal TimeSpan TokenHeartbeatInterval = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Number of attempts to acquire a new token.
    /// If none of the attempts succeed, the same number of attempts will be made at the next timer interval.
    /// </summary>
    internal int MaxTokenRefreshAttempts = 5;

    /// <summary>
    /// Determines the interval between attempts to acquire a new token.
    /// By default, wait a number of seconds corresponding to the number of attempts completed.
    /// </summary>
    internal Func<int, Exception?, TimeSpan> TokenRefreshBackoff = (int attemptCount, Exception? _) => TimeSpan.FromSeconds(attemptCount);

}

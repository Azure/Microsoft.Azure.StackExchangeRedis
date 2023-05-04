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
    /// Check token every 5 minutes by default and refresh if necessary.
    /// </summary>
    internal TimeSpan TokenHeartbeatInterval = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Refresh token 4 hours before it expires by default.
    /// Tokens have a 24hr lifespan by default, so this is a generous margin to allow time for recovery of any issues preventing refresh before the token expires.
    /// </summary>
    internal TimeSpan TokenExpirationMargin = TimeSpan.FromHours(4);

    /// <summary>
    /// Attempt to acquire a fresh token 5 times before giving up.
    /// </summary>
    internal int MaxTokenRefreshAttempts = 5;

}

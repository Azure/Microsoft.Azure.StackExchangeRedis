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
    /// Determines whether the current token should be refreshed, based on its age and lifespan
    /// </summary>
    public Func<DateTime, DateTime, bool> ShouldTokenBeRefreshed = (DateTime acquired, DateTime expiry) =>
    {
        var lifespan = expiry - acquired;
        var age = DateTime.UtcNow - acquired;

        return (age.Ticks / lifespan.Ticks) > .75; // Refresh if current token has exceeded 75% of its lifespan
    };

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
    /// The function receives an integer indicating how many attempts have been made and the exception necessitating a retry. It returns a TimeSpan that determines the interval to wait before the next attempt.
    /// By default, wait a number of seconds corresponding to the number of attempts completed.
    /// </summary>
    internal Func<int, Exception?, TimeSpan> TokenRefreshBackoff = (int attemptCount, Exception? _) => TimeSpan.FromSeconds(attemptCount);

}

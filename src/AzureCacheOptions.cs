﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Security.Cryptography.X509Certificates;
using Azure.Core;
using Microsoft.Identity.Client;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Microsoft.Azure.StackExchangeRedis;

/// <summary>
/// Options for configuring connections to Azure Cache for Redis.
/// </summary>
public class AzureCacheOptions
{
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
    /// Service principal certificate used to authenticate a connection to Redis.
    /// </summary>
    public X509Certificate2? ServicePrincipalCertificate;

    /// <summary>
    /// Azure cloud where the application is running. Defaults to the Public cloud. To use a sovereign cloud, set to the appropriate <see cref="AzureCloudInstance"/>
    /// </summary>
    public AzureCloudInstance Cloud = AzureCloudInstance.AzurePublic;

    /// <summary>
    /// URI for the Azure cloud where the application is running. Use this for clouds not included in <see cref="AzureCloudInstance"/>
    /// </summary>
    public string? CloudUri;

    /// <summary>
    /// Scope identifier for the type of Redis resource. To connect to an Azure Redis resource, leave this as the default "https://redis.azure.com/.default"
    /// </summary>
    public string Scope = "https://redis.azure.com/.default";

    /// <summary>
    /// Enables Subject Name + Issuer authentication of certificates (Microsoft internal use only).
    /// </summary>
    public bool SendX5C = false;

    /// <summary>
    /// TokenCredential used to authenticate a connection to Redis.
    /// </summary>
    public TokenCredential? TokenCredential;

    /// <summary>
    /// Whether or not to throw an exception on failure to refresh an expiring Microsoft Entra ID token.
    /// </summary>
    public bool ThrowOnTokenRefreshFailure = true;

    /// <summary>
    /// Determines whether the current token should be refreshed, based on its age and lifespan.
    /// The default implementation refreshes the token if it has exceeded 75% of its lifespan.
    /// Supply a different implementation if you want to refresh the token at a different interval.
    /// </summary>
    public Func<DateTime, DateTime, bool> ShouldTokenBeRefreshed = (DateTime acquired, DateTime expiry) =>
    {
        var lifespan = expiry - acquired;
        var age = DateTime.UtcNow - acquired;

        return ((double)age.Ticks / lifespan.Ticks) > .75; // Refresh if current token has exceeded 75% of its lifespan
    };

    /// <summary>
    /// Given an access token, produce the Redis user name to be used for authentication.
    /// The default implementation extracts the 'oid' claim from the token using Microsoft.IdentityModel.JsonWebTokens.
    /// Supply a different implementation if you need to use a different approach to determine the user name.
    /// </summary>
    public Func<string?, string> GetUserName = (string? token) =>
    {
        if (token is null)
        {
            throw new ArgumentNullException(nameof(token));
        }

        var jwtHandler = new JsonWebTokenHandler();

        if (!jwtHandler.CanReadToken(token))
        {
            throw new Exception("Invalid token cannot be read");
        }

        var jwt = jwtHandler.ReadJsonWebToken(token);

        if (jwt.TryGetClaim("oid", out var oid))
        {
            return oid.Value;
        }
        else
        {
            throw new Exception("oid not found in token claims");
        }
    };

    /// <summary>
    /// Periodic interval to check token for expiration, acquire new tokens, and re-authenticate connections.
    /// </summary>
    internal TimeSpan TokenHeartbeatInterval = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Number of attempts to acquire a new token (default is 5).
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

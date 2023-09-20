﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;

namespace Microsoft.Azure.StackExchangeRedis;

/// <summary>
/// Events raised to notify Redis client applications about authentication token management operations.
/// </summary>
public interface IAzureCacheTokenEvents
{
    /// <summary>
    /// Raised when a TokenResult token is refreshed.
    /// The AccessToken argument passed contains expiration along with the Tokens value.
    /// </summary>
    event EventHandler<TokenResult>? TokenRefreshed;

    /// <summary>
    /// Raised when an attempt to refresh an authentication token fails.
    /// </summary>
    event EventHandler<TokenRefreshFailedEventArgs>? TokenRefreshFailed;

    /// <summary>
    /// Raised when a Redis connection is re-authenticated.
    /// The string argument passed to subscribers identifies the server endpoint that was re-authenticated.
    /// </summary>
    event EventHandler<string>? ConnectionReauthenticated;

    /// <summary>
    /// Raised when an attempt to re-authenticate a Redis connection fails.
    /// </summary>
    event EventHandler<ConnectionReauthenticationFailedEventArgs>? ConnectionReauthenticationFailed;
}

/// <summary>
/// Contains information about a token refresh failure.
/// </summary>
public class TokenRefreshFailedEventArgs : EventArgs
{
    internal TokenRefreshFailedEventArgs(Exception? exception, DateTime? expiry)
    {
        Exception = exception;
        Expiry = expiry;
    }

    /// <summary>
    /// Gets the exception if available (this can be null).
    /// </summary>
    public Exception? Exception { get; }

    /// <summary>
    /// When token expires.
    /// </summary>
    public DateTime? Expiry { get; }
}

/// <summary>
/// Contains information about a failure to re-authenticate a Redis server connection.
/// </summary>
public class ConnectionReauthenticationFailedEventArgs : EventArgs
{
    internal ConnectionReauthenticationFailedEventArgs(Exception? exception, string endpoint)
    {
        Exception = exception;
        Endpoint = endpoint;
    }

    /// <summary>
    /// Gets the exception if available (this can be null).
    /// </summary>
    public Exception? Exception { get; }

    /// <summary>
    /// Server endpoint that failed to re-authenticate.
    /// </summary>
    public string? Endpoint { get; }
}

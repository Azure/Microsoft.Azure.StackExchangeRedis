// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;

namespace Microsoft.Azure.StackExchangeRedis;

/// <summary>
/// Result from getting a new token for authentication
/// </summary>
public class TokenResult
{
    /// <summary>
    /// Token to be used for authentication.
    /// </summary>
    public string Token { get; set; }

    /// <summary>
    /// Expiration for the acquired token.
    /// </summary>
    public DateTimeOffset ExpiresOn { get; set; }

    /// <summary>
    /// Create a TokenResult.
    /// </summary>
    /// <param name="token">The token acquired.</param>
    /// <param name="expiresOn">The expiration for the token.</param>
    public TokenResult(string token, DateTimeOffset expiresOn)
    {
        Token = token;
        ExpiresOn = expiresOn;
    }
}

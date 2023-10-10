// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using Azure.Core;
using Microsoft.Identity.Client;

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

    /// <summary>
    /// Creates a TokenResult from an AuthenticationResult.
    /// </summary>
    /// <param name="authenticationResult">An AuthenticationResult from getting a token through the Microsoft Identity Client.</param>
    public TokenResult(AuthenticationResult authenticationResult)
    {
        Token = authenticationResult.AccessToken;
        ExpiresOn = authenticationResult.ExpiresOn;
    }

    /// <summary>
    /// Creates a TokenResult from an AccessToken.
    /// </summary>
    /// <param name="accessToken">An AccessToken from a TokenCredential.</param>
    public TokenResult(AccessToken accessToken)
    {
        Token = accessToken.Token;
        ExpiresOn = accessToken.ExpiresOn;
    }
}

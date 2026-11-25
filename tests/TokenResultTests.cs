// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using Azure.Core;
using Microsoft.Identity.Client;

namespace Microsoft.Azure.StackExchangeRedis.Tests;

[TestClass]
public class TokenResultTests
{
    [TestMethod]
    public void FromAuthenticationResult_CopiesTokenAndExpiry()
    {
        var expiresOn = DateTimeOffset.UtcNow.AddMinutes(30);
        var authenticationResult = new AuthenticationResult(
            accessToken: "msal-token",
            isExtendedLifeTimeToken: false,
            uniqueId: null,
            expiresOn: expiresOn,
            extendedExpiresOn: expiresOn,
            tenantId: string.Empty,
            account: null,
            idToken: null,
            scopes: null,
            correlationId: default);

        var result = new TokenResult(authenticationResult);

        Assert.AreEqual("msal-token", result.Token);
        Assert.AreEqual(expiresOn, result.ExpiresOn);
    }

    [TestMethod]
    public void FromAccessToken_CopiesTokenAndExpiry()
    {
        var expiresOn = DateTimeOffset.UtcNow.AddMinutes(30);

        var result = new TokenResult(new AccessToken("credential-token", expiresOn));

        Assert.AreEqual("credential-token", result.Token);
        Assert.AreEqual(expiresOn, result.ExpiresOn);
    }

    [TestMethod]
    public void TokenRefreshFailedEventArgs_ExposesExceptionAndExpiry()
    {
        var exception = new InvalidOperationException();
        var expiry = DateTime.UtcNow;

        var args = new TokenRefreshFailedEventArgs(exception, expiry);

        Assert.AreSame(exception, args.Exception);
        Assert.AreEqual(expiry, args.Expiry);
    }

    [TestMethod]
    public void ConnectionReauthenticationFailedEventArgs_ExposesExceptionAndEndpoint()
    {
        var exception = new InvalidOperationException();

        var args = new ConnectionReauthenticationFailedEventArgs(exception, "host:6380");

        Assert.AreSame(exception, args.Exception);
        Assert.AreEqual("host:6380", args.Endpoint);
    }
}

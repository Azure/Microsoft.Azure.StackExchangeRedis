// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using Azure.Core;
using FakeItEasy;
using Microsoft.Identity.Client;
using StackExchange.Redis;

namespace Microsoft.Azure.StackExchangeRedis.Tests;

[TestClass]
public class AzureCacheOptionsProviderWithTokenTests
{
    [TestMethod]
    public async Task AcquireTokenAsync_Success()
    {
        // Arrange
        var authenticationResult = new AuthenticationResult(
            accessToken: "token",
            isExtendedLifeTimeToken: true,
            uniqueId: null,
            expiresOn: DateTime.UtcNow + TimeSpan.FromMinutes(1),
            extendedExpiresOn: DateTime.UtcNow + TimeSpan.FromMinutes(1),
            tenantId: string.Empty,
            account: null,
            idToken: null,
            scopes: null,
            correlationId: default);
        var tokenResult = new TokenResult(authenticationResult);
        var fakeIdentityClient = A.Fake<ICacheIdentityClient>();
        A.CallTo(() => fakeIdentityClient.GetTokenAsync()).Returns(tokenResult);

        var configurationOptions = new ConfigurationOptions();
        var azureCacheOptions = new AzureCacheOptions()
        {
            PrincipalId = "userName"
        };
        var optionsProviderWithToken = new AzureCacheOptionsProviderWithToken(azureCacheOptions);
        optionsProviderWithToken.IdentityClient = fakeIdentityClient; // Override the IIdentityClient created during instantiation of AzureCacheOptionsProviderWithToken 

        configurationOptions!.Defaults = optionsProviderWithToken;

        var fakeTokenRefreshedHandler = A.Fake<EventHandler<TokenResult>>();
        optionsProviderWithToken.TokenRefreshed += fakeTokenRefreshedHandler;

        // Execute
        await optionsProviderWithToken.AcquireTokenAsync(throwOnFailure: true);

        // Assert
        A.CallTo(() => fakeTokenRefreshedHandler.Invoke(optionsProviderWithToken, A<TokenResult>._)).MustHaveHappenedOnceExactly();
        A.CallTo(() => fakeIdentityClient.GetTokenAsync()).MustHaveHappenedOnceExactly();
        Assert.AreEqual("token", configurationOptions.Password);
    }

    [TestMethod]
    public async Task AcquireTokenAsync_UsingTokenCredential_Success()
    {
        // Arrange
        var token = new AccessToken("token", DateTimeOffset.UtcNow + TimeSpan.FromMinutes(1));
        var tokenResult = new TokenResult(token);
        var fakeTokenCredentialClient = A.Fake<ICacheIdentityClient>();
        A.CallTo(() => fakeTokenCredentialClient.GetTokenAsync()).Returns(tokenResult);
        var tokenCredential = A.Fake<TokenCredential>();

        var configurationOptions = new ConfigurationOptions();
        var azureCacheOptions = new AzureCacheOptions()
        {
            PrincipalId = "userName",
            TokenCredential = tokenCredential
        };
        var optionsProviderWithToken = new AzureCacheOptionsProviderWithToken(azureCacheOptions);
        optionsProviderWithToken.IdentityClient = fakeTokenCredentialClient; // Override the IIdentityClient created during instantiation of AzureCacheOptionsProviderWithToken
        configurationOptions!.Defaults = optionsProviderWithToken;

        var fakeTokenRefreshedHandler = A.Fake<EventHandler<TokenResult>>();
        optionsProviderWithToken.TokenRefreshed += fakeTokenRefreshedHandler;

        //Execute
        await optionsProviderWithToken.AcquireTokenAsync(throwOnFailure: true);

        //Assert
        A.CallTo(() => fakeTokenRefreshedHandler.Invoke(optionsProviderWithToken, A<TokenResult>._)).MustHaveHappenedOnceExactly();
        A.CallTo(() => fakeTokenCredentialClient.GetTokenAsync()).MustHaveHappenedOnceExactly();
        Assert.AreEqual("token", configurationOptions.Password);
    }

    // TODO: More tests

}

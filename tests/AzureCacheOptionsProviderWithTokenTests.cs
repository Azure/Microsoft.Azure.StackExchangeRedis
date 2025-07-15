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
        A.CallTo(() => fakeIdentityClient.GetTokenAsync(CancellationToken.None)).Returns(tokenResult);

        var configurationOptions = new ConfigurationOptions();
        var azureCacheOptions = new AzureCacheOptions();
        var optionsProviderWithToken = new AzureCacheOptionsProviderWithToken(azureCacheOptions, configurationOptions.LoggerFactory)
        {
            IdentityClient = fakeIdentityClient // Override the IIdentityClient created during instantiation of AzureCacheOptionsProviderWithToken 
        };

        var fakeTokenRefreshedHandler = A.Fake<EventHandler<TokenResult>>();
        optionsProviderWithToken.TokenRefreshed += fakeTokenRefreshedHandler;

        configurationOptions!.Defaults = optionsProviderWithToken;

        // Execute
        await optionsProviderWithToken.AcquireTokenAsync(throwOnFailure: true);

        // Assert
        A.CallTo(() => fakeTokenRefreshedHandler.Invoke(optionsProviderWithToken, A<TokenResult>._)).MustHaveHappenedOnceExactly();
        A.CallTo(() => fakeIdentityClient.GetTokenAsync(CancellationToken.None)).MustHaveHappenedOnceExactly();
        Assert.AreEqual("token", configurationOptions.Password);
    }

    [TestMethod]
    public async Task AcquireTokenAsync_UsingTokenCredential_Success()
    {
        // Arrange
        var token = new AccessToken("token", DateTimeOffset.UtcNow + TimeSpan.FromMinutes(1));
        var tokenResult = new TokenResult(token);
        var fakeTokenCredentialClient = A.Fake<ICacheIdentityClient>();
        A.CallTo(() => fakeTokenCredentialClient.GetTokenAsync(CancellationToken.None)).Returns(tokenResult);
        var tokenCredential = A.Fake<TokenCredential>();

        var configurationOptions = new ConfigurationOptions();
        var azureCacheOptions = new AzureCacheOptions()
        {
            TokenCredential = tokenCredential
        };
        var optionsProviderWithToken = new AzureCacheOptionsProviderWithToken(azureCacheOptions, configurationOptions.LoggerFactory)
        {
            IdentityClient = fakeTokenCredentialClient // Override the IIdentityClient created during instantiation of AzureCacheOptionsProviderWithToken
        };

        var fakeTokenRefreshedHandler = A.Fake<EventHandler<TokenResult>>();
        optionsProviderWithToken.TokenRefreshed += fakeTokenRefreshedHandler;

        configurationOptions.Defaults = optionsProviderWithToken;

        //Execute
        await optionsProviderWithToken.AcquireTokenAsync(throwOnFailure: true);

        //Assert
        A.CallTo(() => fakeTokenRefreshedHandler.Invoke(optionsProviderWithToken, A<TokenResult>._)).MustHaveHappenedOnceExactly();
        A.CallTo(() => fakeTokenCredentialClient.GetTokenAsync(CancellationToken.None)).MustHaveHappenedOnceExactly();
        Assert.AreEqual("token", configurationOptions.Password);
    }

    // TODO: More tests

}

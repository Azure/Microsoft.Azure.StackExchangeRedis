// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

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
        var fakeIdentityClient = A.Fake<ICacheIdentityClient>();
        A.CallTo(() => fakeIdentityClient.GetTokenAsync(A<bool>._)).Returns(authenticationResult);

        var configurationOptions = new ConfigurationOptions();
        var azureCacheOptions = new AzureCacheOptions()
        {
            PrincipalId = "userName"
        };
        var optionsProviderWithToken = new AzureCacheOptionsProviderWithToken(azureCacheOptions);
        optionsProviderWithToken.IdentityClient = fakeIdentityClient; // Override the IIdentityClient created during instantiation of AzureCacheOptionsProviderWithToken 

        configurationOptions!.Defaults = optionsProviderWithToken;

        var fakeTokenRefreshedHandler = A.Fake<EventHandler<AuthenticationResult>>();
        optionsProviderWithToken.TokenRefreshed += fakeTokenRefreshedHandler;

        // Execute
        await optionsProviderWithToken.AcquireTokenAsync(forceRefresh: false, throwOnFailure: true);

        // Assert
        A.CallTo(() => fakeTokenRefreshedHandler.Invoke(optionsProviderWithToken, A<AuthenticationResult>._)).MustHaveHappenedOnceExactly();
        A.CallTo(() => fakeIdentityClient.GetTokenAsync(false)).MustHaveHappenedOnceExactly();
        Assert.AreEqual("token", configurationOptions.Password);
    }

    // TODO: More tests

}

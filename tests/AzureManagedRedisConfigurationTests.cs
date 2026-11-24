// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using Azure.Core;
using FakeItEasy;
using Microsoft.Identity.Client;
using StackExchange.Redis;

namespace Microsoft.Azure.StackExchangeRedis.Tests;

[TestClass]
public class AzureManagedRedisConfigurationTests
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
        var fakeTokenServiceClient = A.Fake<ITokenServiceClient>();
        A.CallTo(() => fakeTokenServiceClient.GetTokenAsync(A<CancellationToken>._)).Returns(tokenResult);

        var configurationOptions = new ConfigurationOptions();
        var authenticationOptions = new AuthenticationOptions()
        {
            GetUserName = (token) => "user"
        };

        var amrConfig = new AzureManagedRedisConfiguration(authenticationOptions, configurationOptions.LoggerFactory);
        amrConfig.TokenManager.TokenServiceClient = fakeTokenServiceClient; // Override the IIdentityClient created during instantiation of TokenManager

        var fakeTokenRefreshedHandler = A.Fake<EventHandler<TokenResult>>();
        amrConfig.TokenManager.TokenRefreshed += fakeTokenRefreshedHandler;

        configurationOptions!.Defaults = amrConfig;

        // Execute
        await amrConfig.TokenManager.AcquireTokenAsync(throwOnFailure: true);

        // Assert
        A.CallTo(() => fakeTokenRefreshedHandler.Invoke(amrConfig.TokenManager, A<TokenResult>._)).MustHaveHappenedOnceExactly();
        A.CallTo(() => fakeTokenServiceClient.GetTokenAsync(A<CancellationToken>._)).MustHaveHappenedOnceExactly();
        Assert.AreEqual("token", amrConfig.TokenManager.Token);
    }

    [TestMethod]
    public async Task AcquireTokenAsync_UsingTokenCredential_Success()
    {
        // Arrange
        var token = new AccessToken("token", DateTimeOffset.UtcNow + TimeSpan.FromMinutes(1));
        var tokenResult = new TokenResult(token);
        var fakeTokenServiceClient = A.Fake<ITokenServiceClient>();
        A.CallTo(() => fakeTokenServiceClient.GetTokenAsync(A<CancellationToken>._)).Returns(tokenResult);
        var tokenCredential = A.Fake<TokenCredential>();

        var configurationOptions = new ConfigurationOptions();
        var authenticationOptions = new AuthenticationOptions()
        {
            TokenCredential = tokenCredential,
            GetUserName = (token) => "user"
        };
        var amrConfig = new AzureManagedRedisConfiguration(authenticationOptions, configurationOptions.LoggerFactory);
        amrConfig.TokenManager.TokenServiceClient = fakeTokenServiceClient; // Override the IIdentityClient created during instantiation of TokenManager

        var fakeTokenRefreshedHandler = A.Fake<EventHandler<TokenResult>>();
        amrConfig.TokenManager.TokenRefreshed += fakeTokenRefreshedHandler;

        configurationOptions.Defaults = amrConfig;

        //Execute
        await amrConfig.TokenManager.AcquireTokenAsync(throwOnFailure: true);

        //Assert
        A.CallTo(() => fakeTokenRefreshedHandler.Invoke(amrConfig.TokenManager, A<TokenResult>._)).MustHaveHappenedOnceExactly();
        A.CallTo(() => fakeTokenServiceClient.GetTokenAsync(A<CancellationToken>._)).MustHaveHappenedOnceExactly();
        Assert.AreEqual("token", configurationOptions.Password);
    }

    // TODO: More tests

}

// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using Azure.Core;
using FakeItEasy;
using Microsoft.Identity.Client;
using StackExchange.Redis;
using StackExchange.Redis.Configuration;

namespace Microsoft.Azure.StackExchangeRedis.Tests;

[TestClass]
public class AzureCacheOptionsProviderWithTokenTests
{
    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task AcquireTokenAsync_Success(bool managedRedis)
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
        A.CallTo(() => fakeIdentityClient.GetTokenAsync(A<CancellationToken>._)).Returns(tokenResult);

        var configurationOptions = new ConfigurationOptions();
        var azureCacheOptions = new AzureCacheOptions()
        {
            GetUserName = (token) => "user",
            TokenHeartbeatInterval = TimeSpan.FromHours(1)
        };
        var tokenAuthenticationManager = new RedisTokenAuthenticationManager(azureCacheOptions, configurationOptions.LoggerFactory)
        {
            IdentityClient = fakeIdentityClient
        };
        DefaultOptionsProvider optionsProviderWithToken = managedRedis
            ? new AzureManagedRedisCacheOptionsProviderWithToken(tokenAuthenticationManager)
            : new AzureCacheOptionsProviderWithToken(tokenAuthenticationManager);

        var fakeTokenRefreshedHandler = A.Fake<EventHandler<TokenResult>>();
        ((IAzureCacheTokenEvents)optionsProviderWithToken).TokenRefreshed += fakeTokenRefreshedHandler;

        configurationOptions!.Defaults = optionsProviderWithToken;

        // Execute
        await tokenAuthenticationManager.AcquireTokenAsync(throwOnFailure: true);

        // Assert
        A.CallTo(() => fakeTokenRefreshedHandler.Invoke(optionsProviderWithToken, A<TokenResult>._)).MustHaveHappenedOnceExactly();
        A.CallTo(() => fakeIdentityClient.GetTokenAsync(A<CancellationToken>._)).MustHaveHappenedOnceExactly();
        Assert.AreEqual("token", configurationOptions.Password);
        Assert.AreEqual("user", configurationOptions.User);
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task AcquireTokenAsync_UsingTokenCredential_Success(bool managedRedis)
    {
        // Arrange
        var token = new AccessToken("token", DateTimeOffset.UtcNow + TimeSpan.FromMinutes(1));
        var tokenResult = new TokenResult(token);
        var fakeTokenCredentialClient = A.Fake<ICacheIdentityClient>();
        A.CallTo(() => fakeTokenCredentialClient.GetTokenAsync(A<CancellationToken>._)).Returns(tokenResult);
        var tokenCredential = A.Fake<TokenCredential>();

        var configurationOptions = new ConfigurationOptions();
        var azureCacheOptions = new AzureCacheOptions()
        {
            TokenCredential = tokenCredential,
            GetUserName = (token) => "user",
            TokenHeartbeatInterval = TimeSpan.FromHours(1)
        };
        var tokenAuthenticationManager = new RedisTokenAuthenticationManager(azureCacheOptions, configurationOptions.LoggerFactory)
        {
            IdentityClient = fakeTokenCredentialClient
        };
        DefaultOptionsProvider optionsProviderWithToken = managedRedis
            ? new AzureManagedRedisCacheOptionsProviderWithToken(tokenAuthenticationManager)
            : new AzureCacheOptionsProviderWithToken(tokenAuthenticationManager);

        var fakeTokenRefreshedHandler = A.Fake<EventHandler<TokenResult>>();
        ((IAzureCacheTokenEvents)optionsProviderWithToken).TokenRefreshed += fakeTokenRefreshedHandler;

        configurationOptions.Defaults = optionsProviderWithToken;

        //Execute
        await tokenAuthenticationManager.AcquireTokenAsync(throwOnFailure: true);

        //Assert
        A.CallTo(() => fakeTokenRefreshedHandler.Invoke(optionsProviderWithToken, A<TokenResult>._)).MustHaveHappenedOnceExactly();
        A.CallTo(() => fakeTokenCredentialClient.GetTokenAsync(A<CancellationToken>._)).MustHaveHappenedOnceExactly();
        Assert.AreEqual("token", configurationOptions.Password);
    }

    // TODO: More tests

}

// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using FakeItEasy;
using StackExchange.Redis;
using StackExchange.Redis.Configuration;
using static Microsoft.Azure.StackExchangeRedis.Tests.TestHelpers;

namespace Microsoft.Azure.StackExchangeRedis.Tests;

/// <summary>
/// Behavior shared by <see cref="AzureCacheForRedisConfiguration"/> and <see cref="AzureManagedRedisConfiguration"/>.
/// </summary>
public abstract class AzureRedisConfigurationTestsBase
{
    internal abstract DefaultOptionsProvider CreateWithoutAuthentication();

    internal abstract (DefaultOptionsProvider Provider, TokenManager TokenManager) CreateWithAuthentication(AuthenticationOptions options);

    private (DefaultOptionsProvider Provider, ITokenEvents Events, TokenManager TokenManager) CreateWithFakeTokenService(ITokenServiceClient client)
    {
        var (provider, tokenManager) = CreateWithAuthentication(CreateAuthenticationOptions());
        tokenManager.TokenServiceClient = client;
        return (provider, (ITokenEvents)provider, tokenManager);
    }

    [TestMethod]
    public void LibraryName_IdentifiesThisPackage()
    {
        Assert.AreEqual("MS.Az.SERedis", CreateWithoutAuthentication().LibraryName);
        Assert.AreEqual("MS.Az.SERedis", CreateWithAuthentication(CreateAuthenticationOptions()).Provider.LibraryName);
    }

    [TestMethod]
    public void WithoutAuthentication_HasNoCredentials()
    {
        var provider = CreateWithoutAuthentication();
        var configurationOptions = new ConfigurationOptions { Defaults = provider };

        Assert.IsNull(provider.User);
        Assert.IsNull(provider.Password);
        Assert.IsNull(configurationOptions.User);
        Assert.IsNull(configurationOptions.Password);
    }

    [TestMethod]
    public async Task WithAuthentication_UserAndPasswordComeFromToken()
    {
        var (provider, _, tokenManager) = CreateWithFakeTokenService(CreateTokenServiceClient("token1"));
        var configurationOptions = new ConfigurationOptions { Defaults = provider };

        await tokenManager.AcquireTokenAsync(throwOnFailure: true);

        Assert.AreEqual(TestUser, provider.User);
        Assert.AreEqual("token1", provider.Password);
        Assert.AreEqual(TestUser, configurationOptions.User);
        Assert.AreEqual("token1", configurationOptions.Password);
    }

    [TestMethod]
    public async Task WithAuthentication_PasswordReflectsRefreshedToken()
    {
        var client = A.Fake<ITokenServiceClient>();
        A.CallTo(() => client.GetTokenAsync(A<CancellationToken>._))
            .ReturnsNextFromSequence(CreateTokenResult("token1", TimeSpan.FromMinutes(30)), CreateTokenResult("token2", TimeSpan.FromMinutes(60)));
        var (provider, _, tokenManager) = CreateWithFakeTokenService(client);
        var configurationOptions = new ConfigurationOptions { Defaults = provider };

        await tokenManager.AcquireTokenAsync(throwOnFailure: true);
        await tokenManager.AcquireTokenAsync(throwOnFailure: true);

        Assert.AreEqual("token2", configurationOptions.Password);
    }

    [TestMethod]
    public async Task ForwardsTokenRefreshedEvent()
    {
        var (_, events, tokenManager) = CreateWithFakeTokenService(CreateTokenServiceClient());
        object? sender = null;
        TokenResult? result = null;
        events.TokenRefreshed += (s, e) => { sender = s; result = e; };

        await tokenManager.AcquireTokenAsync(throwOnFailure: true);

        Assert.AreSame(tokenManager, sender);
        Assert.IsNotNull(result);
        Assert.AreEqual("token", result.Token);
    }

    [TestMethod]
    public async Task ForwardsTokenRefreshFailedEvent()
    {
        var client = A.Fake<ITokenServiceClient>();
        var failure = new InvalidOperationException("failure");
        A.CallTo(() => client.GetTokenAsync(A<CancellationToken>._)).ThrowsAsync(failure);
        var (_, events, tokenManager) = CreateWithFakeTokenService(client);
        TokenRefreshFailedEventArgs? args = null;
        events.TokenRefreshFailed += (_, e) => args = e;

        await tokenManager.AcquireTokenAsync(throwOnFailure: false);

        Assert.IsNotNull(args);
        Assert.AreSame(failure, args.Exception);
    }

    [TestMethod]
    public async Task ForwardsConnectionReauthenticationEvents()
    {
        var (_, events, tokenManager) = CreateWithFakeTokenService(CreateTokenServiceClient());
        await tokenManager.AcquireTokenAsync(throwOnFailure: true);
        var healthy = CreateServer("healthy");
        var failing = CreateServer("failing");
        var failure = new RedisException("NOAUTH failure");
        A.CallTo(() => failing.ExecuteAsync("AUTH", A<object[]>._)).ThrowsAsync(failure);
        tokenManager.AddManagedConnection(CreateMultiplexer(healthy, failing), DateTime.MinValue);
        string? reauthenticated = null;
        ConnectionReauthenticationFailedEventArgs? failedArgs = null;
        events.ConnectionReauthenticated += (_, e) => reauthenticated = e;
        events.ConnectionReauthenticationFailed += (_, e) => failedArgs = e;

        await tokenManager.EnsureAuthenticationAsync();

        Assert.AreEqual(healthy.EndPoint.ToString(), reauthenticated);
        Assert.IsNotNull(failedArgs);
        Assert.AreSame(failure, failedArgs.Exception);
        Assert.AreEqual(failing.EndPoint.ToString(), failedArgs.Endpoint);
    }
}

// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Reflection;
using Azure.Core;
using FakeItEasy;
using StackExchange.Redis;
using StackExchange.Redis.Configuration;

namespace Microsoft.Azure.StackExchangeRedis.Tests;

[TestClass]
public class AzureCacheOptionsProviderTests
{
    [DataTestMethod]
    [DataRow("cache.northeurope.redis.azure.net:10000", true)]
    [DataRow("cache.redis.cache.windows.net:10000", false)]
    [DataRow("redis.example.com:10000", false)]
    [DataRow("127.0.0.1:10000", false)]
    [DataRow("", false)]
    [DataRow("127.0.0.1:10000,cache.northeurope.redis.azure.net:10000", true)]
    [DataRow("cache.redis.cache.windows.net:10000,cache.northeurope.redis.azure.net:10000", true)]
    public async Task Factory_SelectsProviderFlavoursAndSharesTokenLifecycle(string endpoints, bool managedRedis)
    {
        var options = endpoints.Length == 0 ? new ConfigurationOptions() : ConfigurationOptions.Parse(endpoints);
        var accessKeyProvider = AzureCacheOptionsProviderFactory.CreateDefaultsForAccessKey(options.EndPoints);
        Assert.AreEqual(
            managedRedis ? typeof(AzureManagedRedisCacheOptionsProvider) : typeof(AzureCacheOptionsProvider),
            accessKeyProvider.GetType());

        var azureOptions = CreateAzureOptions();
        var tokenOptionsProvider = new TokenOptionsProvider(azureOptions, null);
        await tokenOptionsProvider.AcquireTokenAsync(throwOnFailure: true);
        var provider = AzureCacheOptionsProviderFactory.CreateDefaultsWithToken(options.EndPoints, tokenOptionsProvider);
        Assert.AreEqual(
            managedRedis ? typeof(AzureManagedRedisCacheOptionsProviderWithToken) : typeof(AzureCacheOptionsProviderWithToken),
            provider.GetType());
        Assert.AreEqual("user", provider.User);
        Assert.AreEqual("token-1", provider.Password);
        var refreshes = 0;
        object? eventSender = null;
        ((IAzureCacheTokenEvents)provider).TokenRefreshed += (sender, _) =>
        {
            eventSender = sender;
            refreshes++;
        };

        await tokenOptionsProvider.AcquireTokenAsync(throwOnFailure: true);

        Assert.AreEqual("token-2", provider.Password);
        Assert.AreSame(provider, eventSender);
        Assert.AreEqual(1, refreshes);
        A.CallTo(() => azureOptions.TokenCredential!.GetTokenAsync(A<TokenRequestContext>._, A<CancellationToken>._))
            .MustHaveHappenedTwiceExactly();
    }

    [DataTestMethod]
    [DataRow("cache.northeurope.redis.azure.net", true)]
    [DataRow("CACHE.NORTHEUROPE.REDIS.AZURE.NET", true)]
    [DataRow("cache.privatelink.redis.azure.net", true)]
    [DataRow("cache.redis.chinacloudapi.cn", true)]
    [DataRow("cache.redis.usgovcloudapi.net", true)]
    [DataRow("cache.redis.sovcloud-api.de", true)]
    [DataRow("cache.redis.sovcloud-api.fr", true)]
    [DataRow("cache.redisenterprise.cache.azure.net", true)]
    [DataRow("cache.redis.cache.windows.net", false)]
    [DataRow("cache.redis.cache.chinacloudapi.cn", false)]
    [DataRow("cache.redis.cache.usgovcloudapi.net", false)]
    [DataRow("cache.redis.azure.net.example.com", false)]
    [DataRow("redis.azure.net", false)]
    [DataRow("redis.example.com", false)]
    [DataRow("127.0.0.1", false)]
    public void ConfigureForAzure_SelectsNativeDefaults(string host, bool managedRedis)
    {
        var options = ConfigurationOptions.Parse($"{host}:10000");
        DefaultOptionsProvider expected = managedRedis
            ? new AzureManagedRedisOptionsProvider()
            : new AzureOptionsProvider();
        var commandMap = options.CommandMap;

        AzureCacheForRedis.ConfigureForAzure(options);

        Assert.AreEqual(
            managedRedis ? typeof(AzureManagedRedisCacheOptionsProvider) : typeof(AzureCacheOptionsProvider),
            options.Defaults.GetType());
        foreach (var property in typeof(DefaultOptionsProvider).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.Name != nameof(DefaultOptionsProvider.LibraryName))
            {
                Assert.AreEqual(property.GetValue(expected), property.GetValue(options.Defaults), property.Name);
            }
        }

        Assert.AreEqual(expected.GetDefaultSsl(options.EndPoints), options.Ssl);
        Assert.AreEqual(expected.GetSslHostFromEndpoints(options.EndPoints), options.SslHost);
        Assert.AreEqual(expected.IsMatch(options.EndPoints[0]), options.Defaults.IsMatch(options.EndPoints[0]));
        Assert.AreEqual("Az.SE.Redis", options.Defaults.LibraryName);
        Assert.AreSame(commandMap, options.CommandMap);
    }

    [DataTestMethod]
    [DataRow("cache.northeurope.redis.azure.net")]
    [DataRow("cache.redis.cache.windows.net")]
    public void ConfigureForAzure_PreservesExplicitOptions(string host)
    {
        var options = ConfigurationOptions.Parse(
            $"{host}:10000,ssl=false,protocol=resp2,version=6.0,configChannel=custom,abortConnect=true");

        AzureCacheForRedis.ConfigureForAzure(options);

        Assert.IsFalse(options.Ssl);
        Assert.AreEqual(RedisProtocol.Resp2, options.Protocol);
        Assert.AreEqual(new Version(6, 0), options.DefaultVersion);
        Assert.AreEqual("custom", options.ConfigurationChannel);
        Assert.IsTrue(options.AbortOnConnectFail);
    }

    [DataTestMethod]
    [DataRow("cache.northeurope.redis.azure.net")]
    [DataRow("cache.redis.cache.windows.net")]
    public async Task ConfigureForAzureAsync_PreservesExplicitOptions(string host)
    {
        var options = ConfigurationOptions.Parse(
            $"{host}:10000,ssl=false,protocol=resp2,version=6.0,configChannel=custom,abortConnect=true");

        await options.ConfigureForAzureAsync(CreateAzureOptions());

        Assert.IsFalse(options.Ssl);
        Assert.AreEqual(RedisProtocol.Resp2, options.Protocol);
        Assert.AreEqual(new Version(6, 0), options.DefaultVersion);
        Assert.AreEqual("custom", options.ConfigurationChannel);
        Assert.IsTrue(options.AbortOnConnectFail);
        Assert.AreEqual("user", options.User);
        Assert.AreEqual("token-1", options.Password);
    }

    [TestMethod]
    public void ConfigureForAzure_DetectsManagedRedisBeyondFirstEndpoint()
    {
        var options = ConfigurationOptions.Parse("127.0.0.1:10000,cache.northeurope.redis.azure.net:10000");

        AzureCacheForRedis.ConfigureForAzure(options);

        Assert.AreEqual(RedisProtocol.Resp3, options.Protocol);
        Assert.AreEqual(string.Empty, options.ConfigurationChannel);
    }

    [TestMethod]
    public void ConfigureForAzure_WithNoEndpoints_PreservesLegacyDefaults()
    {
        var options = new ConfigurationOptions();

        AzureCacheForRedis.ConfigureForAzure(options);

        Assert.AreEqual(new AzureOptionsProvider().DefaultVersion, options.DefaultVersion);
        Assert.AreEqual(new AzureOptionsProvider().ConfigurationChannel, options.ConfigurationChannel);
    }

    [TestMethod]
    public async Task ConfigureForAzure_ManagedRedisConnectionHook_DoesNotSubscribe()
    {
        var options = ConfigurationOptions.Parse("cache.northeurope.redis.azure.net:10000");
        AzureCacheForRedis.ConfigureForAzure(options);
        var messages = new List<string>();

        // A maintenance listener would access the multiplexer; the AMR hook must be a no-op.
        await options.Defaults.AfterConnectAsync(null!, messages.Add);

        Assert.AreEqual(0, messages.Count);
    }

    [DataTestMethod]
    [DataRow("cache.northeurope.redis.azure.net", true)]
    [DataRow("cache.redis.cache.windows.net", false)]
    public async Task ConfigureForAzureAsync_PreservesAuthenticationAndTokenRefresh(string host, bool managedRedis)
    {
        var options = ConfigurationOptions.Parse($"{host}:10000");
        var credential = A.Fake<TokenCredential>();
        var tokensAcquired = 0;
        var refreshToken = new TaskCompletionSource<AccessToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        A.CallTo(() => credential.GetTokenAsync(A<TokenRequestContext>._, A<CancellationToken>._))
            .ReturnsLazily(() => ++tokensAcquired == 1
                ? new ValueTask<AccessToken>(new AccessToken("token-1", DateTimeOffset.UtcNow.AddMinutes(-1)))
                : new ValueTask<AccessToken>(refreshToken.Task));
        var azureOptions = new AzureCacheOptions
        {
            TokenCredential = credential,
            GetUserName = _ => "user",
            TokenHeartbeatInterval = TimeSpan.FromHours(1)
        };

        var configuredOptions = await options.ConfigureForAzureAsync(azureOptions);

        var provider = options.Defaults;
        Assert.AreSame(options, configuredOptions);
        Assert.AreEqual(
            managedRedis ? typeof(AzureManagedRedisCacheOptionsProviderWithToken) : typeof(AzureCacheOptionsProviderWithToken),
            provider.GetType());
        DefaultOptionsProvider expected = managedRedis
            ? new AzureManagedRedisOptionsProvider()
            : new AzureOptionsProvider();
        Assert.AreEqual("user", options.User);
        Assert.IsTrue(options.Ssl);
        Assert.AreEqual(expected.DefaultVersion, options.DefaultVersion);
        Assert.AreEqual(expected.Protocol, options.Protocol);
        Assert.AreEqual(expected.ConfigurationChannel, options.ConfigurationChannel);
        Assert.AreEqual("Az.SE.Redis", provider.LibraryName);
        var refreshed = 0;
        var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        ((IAzureCacheTokenEvents)provider).TokenRefreshed += (sender, _) =>
        {
            refreshed++;
            completion.TrySetResult(sender);
        };

        // Reading the expired token starts refresh; keep the replacement pending until the initial value is checked.
        Assert.AreEqual("token-1", options.Password);
        refreshToken.SetResult(new AccessToken("token-2", DateTimeOffset.UtcNow.AddHours(1)));
        Assert.AreSame(completion.Task, await Task.WhenAny(completion.Task, Task.Delay(TimeSpan.FromSeconds(5))));
        Assert.AreSame(provider, await completion.Task);

        Assert.AreEqual("token-2", options.Password);
        Assert.AreEqual("user", options.User);
        Assert.AreEqual(2, tokensAcquired);
        Assert.AreEqual(1, refreshed);
    }

    private static AzureCacheOptions CreateAzureOptions()
    {
        var credential = A.Fake<TokenCredential>();
        var tokensAcquired = 0;
        A.CallTo(() => credential.GetTokenAsync(A<TokenRequestContext>._, A<CancellationToken>._))
            .ReturnsLazily(() => new ValueTask<AccessToken>(
                new AccessToken($"token-{++tokensAcquired}", DateTimeOffset.UtcNow.AddHours(tokensAcquired))));

        return new AzureCacheOptions
        {
            TokenCredential = credential,
            GetUserName = _ => "user",
            TokenHeartbeatInterval = TimeSpan.FromHours(1)
        };
    }
}

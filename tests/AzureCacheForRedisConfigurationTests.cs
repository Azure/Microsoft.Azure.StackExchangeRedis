// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using StackExchange.Redis;
using StackExchange.Redis.Configuration;
using static Microsoft.Azure.StackExchangeRedis.Tests.TestHelpers;

namespace Microsoft.Azure.StackExchangeRedis.Tests;

[TestClass]
public class AzureCacheForRedisConfigurationTests : AzureRedisConfigurationTestsBase
{
    internal override DefaultOptionsProvider CreateWithoutAuthentication() => new AzureCacheForRedisConfiguration();

    internal override (DefaultOptionsProvider Provider, TokenManager TokenManager) CreateWithAuthentication(AuthenticationOptions options)
    {
        var config = new AzureCacheForRedisConfiguration(options, loggerFactory: null);
        return (config, config.TokenManager!);
    }

    [TestMethod]
    public void WithoutAuthentication_HasNoTokenManager()
    {
        Assert.IsNull(new AzureCacheForRedisConfiguration().TokenManager);
    }

    [TestMethod]
    [DataRow("contoso.redis.cache.windows.net:6379")]
    [DataRow("contoso.redis.cache.windows.net:6380")]
    [DataRow("localhost:6379")]
    public void GetDefaultSsl_WithAuthentication_AlwaysRequiresSsl(string endpoint)
    {
        var config = new AzureCacheForRedisConfiguration(CreateAuthenticationOptions(), loggerFactory: null);

        Assert.IsTrue(config.GetDefaultSsl(new EndPointCollection { endpoint }));
    }

    [TestMethod]
    [DataRow("contoso.redis.cache.windows.net:6379")]
    [DataRow("contoso.redis.cache.windows.net:6380")]
    public void GetDefaultSsl_WithoutAuthentication_MatchesAzureDefaults(string endpoint)
    {
        var endpoints = new EndPointCollection { endpoint };

        Assert.AreEqual(new AzureOptionsProvider().GetDefaultSsl(endpoints), new AzureCacheForRedisConfiguration().GetDefaultSsl(endpoints));
    }

    [TestMethod]
    public void WithAuthentication_SslEnabledByDefaultOnConfigurationOptions()
    {
        var configurationOptions = ConfigurationOptions.Parse("contoso.redis.cache.windows.net:6379");
        configurationOptions.Defaults = new AzureCacheForRedisConfiguration(CreateAuthenticationOptions(), loggerFactory: null);

        Assert.IsTrue(configurationOptions.Ssl);
    }
}

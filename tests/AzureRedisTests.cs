// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using Azure.Core;
using FakeItEasy;
using StackExchange.Redis;
using static Microsoft.Azure.StackExchangeRedis.Tests.TestHelpers;

namespace Microsoft.Azure.StackExchangeRedis.Tests;

[TestClass]
public class AzureRedisTests
{
    private const string AmrEndpoint = "contoso.eastus.redis.azure.net:10000";
    private const string AcrEndpoint = "contoso.redis.cache.windows.net:6380";

    private static TokenCredential CreateCredential(string token, Action<TokenRequestContext>? onRequest = null)
    {
        var credential = A.Fake<TokenCredential>();
        A.CallTo(() => credential.GetTokenAsync(A<TokenRequestContext>._, A<CancellationToken>._))
            .ReturnsLazily((TokenRequestContext context, CancellationToken _) =>
            {
                onRequest?.Invoke(context);
                return new ValueTask<AccessToken>(new AccessToken(token, DateTimeOffset.UtcNow.AddHours(1)));
            });
        return credential;
    }

    private static AuthenticationOptions CreateOptions(TokenCredential credential)
    {
        var options = CreateAuthenticationOptions();
        options.TokenCredential = credential;
        return options;
    }

    #region ConfigureForAzure (no Entra ID authentication)

    [TestMethod]
    public void ConfigureForAzure_AmrEndpoint_UsesAzureManagedRedisDefaults()
    {
        var configurationOptions = ConfigurationOptions.Parse(AmrEndpoint);

        AzureRedis.ConfigureForAzure(configurationOptions);

        var config = Assert.IsInstanceOfType<AzureManagedRedisConfiguration>(configurationOptions.Defaults);
        Assert.IsNull(config.TokenManager);
    }

    [TestMethod]
    [DataRow(AcrEndpoint)]
    [DataRow("localhost:6379")]
    public void ConfigureForAzure_OtherEndpoint_UsesAzureCacheForRedisDefaults(string endpoint)
    {
        var configurationOptions = ConfigurationOptions.Parse(endpoint);

        AzureRedis.ConfigureForAzure(configurationOptions);

        var config = Assert.IsInstanceOfType<AzureCacheForRedisConfiguration>(configurationOptions.Defaults);
        Assert.IsNull(config.TokenManager);
    }

    [TestMethod]
    public void ConfigureForAzure_NoEndpoints_UsesAzureCacheForRedisDefaults()
    {
        var configurationOptions = new ConfigurationOptions();

        AzureRedis.ConfigureForAzure(configurationOptions);

        Assert.IsInstanceOfType<AzureCacheForRedisConfiguration>(configurationOptions.Defaults);
    }

    [TestMethod]
    public void ConfigureForAzure_CanBeUsedAsConfigurationCallback()
    {
        var configurationOptions = ConfigurationOptions.Parse(AmrEndpoint).Apply(AzureRedis.ConfigureForAzure);

        Assert.IsInstanceOfType<AzureManagedRedisConfiguration>(configurationOptions.Defaults);
    }

    #endregion

    #region ConfigureForAzureAsync (Entra ID authentication)

    [TestMethod]
    public async Task ConfigureForAzureAsync_AmrEndpoint_UsesAzureManagedRedisConfigurationAndAcquiresToken()
    {
        var configurationOptions = ConfigurationOptions.Parse(AmrEndpoint);

        var result = await configurationOptions.ConfigureForAzureAsync(CreateOptions(CreateCredential("amr-token")));

        Assert.AreSame(configurationOptions, result);
        var config = Assert.IsInstanceOfType<AzureManagedRedisConfiguration>(configurationOptions.Defaults);
        Assert.IsNotNull(config.TokenManager);
        Assert.AreEqual(TestUser, configurationOptions.User);
        Assert.AreEqual("amr-token", configurationOptions.Password);
    }

    [TestMethod]
    [DataRow(AcrEndpoint)]
    [DataRow("localhost:6379")]
    public async Task ConfigureForAzureAsync_OtherEndpoint_UsesAzureCacheForRedisConfigurationAndAcquiresToken(string endpoint)
    {
        var configurationOptions = ConfigurationOptions.Parse(endpoint);

        await configurationOptions.ConfigureForAzureAsync(CreateOptions(CreateCredential("acr-token")));

        var config = Assert.IsInstanceOfType<AzureCacheForRedisConfiguration>(configurationOptions.Defaults);
        Assert.IsNotNull(config.TokenManager);
        Assert.AreEqual(TestUser, configurationOptions.User);
        Assert.AreEqual("acr-token", configurationOptions.Password);
        Assert.IsTrue(configurationOptions.Ssl);
    }

    [TestMethod]
    public async Task ConfigureForAzureAsync_RequestsConfiguredScope()
    {
        var scopes = new List<string>();
        var options = CreateOptions(CreateCredential("token", context => scopes.AddRange(context.Scopes)));
        options.Scope = "https://custom.scope/.default";

        await ConfigurationOptions.Parse(AmrEndpoint).ConfigureForAzureAsync(options);

        CollectionAssert.AreEqual(new[] { "https://custom.scope/.default" }, scopes);
    }

    [TestMethod]
    public async Task ConfigureForAzureAsync_TokenAcquisitionFailure_ThrowsWrappedException()
    {
        var failure = new InvalidOperationException("credential failure");
        var credential = A.Fake<TokenCredential>();
        A.CallTo(() => credential.GetTokenAsync(A<TokenRequestContext>._, A<CancellationToken>._))
            .ReturnsLazily(() => new ValueTask<AccessToken>(Task.FromException<AccessToken>(failure)));
        var options = CreateOptions(credential);
        options.MaxTokenRefreshAttempts = 2;

        var ex = await Assert.ThrowsExactlyAsync<Exception>(() => ConfigurationOptions.Parse(AmrEndpoint).ConfigureForAzureAsync(options));

        Assert.AreEqual("Failed to acquire token", ex.Message);
        Assert.AreSame(failure, ex.InnerException);
        A.CallTo(() => credential.GetTokenAsync(A<TokenRequestContext>._, A<CancellationToken>._)).MustHaveHappenedTwiceExactly();
    }

    [TestMethod]
    public async Task ConfigureForAzureAsync_InvalidServicePrincipalOptions_ThrowsArgumentException()
    {
        var options = new AuthenticationOptions { ServicePrincipalSecret = "secret" };

        await Assert.ThrowsExactlyAsync<ArgumentException>(() => ConfigurationOptions.Parse(AmrEndpoint).ConfigureForAzureAsync(options));
    }

    #endregion

    #region Convenience overloads

    [TestMethod]
    public async Task ConfigureForAzureWithTokenCredentialAsync_UsesDefaultScopeAndOidFromToken()
    {
        var jwt = CreateJwt("00000000-aaaa-bbbb-cccc-000000000000");
        var scopes = new List<string>();
        var credential = CreateCredential(jwt, context => scopes.AddRange(context.Scopes));
        var configurationOptions = ConfigurationOptions.Parse(AmrEndpoint);

        var result = await configurationOptions.ConfigureForAzureWithTokenCredentialAsync(credential);

        Assert.AreSame(configurationOptions, result);
        Assert.AreEqual("00000000-aaaa-bbbb-cccc-000000000000", configurationOptions.User);
        Assert.AreEqual(jwt, configurationOptions.Password);
        CollectionAssert.AreEqual(new[] { "https://redis.azure.com/.default" }, scopes);
    }

    [TestMethod]
    public async Task ConfigureForAzureWithTokenCredentialAsync_TokenWithoutOid_ThrowsWrappedException()
    {
        var credential = CreateCredential(CreateJwt(oid: null));

        var ex = await Assert.ThrowsExactlyAsync<Exception>(() => ConfigurationOptions.Parse(AmrEndpoint).ConfigureForAzureWithTokenCredentialAsync(credential));

        Assert.AreEqual("Failed to acquire token", ex.Message);
        Assert.AreEqual("oid not found in token claims", ex.InnerException?.Message);
    }

    [TestMethod]
    public async Task ConfigureForAzureWithServicePrincipalAsync_WithoutSecretOrCertificate_ThrowsArgumentException()
    {
        var ex = await Assert.ThrowsExactlyAsync<ArgumentException>(() => ConfigurationOptions.Parse(AmrEndpoint)
            .ConfigureForAzureWithServicePrincipalAsync("00000000-0000-0000-0000-000000000001", "00000000-0000-0000-0000-000000000002"));

        StringAssert.Contains(ex.Message, nameof(AuthenticationOptions.ServicePrincipalSecret));
    }

    #endregion
}

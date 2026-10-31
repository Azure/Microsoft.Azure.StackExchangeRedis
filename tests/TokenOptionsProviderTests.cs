// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Net;
using System.Reflection;
using Azure.Core;
using FakeItEasy;
using StackExchange.Redis;
using StackExchange.Redis.Configuration;

namespace Microsoft.Azure.StackExchangeRedis.Tests;

[TestClass]
public class TokenOptionsProviderTests
{
    [DataTestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void Providers_PreserveNativeDefaults(bool managedRedis, bool withToken)
    {
        var tokenProvider = new TokenOptionsProvider(CreateOptions(), null);
        DefaultOptionsProvider provider = withToken
            ? CreateWrapper(managedRedis, tokenProvider)
            : managedRedis ? new AzureManagedRedisCacheOptionsProvider() : new AzureCacheOptionsProvider();
        DefaultOptionsProvider expected = managedRedis
            ? new AzureManagedRedisOptionsProvider()
            : new AzureOptionsProvider();

        foreach (var property in typeof(DefaultOptionsProvider).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.Name != nameof(DefaultOptionsProvider.LibraryName)
                && property.Name != nameof(DefaultOptionsProvider.User)
                && property.Name != nameof(DefaultOptionsProvider.Password))
            {
                Assert.AreEqual(property.GetValue(expected), property.GetValue(provider), property.Name);
            }
        }

        var endPoints = ConfigurationOptions.Parse("redis.example.com:10000").EndPoints;
        Assert.AreEqual(withToken || expected.GetDefaultSsl(endPoints), provider.GetDefaultSsl(endPoints));
        Assert.AreEqual("Az.SE.Redis", provider.LibraryName);
        Assert.IsInstanceOfType(provider, managedRedis ? typeof(AzureManagedRedisOptionsProvider) : typeof(AzureOptionsProvider));
    }

    [TestMethod]
    public void TokenWrappers_RequireTokenProvider()
    {
        Assert.ThrowsException<ArgumentNullException>(() => new AzureCacheOptionsProviderWithToken(null!));
        Assert.ThrowsException<ArgumentNullException>(() => new AzureManagedRedisCacheOptionsProviderWithToken(null!));
    }

    [TestMethod]
    public async Task AcquireTokenAsync_RetriesBeforeSuccessfulRefresh()
    {
        var options = CreateOptions();
        var backoffAttempts = new List<int>();
        options.TokenRefreshBackoff = (attempt, _) =>
        {
            backoffAttempts.Add(attempt);
            return TimeSpan.Zero;
        };
        var provider = new TokenOptionsProvider(options, null);
        var identity = A.Fake<ICacheIdentityClient>();
        provider.IdentityClient = identity;
        var attempts = 0;
        A.CallTo(() => identity.GetTokenAsync(A<CancellationToken>._))
            .ReturnsLazily(() => ++attempts == 1
                ? Task.FromException<TokenResult>(new InvalidOperationException("Transient failure"))
                : Task.FromResult(new TokenResult(new AccessToken("token", DateTimeOffset.UtcNow.AddHours(1)))));

        await provider.AcquireTokenAsync(throwOnFailure: true);

        Assert.AreEqual(2, attempts);
        CollectionAssert.AreEqual(new[] { 0 }, backoffAttempts);
        Assert.AreEqual("user", provider.User);
        Assert.AreEqual("token", provider.Password);
    }

    [TestMethod]
    public async Task AcquireTokenAsync_DoesNotReplaceTokenWithEarlierExpiry()
    {
        var provider = new TokenOptionsProvider(CreateOptions(), null);
        var identity = A.Fake<ICacheIdentityClient>();
        provider.IdentityClient = identity;
        var attempts = 0;
        A.CallTo(() => identity.GetTokenAsync(A<CancellationToken>._))
            .ReturnsLazily(() => Task.FromResult(new TokenResult(
                new AccessToken($"token-{++attempts}", DateTimeOffset.UtcNow.AddHours(3 - attempts)))));
        var refreshes = 0;
        provider.TokenRefreshed += (_, _) => refreshes++;

        await provider.AcquireTokenAsync(throwOnFailure: true);
        await provider.AcquireTokenAsync(throwOnFailure: true);

        Assert.AreEqual("token-1", provider.Password);
        Assert.AreEqual(1, refreshes);
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task TokenWrappers_RelayRefreshFailureAndSupportUnsubscription(bool managedRedis)
    {
        var options = CreateOptions();
        options.MaxTokenRefreshAttempts = 1;
        var tokenProvider = new TokenOptionsProvider(options, null);
        var identity = A.Fake<ICacheIdentityClient>();
        tokenProvider.IdentityClient = identity;
        var failure = new InvalidOperationException("Token acquisition failed");
        A.CallTo(() => identity.GetTokenAsync(A<CancellationToken>._))
            .Returns(Task.FromException<TokenResult>(failure));
        var wrapper = CreateWrapper(managedRedis, tokenProvider);
        var events = (IAzureCacheTokenEvents)wrapper;
        object? sender = null;
        TokenRefreshFailedEventArgs? result = null;
        var failures = 0;
        EventHandler<TokenRefreshFailedEventArgs> handler = (source, args) =>
        {
            sender = source;
            result = args;
            failures++;
        };
        events.TokenRefreshFailed += handler;

        await tokenProvider.AcquireTokenAsync(throwOnFailure: false);
        events.TokenRefreshFailed -= handler;
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(
            () => tokenProvider.AcquireTokenAsync(throwOnFailure: true));

        Assert.AreSame(wrapper, sender);
        Assert.AreSame(failure, result!.Exception);
        Assert.AreEqual(DateTime.MinValue, result.Expiry);
        Assert.AreEqual(1, failures);
    }

    [DataTestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task TokenWrappers_RefreshExpiredPasswordAndReauthenticateConnections(bool managedRedis, bool failReauthentication)
    {
        var tokenProvider = new TokenOptionsProvider(CreateOptions(), null);
        var identity = A.Fake<ICacheIdentityClient>();
        tokenProvider.IdentityClient = identity;
        var attempts = 0;
        A.CallTo(() => identity.GetTokenAsync(A<CancellationToken>._))
            .ReturnsLazily(() => Task.FromResult(new TokenResult(new AccessToken(
                $"token-{++attempts}", DateTimeOffset.UtcNow.AddMinutes(attempts == 1 ? -1 : 60)))));
        var wrapper = CreateWrapper(managedRedis, tokenProvider);
        var events = (IAzureCacheTokenEvents)wrapper;
        var connection = A.Fake<IConnectionMultiplexer>();
        var server = A.Fake<IServer>();
        var endPoint = new DnsEndPoint("redis.example.com", 10000);
        A.CallTo(() => connection.GetServers()).Returns(new[] { server });
        A.CallTo(() => server.IsConnected).Returns(true);
        A.CallTo(() => server.EndPoint).Returns(endPoint);
        var failure = new InvalidOperationException("Re-authentication failed");
        A.CallTo(() => server.ExecuteAsync("AUTH", A<object[]>._))
            .Returns(failReauthentication
                ? Task.FromException<RedisResult>(failure)
                : Task.FromResult(RedisResult.Create((RedisValue)"OK")));
        var completion = new TaskCompletionSource<(object? Sender, string? Endpoint, Exception? Error)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        events.ConnectionReauthenticated += (sender, endpoint) => completion.TrySetResult((sender, endpoint, null));
        events.ConnectionReauthenticationFailed += (sender, args) => completion.TrySetResult((sender, args.Endpoint, args.Exception));
        await tokenProvider.AcquireTokenAsync(throwOnFailure: true);
        await tokenProvider.AfterConnectAsync(connection, () => Task.CompletedTask);

        _ = wrapper.Password;

        Assert.AreSame(completion.Task, await Task.WhenAny(completion.Task, Task.Delay(TimeSpan.FromSeconds(5))));
        var result = await completion.Task;
        Assert.AreSame(wrapper, result.Sender);
        Assert.AreEqual(endPoint.ToString(), result.Endpoint);
        Assert.AreSame(failReauthentication ? failure : null, result.Error);
        Assert.AreEqual("token-2", wrapper.Password);
        A.CallTo(() => server.ExecuteAsync("AUTH", A<object[]>.That.Matches(
            args => args.Length == 2 && (string)args[0] == "user" && (string)args[1] == "token-2")))
            .MustHaveHappenedOnceExactly();
    }

    [TestMethod]
    public async Task AfterConnectAsync_AwaitsProviderSpecificHook()
    {
        var provider = new TokenOptionsProvider(CreateOptions(), null);
        var connection = A.Fake<IConnectionMultiplexer>();
        var hook = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var initialization = provider.AfterConnectAsync(connection, () => hook.Task);

        Assert.IsFalse(initialization.IsCompleted);
        hook.SetResult(true);
        await initialization;
    }

    [TestMethod]
    public async Task AfterConnectAsync_ContainsProviderSpecificHookFailures()
    {
        var provider = new TokenOptionsProvider(CreateOptions(), null);

        await provider.AfterConnectAsync(A.Fake<IConnectionMultiplexer>(),
            () => Task.FromException(new InvalidOperationException("Connection hook failed")));
    }

    private static AzureCacheOptions CreateOptions() => new()
    {
        TokenCredential = A.Fake<TokenCredential>(),
        GetUserName = _ => "user",
        TokenHeartbeatInterval = TimeSpan.FromHours(1),
        TokenRefreshBackoff = (_, _) => TimeSpan.Zero
    };

    private static DefaultOptionsProvider CreateWrapper(bool managedRedis, TokenOptionsProvider tokenProvider)
        => managedRedis
            ? new AzureManagedRedisCacheOptionsProviderWithToken(tokenProvider)
            : new AzureCacheOptionsProviderWithToken(tokenProvider);
}

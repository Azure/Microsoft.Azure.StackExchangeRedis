// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Collections.Concurrent;
using System.Diagnostics;
using Azure.Core;
using FakeItEasy;
using StackExchange.Redis;
using static Microsoft.Azure.StackExchangeRedis.Tests.TestHelpers;

namespace Microsoft.Azure.StackExchangeRedis.Tests;

[TestClass]
public class TokenManagerTests
{
    private const string ClientId = "00000000-0000-0000-0000-000000000001";
    private const string TenantId = "00000000-0000-0000-0000-000000000002";

    #region Identity client selection

    [TestMethod]
    public async Task Constructor_UsesTokenCredential_WhenProvided()
    {
        var credential = A.Fake<TokenCredential>();
        A.CallTo(() => credential.GetTokenAsync(A<TokenRequestContext>._, A<CancellationToken>._))
            .Returns(new ValueTask<AccessToken>(new AccessToken("credential-token", DateTimeOffset.UtcNow.AddHours(1))));
        var options = CreateAuthenticationOptions();
        options.TokenCredential = credential;
        options.Scope = "https://custom.scope/.default";

        var tokenManager = new TokenManager(options, loggerFactory: null);
        await tokenManager.AcquireTokenAsync(throwOnFailure: true);

        Assert.AreEqual("credential-token", tokenManager.Token);
        A.CallTo(() => credential.GetTokenAsync(
                A<TokenRequestContext>.That.Matches(c => c.Scopes.Length == 1 && c.Scopes[0] == "https://custom.scope/.default"),
                A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [TestMethod]
    public void Constructor_TokenCredentialTakesPrecedenceOverIncompleteServicePrincipalSettings()
    {
        var options = CreateAuthenticationOptions();
        options.ServicePrincipalSecret = "secret"; // Would be invalid service principal config (no client or tenant ID)

        var tokenManager = new TokenManager(options, loggerFactory: null);

        Assert.IsInstanceOfType<TokenServiceClient>(tokenManager.TokenServiceClient);
    }

    [TestMethod]
    [DataRow(null, DisplayName = "System-assigned")]
    [DataRow("00000000-0000-0000-0000-000000000003", DisplayName = "User-assigned client ID")]
    [DataRow("/subscriptions/sub/resourceGroups/rg/providers/Microsoft.ManagedIdentity/userAssignedIdentities/id", DisplayName = "User-assigned resource ID")]
    public void Constructor_UsesManagedIdentity_WhenNoCredentialOrServicePrincipal(string? clientId)
    {
        var options = new AuthenticationOptions { ClientId = clientId, TokenHeartbeatInterval = TimeSpan.FromHours(1) };

        var tokenManager = new TokenManager(options, loggerFactory: null);

        Assert.IsInstanceOfType<TokenServiceClient>(tokenManager.TokenServiceClient);
    }

    [TestMethod]
    public void Constructor_ServicePrincipalWithSecret_Succeeds()
    {
        var options = new AuthenticationOptions
        {
            ClientId = ClientId,
            ServicePrincipalTenantId = TenantId,
            ServicePrincipalSecret = "secret",
            TokenHeartbeatInterval = TimeSpan.FromHours(1),
        };

        var tokenManager = new TokenManager(options, loggerFactory: null);

        Assert.IsInstanceOfType<TokenServiceClient>(tokenManager.TokenServiceClient);
    }

    [TestMethod]
    public void Constructor_ServicePrincipalWithCertificate_Succeeds()
    {
        using var certificate = CreateSelfSignedCertificate();
        var options = new AuthenticationOptions
        {
            ClientId = ClientId,
            ServicePrincipalTenantId = TenantId,
            ServicePrincipalCertificate = certificate,
            TokenHeartbeatInterval = TimeSpan.FromHours(1),
        };

        var tokenManager = new TokenManager(options, loggerFactory: null);

        Assert.IsInstanceOfType<TokenServiceClient>(tokenManager.TokenServiceClient);
    }

    [TestMethod]
    [DataRow(null, TenantId, "secret", DisplayName = "Missing client ID")]
    [DataRow(ClientId, null, "secret", DisplayName = "Missing tenant ID")]
    [DataRow(ClientId, TenantId, null, DisplayName = "Missing secret and certificate")]
    public void Constructor_IncompleteServicePrincipal_Throws(string? clientId, string? tenantId, string? secret)
    {
        var options = new AuthenticationOptions
        {
            ClientId = clientId,
            ServicePrincipalTenantId = tenantId,
            ServicePrincipalSecret = secret,
        };

        Assert.ThrowsExactly<ArgumentException>(() => new TokenManager(options, loggerFactory: null));
    }

    #endregion

    #region AcquireTokenAsync

    [TestMethod]
    public async Task AcquireTokenAsync_Success_StoresTokenAndIdentityAndRaisesTokenRefreshed()
    {
        var expected = CreateTokenResult("token");
        var client = A.Fake<ITokenServiceClient>();
        A.CallTo(() => client.GetTokenAsync(A<CancellationToken>._)).Returns(expected);
        var tokenManager = CreateTokenManager(client);
        object? sender = null;
        TokenResult? refreshed = null;
        tokenManager.TokenRefreshed += (s, e) => { sender = s; refreshed = e; };

        await tokenManager.AcquireTokenAsync(throwOnFailure: true);

        Assert.AreEqual("token", tokenManager.Token);
        Assert.AreEqual(TestUser, tokenManager.IdentityObjectId);
        Assert.AreEqual(expected.ExpiresOn.UtcDateTime, tokenManager._tokenExpiry);
        Assert.AreSame(tokenManager, sender);
        Assert.AreSame(expected, refreshed);
    }

    [TestMethod]
    public async Task AcquireTokenAsync_PassesCancellableToken()
    {
        var client = CreateTokenServiceClient();
        var tokenManager = CreateTokenManager(client);

        await tokenManager.AcquireTokenAsync(throwOnFailure: true);

        A.CallTo(() => client.GetTokenAsync(A<CancellationToken>.That.Matches(ct => ct.CanBeCanceled))).MustHaveHappenedOnceExactly();
    }

    [TestMethod]
    public async Task AcquireTokenAsync_ResolvesUserNameOnlyFromFirstToken()
    {
        var userNameCalls = new ConcurrentQueue<string?>();
        var options = CreateAuthenticationOptions();
        options.GetUserName = token => { userNameCalls.Enqueue(token); return "first-user"; };
        var client = A.Fake<ITokenServiceClient>();
        A.CallTo(() => client.GetTokenAsync(A<CancellationToken>._))
            .ReturnsNextFromSequence(CreateTokenResult("token1", TimeSpan.FromMinutes(30)), CreateTokenResult("token2", TimeSpan.FromMinutes(60)));
        var tokenManager = CreateTokenManager(client, options);

        await tokenManager.AcquireTokenAsync(throwOnFailure: true);
        await tokenManager.AcquireTokenAsync(throwOnFailure: true);

        Assert.AreEqual("token2", tokenManager.Token);
        Assert.AreEqual("first-user", tokenManager.IdentityObjectId);
        CollectionAssert.AreEqual(new[] { "token1" }, userNameCalls.ToArray());
    }

    [TestMethod]
    public async Task AcquireTokenAsync_RetriesTransientFailuresWithBackoff()
    {
        var backoffCalls = new ConcurrentQueue<(int Attempt, Exception? Exception)>();
        var options = CreateAuthenticationOptions();
        options.MaxTokenRefreshAttempts = 5;
        options.TokenRefreshBackoff = (attempt, ex) => { backoffCalls.Enqueue((attempt, ex)); return TimeSpan.Zero; };
        var failure1 = new InvalidOperationException("failure 1");
        var failure2 = new InvalidOperationException("failure 2");
        var client = A.Fake<ITokenServiceClient>();
        A.CallTo(() => client.GetTokenAsync(A<CancellationToken>._)).ReturnsNextFromSequence(
            Task.FromException<TokenResult>(failure1),
            Task.FromException<TokenResult>(failure2),
            Task.FromResult(CreateTokenResult("token")));
        var tokenManager = CreateTokenManager(client, options);
        var failedEvents = 0;
        tokenManager.TokenRefreshFailed += (_, _) => Interlocked.Increment(ref failedEvents);

        await tokenManager.AcquireTokenAsync(throwOnFailure: true);

        Assert.AreEqual("token", tokenManager.Token);
        A.CallTo(() => client.GetTokenAsync(A<CancellationToken>._)).MustHaveHappened(3, Times.Exactly);
        Assert.AreEqual(0, failedEvents);
        var calls = backoffCalls.ToArray();
        Assert.AreEqual(2, calls.Length);
        Assert.AreEqual(0, calls[0].Attempt);
        Assert.AreSame(failure1, calls[0].Exception);
        Assert.AreEqual(1, calls[1].Attempt);
        Assert.AreSame(failure2, calls[1].Exception);
    }

    [TestMethod]
    public async Task AcquireTokenAsync_AllAttemptsFail_ThrowOnFailure_ThrowsLastExceptionAndRaisesTokenRefreshFailed()
    {
        var options = CreateAuthenticationOptions();
        options.MaxTokenRefreshAttempts = 4;
        var attempt = 0;
        var client = A.Fake<ITokenServiceClient>();
        A.CallTo(() => client.GetTokenAsync(A<CancellationToken>._))
            .ReturnsLazily(() => Task.FromException<TokenResult>(new InvalidOperationException($"failure {Interlocked.Increment(ref attempt)}")));
        var tokenManager = CreateTokenManager(client, options);
        TokenRefreshFailedEventArgs? failedArgs = null;
        object? sender = null;
        tokenManager.TokenRefreshFailed += (s, e) => { sender = s; failedArgs = e; };

        var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => tokenManager.AcquireTokenAsync(throwOnFailure: true));

        Assert.AreEqual("failure 4", ex.Message);
        A.CallTo(() => client.GetTokenAsync(A<CancellationToken>._)).MustHaveHappened(4, Times.Exactly);
        Assert.AreSame(tokenManager, sender);
        Assert.IsNotNull(failedArgs);
        Assert.AreSame(ex, failedArgs.Exception);
        Assert.AreEqual(DateTime.MinValue, failedArgs.Expiry);
        Assert.IsNull(tokenManager.IdentityObjectId);
    }

    [TestMethod]
    public async Task AcquireTokenAsync_AllAttemptsFail_NoThrow_KeepsCurrentTokenAndReportsItsExpiry()
    {
        var (tokenManager, client) = await CreateAuthenticatedTokenManagerAsync(token: "current-token");
        var currentExpiry = tokenManager._tokenExpiry;
        A.CallTo(() => client.GetTokenAsync(A<CancellationToken>._)).ThrowsAsync(new InvalidOperationException("failure"));
        TokenRefreshFailedEventArgs? failedArgs = null;
        tokenManager.TokenRefreshFailed += (_, e) => failedArgs = e;

        await tokenManager.AcquireTokenAsync(throwOnFailure: false);

        Assert.AreEqual("current-token", tokenManager.Token);
        Assert.AreEqual(currentExpiry, tokenManager._tokenExpiry);
        Assert.IsNotNull(failedArgs);
        Assert.IsInstanceOfType<InvalidOperationException>(failedArgs.Exception);
        Assert.AreEqual(currentExpiry, failedArgs.Expiry);
    }

    [TestMethod]
    public async Task AcquireTokenAsync_IgnoresTokenThatDoesNotExtendExpiry()
    {
        var (tokenManager, client) = await CreateAuthenticatedTokenManagerAsync(token: "current-token");
        var currentExpiry = tokenManager._tokenExpiry;
        A.CallTo(() => client.GetTokenAsync(A<CancellationToken>._))
            .Returns(new TokenResult(new AccessToken("cached-token", new DateTimeOffset(currentExpiry, TimeSpan.Zero))));
        var refreshedEvents = 0;
        var failedEvents = 0;
        tokenManager.TokenRefreshed += (_, _) => Interlocked.Increment(ref refreshedEvents);
        tokenManager.TokenRefreshFailed += (_, _) => Interlocked.Increment(ref failedEvents);

        await tokenManager.AcquireTokenAsync(throwOnFailure: true);

        Assert.AreEqual("current-token", tokenManager.Token);
        Assert.AreEqual(currentExpiry, tokenManager._tokenExpiry);
        Assert.AreEqual(0, refreshedEvents);
        Assert.AreEqual(0, failedEvents);
    }

    [TestMethod]
    public async Task AcquireTokenAsync_HandlerExceptionsDoNotPropagate()
    {
        var options = CreateAuthenticationOptions();
        options.MaxTokenRefreshAttempts = 1;
        var client = A.Fake<ITokenServiceClient>();
        A.CallTo(() => client.GetTokenAsync(A<CancellationToken>._)).ReturnsNextFromSequence(
            Task.FromResult(CreateTokenResult("token")),
            Task.FromException<TokenResult>(new InvalidOperationException()));
        var tokenManager = CreateTokenManager(client, options);
        tokenManager.TokenRefreshed += (_, _) => throw new InvalidOperationException("handler failure");
        tokenManager.TokenRefreshFailed += (_, _) => throw new InvalidOperationException("handler failure");

        await tokenManager.AcquireTokenAsync(throwOnFailure: true);
        await tokenManager.AcquireTokenAsync(throwOnFailure: false);

        Assert.AreEqual("token", tokenManager.Token);
    }

    [TestMethod]
    public async Task AcquireTokenAsync_HungHandlerDoesNotBlockIndefinitely()
    {
        var tokenManager = CreateTokenManager(CreateTokenServiceClient());
        var release = new ManualResetEventSlim(false);
        tokenManager.TokenRefreshed += (_, _) => release.Wait();
        var stopwatch = Stopwatch.StartNew();

        try
        {
            await tokenManager.AcquireTokenAsync(throwOnFailure: true);
        }
        finally
        {
            release.Set();
        }

        Assert.IsLessThan(TimeSpan.FromSeconds(30), stopwatch.Elapsed);
        Assert.AreEqual("token", tokenManager.Token);
    }

    #endregion

    #region Token property

    [TestMethod]
    public async Task Token_WhenExpired_ReturnsCurrentValueAndRefreshesInBackground()
    {
        var pending = new TaskCompletionSource<TokenResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = A.Fake<ITokenServiceClient>();
        A.CallTo(() => client.GetTokenAsync(A<CancellationToken>._)).Returns(pending.Task);
        var tokenManager = CreateTokenManager(client);

        // No valid token yet: returns the current value without blocking, and starts a background refresh
        Assert.IsNull(tokenManager.Token);
        A.CallTo(() => client.GetTokenAsync(A<CancellationToken>._)).MustHaveHappenedOnceExactly();

        pending.SetResult(CreateTokenResult("fresh-token"));
        await WaitUntilAsync(() => tokenManager._tokenExpiry > DateTime.UtcNow);
        Assert.AreEqual("fresh-token", tokenManager.Token);
    }

    [TestMethod]
    public async Task Token_WhenValid_DoesNotTriggerRefresh()
    {
        var (tokenManager, client) = await CreateAuthenticatedTokenManagerAsync();

        Assert.AreEqual("token", tokenManager.Token);
        await Task.Delay(100);

        A.CallTo(() => client.GetTokenAsync(A<CancellationToken>._)).MustNotHaveHappened();
    }

    #endregion

    #region EnsureAuthenticationAsync / heartbeat

    [TestMethod]
    public async Task EnsureAuthentication_ValidToken_DoesNotRefresh()
    {
        var (tokenManager, client) = await CreateAuthenticatedTokenManagerAsync();

        await tokenManager.EnsureAuthenticationAsync();

        A.CallTo(() => client.GetTokenAsync(A<CancellationToken>._)).MustNotHaveHappened();
    }

    [TestMethod]
    public async Task EnsureAuthentication_RefreshesWhenShouldTokenBeRefreshedReturnsTrue()
    {
        var options = CreateAuthenticationOptions();
        var (tokenManager, client) = await CreateAuthenticatedTokenManagerAsync(options);
        var expiry = tokenManager._tokenExpiry;
        DateTime? acquiredArg = null;
        DateTime? expiryArg = null;
        options.ShouldTokenBeRefreshed = (acquired, exp) => { acquiredArg = acquired; expiryArg = exp; return true; };
        A.CallTo(() => client.GetTokenAsync(A<CancellationToken>._)).ReturnsLazily(() => CreateTokenResult("new-token", TimeSpan.FromHours(2)));

        await tokenManager.EnsureAuthenticationAsync();

        A.CallTo(() => client.GetTokenAsync(A<CancellationToken>._)).MustHaveHappenedOnceExactly();
        Assert.AreEqual("new-token", tokenManager.Token);
        Assert.AreEqual(expiry, expiryArg);
        Assert.IsNotNull(acquiredArg);
        Assert.IsLessThan(expiry, acquiredArg.Value);
    }

    [TestMethod]
    public async Task EnsureAuthentication_RefreshesExpiredToken_EvenIfShouldTokenBeRefreshedReturnsFalse()
    {
        var options = CreateAuthenticationOptions();
        options.ShouldTokenBeRefreshed = (_, _) => false;
        var client = A.Fake<ITokenServiceClient>();
        A.CallTo(() => client.GetTokenAsync(A<CancellationToken>._)).ReturnsNextFromSequence(
            CreateTokenResult("expired-token", TimeSpan.FromMinutes(-1)),
            CreateTokenResult("new-token", TimeSpan.FromHours(1)));
        var tokenManager = CreateTokenManager(client, options);
        await tokenManager.AcquireTokenAsync(throwOnFailure: true);

        await tokenManager.EnsureAuthenticationAsync();

        Assert.AreEqual("new-token", tokenManager.Token);
    }

    [TestMethod]
    public async Task EnsureAuthentication_SkipsConcurrentCalls()
    {
        var options = CreateAuthenticationOptions();
        var pending = new TaskCompletionSource<TokenResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = A.Fake<ITokenServiceClient>();
        A.CallTo(() => client.GetTokenAsync(A<CancellationToken>._)).Returns(pending.Task);
        var tokenManager = CreateTokenManager(client, options);

        var first = tokenManager.EnsureAuthenticationAsync();
        var second = tokenManager.EnsureAuthenticationAsync();
        await second;

        Assert.IsFalse(first.IsCompleted);
        A.CallTo(() => client.GetTokenAsync(A<CancellationToken>._)).MustHaveHappenedOnceExactly();

        pending.SetResult(CreateTokenResult("token"));
        await first;
        Assert.AreEqual("token", tokenManager.Token);

        // The in-progress flag is released once the first call completes
        options.ShouldTokenBeRefreshed = (_, _) => true;
        A.CallTo(() => client.GetTokenAsync(A<CancellationToken>._)).ReturnsLazily(() => CreateTokenResult("token2", TimeSpan.FromHours(2)));
        await tokenManager.EnsureAuthenticationAsync();
        Assert.AreEqual("token2", tokenManager.Token);
    }

    [TestMethod]
    public async Task EnsureAuthentication_DoesNotThrow_WhenUserNameCannotBeResolved()
    {
        var options = CreateAuthenticationOptions();
        options.GetUserName = _ => throw new InvalidOperationException("bad token");
        var tokenManager = CreateTokenManager(CreateTokenServiceClient(), options);

        await tokenManager.EnsureAuthenticationAsync();

        Assert.IsNull(tokenManager.IdentityObjectId);
    }

    [TestMethod]
    public async Task Heartbeat_PeriodicallyRefreshesToken()
    {
        var calls = 0;
        var credential = A.Fake<TokenCredential>();
        A.CallTo(() => credential.GetTokenAsync(A<TokenRequestContext>._, A<CancellationToken>._))
            .ReturnsLazily(() =>
            {
                Interlocked.Increment(ref calls);
                return new ValueTask<AccessToken>(new AccessToken("token", DateTimeOffset.UtcNow.AddHours(1)));
            });
        var options = CreateAuthenticationOptions();
        options.TokenCredential = credential;
        options.TokenHeartbeatInterval = TimeSpan.FromMilliseconds(50);
        options.ShouldTokenBeRefreshed = (_, _) => true;

        var tokenManager = new TokenManager(options, loggerFactory: null);
        try
        {
            await WaitUntilAsync(() => Volatile.Read(ref calls) >= 2);
            Assert.AreEqual("token", tokenManager.Token);
        }
        finally
        {
            tokenManager._heartbeatTimer!.Stop();
        }
    }

    #endregion

    #region Connection tracking

    [TestMethod]
    public async Task StartManagingConnection_TracksConnectionWithCurrentTokenExpiryAndStartsHeartbeat()
    {
        var (tokenManager, _) = await CreateAuthenticatedTokenManagerAsync();
        var timer = tokenManager._heartbeatTimer!;
        timer.Stop();
        using var multiplexer = await ConnectionMultiplexer.ConnectAsync("127.0.0.1:1,abortConnect=false,connectTimeout=100,connectRetry=0");

        tokenManager.StartManagingConnection(multiplexer);

        try
        {
            Assert.AreEqual(1, tokenManager._connectionCount);
            var connection = tokenManager._connections.Values.Single();
            Assert.AreEqual(tokenManager._tokenExpiry, connection.TokenExpiry);
            Assert.IsTrue(connection.ConnectionMultiplexerReference.TryGetTarget(out var target));
            Assert.AreSame(multiplexer, target);
            Assert.IsTrue(timer.Enabled);
        }
        finally
        {
            timer.Stop();
        }
    }

    #endregion
}

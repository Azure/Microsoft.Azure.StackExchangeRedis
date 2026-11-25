// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Collections.Concurrent;
using FakeItEasy;
using StackExchange.Redis;
using static Microsoft.Azure.StackExchangeRedis.Tests.TestHelpers;

namespace Microsoft.Azure.StackExchangeRedis.Tests;

[TestClass]
public class TokenManagerReauthenticationTests
{
    [TestMethod]
    public async Task Reauthenticate_SendsAuthWithIdentityAndToken_AndRecordsNewExpiry()
    {
        var (tokenManager, _) = await CreateAuthenticatedTokenManagerAsync(token: "current-token");
        var server1 = CreateServer("server1");
        var server2 = CreateServer("server2");
        var connection = tokenManager.AddManagedConnection(CreateMultiplexer(server1, server2), DateTime.MinValue);
        var reauthenticated = new ConcurrentBag<(object? Sender, string Endpoint)>();
        tokenManager.ConnectionReauthenticated += (s, e) => reauthenticated.Add((s, e));

        await tokenManager.EnsureAuthenticationAsync();

        foreach (var server in new[] { server1, server2 })
        {
            A.CallTo(() => server.ExecuteAsync("AUTH", A<object[]>.That.IsSameSequenceAs(new object[] { TestUser, "current-token" })))
                .MustHaveHappenedOnceExactly();
        }
        Assert.AreEqual(tokenManager._tokenExpiry, connection.TokenExpiry);
        CollectionAssert.AreEquivalent(
            new[] { server1.EndPoint.ToString(), server2.EndPoint.ToString() },
            reauthenticated.Select(r => r.Endpoint).ToArray());
        Assert.IsTrue(reauthenticated.All(r => ReferenceEquals(r.Sender, tokenManager)));
    }

    [TestMethod]
    public async Task Reauthenticate_UsesNewlyRefreshedToken()
    {
        var options = CreateAuthenticationOptions();
        var (tokenManager, client) = await CreateAuthenticatedTokenManagerAsync(options, token: "old-token");
        var server = CreateServer("server");
        var connection = tokenManager.AddManagedConnection(CreateMultiplexer(server), tokenManager._tokenExpiry);
        options.ShouldTokenBeRefreshed = (_, _) => true;
        A.CallTo(() => client.GetTokenAsync(A<CancellationToken>._)).ReturnsLazily(() => CreateTokenResult("new-token", TimeSpan.FromHours(2)));

        await tokenManager.EnsureAuthenticationAsync();

        A.CallTo(() => server.ExecuteAsync("AUTH", A<object[]>.That.IsSameSequenceAs(new object[] { TestUser, "new-token" })))
            .MustHaveHappenedOnceExactly();
        Assert.AreEqual(tokenManager._tokenExpiry, connection.TokenExpiry);
    }

    [TestMethod]
    public async Task Reauthenticate_SkipsConnectionsAlreadyUsingCurrentToken()
    {
        var (tokenManager, _) = await CreateAuthenticatedTokenManagerAsync();
        var server = CreateServer("server");
        var multiplexer = CreateMultiplexer(server);
        tokenManager.AddManagedConnection(multiplexer, tokenManager._tokenExpiry);

        await tokenManager.EnsureAuthenticationAsync();

        A.CallTo(() => multiplexer.GetServers()).MustNotHaveHappened();
        A.CallTo(() => server.ExecuteAsync(A<string>._, A<object[]>._)).MustNotHaveHappened();
    }

    [TestMethod]
    public async Task Reauthenticate_SkipsDisconnectedServers()
    {
        var (tokenManager, _) = await CreateAuthenticatedTokenManagerAsync();
        var connected = CreateServer("connected");
        var disconnected = CreateServer("disconnected", isConnected: false);
        var connection = tokenManager.AddManagedConnection(CreateMultiplexer(connected, disconnected), DateTime.MinValue);

        await tokenManager.EnsureAuthenticationAsync();

        A.CallTo(() => connected.ExecuteAsync("AUTH", A<object[]>._)).MustHaveHappenedOnceExactly();
        A.CallTo(() => disconnected.ExecuteAsync(A<string>._, A<object[]>._)).MustNotHaveHappened();
        // A disconnected server will authenticate with the current token when it reconnects
        Assert.AreEqual(tokenManager._tokenExpiry, connection.TokenExpiry);
    }

    [TestMethod]
    public async Task Reauthenticate_ServerFailure_RaisesFailedEvent_AndRetriesOnNextHeartbeat()
    {
        var (tokenManager, _) = await CreateAuthenticatedTokenManagerAsync();
        var healthy = CreateServer("healthy");
        var failing = CreateServer("failing");
        var failure = new RedisException("AUTH timed out");
        A.CallTo(() => failing.ExecuteAsync("AUTH", A<object[]>._)).ThrowsAsync(failure);
        var connection = tokenManager.AddManagedConnection(CreateMultiplexer(healthy, failing), DateTime.MinValue);
        var failedEvents = new ConcurrentBag<ConnectionReauthenticationFailedEventArgs>();
        var reauthenticated = new ConcurrentBag<string>();
        tokenManager.ConnectionReauthenticationFailed += (_, e) => failedEvents.Add(e);
        tokenManager.ConnectionReauthenticated += (_, e) => reauthenticated.Add(e);

        await tokenManager.EnsureAuthenticationAsync();

        Assert.AreEqual(DateTime.MinValue, connection.TokenExpiry, "Expiry must not advance while any server failed to re-authenticate");
        var failed = failedEvents.Single();
        Assert.AreSame(failure, failed.Exception);
        Assert.AreEqual(failing.EndPoint.ToString(), failed.Endpoint);
        CollectionAssert.AreEqual(new[] { healthy.EndPoint.ToString() }, reauthenticated.ToArray());

        // Next heartbeat succeeds
        A.CallTo(() => failing.ExecuteAsync("AUTH", A<object[]>._)).Returns(RedisResult.Create((RedisValue)"OK"));
        await tokenManager.EnsureAuthenticationAsync();

        Assert.AreEqual(tokenManager._tokenExpiry, connection.TokenExpiry);
        A.CallTo(() => healthy.ExecuteAsync("AUTH", A<object[]>._)).MustHaveHappenedTwiceExactly();
    }

    [TestMethod]
    public async Task Reauthenticate_MultiplexerFailure_RaisesFailedEventWithClientName_AndKeepsManagingConnection()
    {
        var (tokenManager, _) = await CreateAuthenticatedTokenManagerAsync();
        var multiplexer = CreateMultiplexer();
        var failure = new InvalidOperationException("GetServers failed");
        A.CallTo(() => multiplexer.GetServers()).Throws(failure);
        var connection = tokenManager.AddManagedConnection(multiplexer, DateTime.MinValue);
        ConnectionReauthenticationFailedEventArgs? failedArgs = null;
        tokenManager.ConnectionReauthenticationFailed += (_, e) => failedArgs = e;

        await tokenManager.EnsureAuthenticationAsync();

        Assert.IsNotNull(failedArgs);
        Assert.AreSame(failure, failedArgs.Exception);
        Assert.AreEqual("test-client", failedArgs.Endpoint);
        Assert.AreEqual(DateTime.MinValue, connection.TokenExpiry);
        Assert.AreEqual(1, tokenManager._connectionCount);
    }

    [TestMethod]
    public async Task Reauthenticate_DisposedMultiplexer_StopsManagingConnectionAndHeartbeat()
    {
        var (tokenManager, _) = await CreateAuthenticatedTokenManagerAsync();
        var multiplexer = CreateMultiplexer();
        A.CallTo(() => multiplexer.GetServers()).Throws(new ObjectDisposedException("multiplexer"));
        tokenManager.AddManagedConnection(multiplexer, DateTime.MinValue);
        var timer = tokenManager._heartbeatTimer!;
        var failedEvents = 0;
        tokenManager.ConnectionReauthenticationFailed += (_, _) => Interlocked.Increment(ref failedEvents);

        await tokenManager.EnsureAuthenticationAsync();

        Assert.AreEqual(0, tokenManager._connectionCount);
        Assert.IsEmpty(tokenManager._connections);
        Assert.IsFalse(timer.Enabled);
        Assert.AreEqual(0, failedEvents);
    }

    [TestMethod]
    public async Task Reauthenticate_ServerReportsDisposed_StopsManagingConnection()
    {
        var (tokenManager, _) = await CreateAuthenticatedTokenManagerAsync();
        var server = CreateServer("server");
        A.CallTo(() => server.ExecuteAsync("AUTH", A<object[]>._)).ThrowsAsync(new ObjectDisposedException("multiplexer"));
        tokenManager.AddManagedConnection(CreateMultiplexer(server), DateTime.MinValue);
        var failedEvents = 0;
        tokenManager.ConnectionReauthenticationFailed += (_, _) => Interlocked.Increment(ref failedEvents);

        await tokenManager.EnsureAuthenticationAsync();

        Assert.AreEqual(0, tokenManager._connectionCount);
        Assert.AreEqual(0, failedEvents);
    }

    [TestMethod]
    public async Task Reauthenticate_CollectedMultiplexer_StopsManagingConnection()
    {
        var (tokenManager, _) = await CreateAuthenticatedTokenManagerAsync();
        var released = tokenManager.AddManagedConnection(CreateMultiplexer(CreateServer("released")), DateTime.MinValue);
        var liveServer = CreateServer("live");
        var live = tokenManager.AddManagedConnection(CreateMultiplexer(liveServer), DateTime.MinValue);
        released.ConnectionMultiplexerReference.SetTarget(null!); // Simulate the app releasing the multiplexer and it being garbage collected
        var timer = tokenManager._heartbeatTimer!;

        await tokenManager.EnsureAuthenticationAsync();

        Assert.AreEqual(1, tokenManager._connectionCount);
        Assert.AreSame(live, tokenManager._connections.Values.Single());
        Assert.AreEqual(tokenManager._tokenExpiry, live.TokenExpiry);
        A.CallTo(() => liveServer.ExecuteAsync("AUTH", A<object[]>._)).MustHaveHappenedOnceExactly();
        Assert.IsTrue(timer.Enabled, "Heartbeat continues while connections remain");
        timer.Stop();
    }

    [TestMethod]
    public async Task Reauthenticate_HandlerExceptionsDoNotPreventReauthentication()
    {
        var (tokenManager, _) = await CreateAuthenticatedTokenManagerAsync();
        var connection = tokenManager.AddManagedConnection(CreateMultiplexer(CreateServer("server")), DateTime.MinValue);
        tokenManager.ConnectionReauthenticated += (_, _) => throw new InvalidOperationException("handler failure");

        await tokenManager.EnsureAuthenticationAsync();

        Assert.AreEqual(tokenManager._tokenExpiry, connection.TokenExpiry);
    }
}

// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Azure.Core;
using FakeItEasy;
using StackExchange.Redis;

namespace Microsoft.Azure.StackExchangeRedis.Tests;

internal static class TestHelpers
{
    internal const string TestUser = "test-user";

    internal static TokenResult CreateTokenResult(string token = "token", TimeSpan? lifetime = null)
        => new(new AccessToken(token, DateTimeOffset.UtcNow + (lifetime ?? TimeSpan.FromHours(1))));

    /// <summary>
    /// Options that avoid real identity calls, slow backoff, and background heartbeats during tests.
    /// </summary>
    internal static AuthenticationOptions CreateAuthenticationOptions() => new()
    {
        TokenCredential = A.Fake<TokenCredential>(),
        GetUserName = _ => TestUser,
        TokenHeartbeatInterval = TimeSpan.FromHours(1),
        MaxTokenRefreshAttempts = 3,
        TokenRefreshBackoff = (_, _) => TimeSpan.Zero,
    };

    internal static ITokenServiceClient CreateTokenServiceClient(string token = "token", TimeSpan? lifetime = null)
    {
        var client = A.Fake<ITokenServiceClient>();
        A.CallTo(() => client.GetTokenAsync(A<CancellationToken>._)).ReturnsLazily(() => CreateTokenResult(token, lifetime));
        return client;
    }

    internal static TokenManager CreateTokenManager(ITokenServiceClient tokenServiceClient, AuthenticationOptions? options = null)
        => new(options ?? CreateAuthenticationOptions(), loggerFactory: null)
        {
            TokenServiceClient = tokenServiceClient,
        };

    internal static async Task<(TokenManager TokenManager, ITokenServiceClient Client)> CreateAuthenticatedTokenManagerAsync(AuthenticationOptions? options = null, string token = "token")
    {
        var client = CreateTokenServiceClient(token);
        var tokenManager = CreateTokenManager(client, options);
        await tokenManager.AcquireTokenAsync(throwOnFailure: true);
        Fake.ClearRecordedCalls(client);
        return (tokenManager, client);
    }

    internal static string CreateJwt(string? oid)
    {
        var header = Base64UrlEncode("{\"alg\":\"HS256\",\"typ\":\"JWT\"}");
        var payload = Base64UrlEncode(oid is null
            ? "{\"sub\":\"subject\"}"
            : $"{{\"oid\":\"{oid}\",\"sub\":\"subject\"}}");
        return $"{header}.{payload}.{Base64UrlEncode("signature")}";
    }

    private static string Base64UrlEncode(string value)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes(value)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    internal static X509Certificate2 CreateSelfSignedCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=Microsoft.Azure.StackExchangeRedis.Tests", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
    }

    internal static IServer CreateServer(string host, bool isConnected = true)
    {
        var server = A.Fake<IServer>();
        A.CallTo(() => server.IsConnected).Returns(isConnected);
        A.CallTo(() => server.EndPoint).Returns(new DnsEndPoint(host, 6380));
        A.CallTo(() => server.ExecuteAsync(A<string>._, A<object[]>._)).Returns(RedisResult.Create((RedisValue)"OK"));
        return server;
    }

    internal static IConnectionMultiplexer CreateMultiplexer(params IServer[] servers)
    {
        var multiplexer = A.Fake<IConnectionMultiplexer>();
        A.CallTo(() => multiplexer.GetServers()).Returns(servers);
        A.CallTo(() => multiplexer.ClientName).Returns("test-client");
        return multiplexer;
    }

    internal static async Task WaitUntilAsync(Func<bool> condition, TimeSpan? timeout = null)
    {
        var stopwatch = Stopwatch.StartNew();
        var limit = timeout ?? TimeSpan.FromSeconds(10);
        while (!condition())
        {
            if (stopwatch.Elapsed > limit)
            {
                Assert.Fail($"Condition was not met within {limit}.");
            }
            await Task.Delay(10);
        }
    }
}

internal static class TokenManagerTestExtensions
{
    /// <summary>
    /// Starts managing <paramref name="multiplexer"/>, then sets the expiry of the token it was last authenticated with.
    /// </summary>
    internal static Connection AddManagedConnection(this TokenManager tokenManager, IConnectionMultiplexer multiplexer, DateTime tokenExpiry)
    {
        tokenManager.StartManagingConnection(multiplexer);
        var connection = tokenManager._connections.Values.Single(c => c.ConnectionMultiplexerReference.TryGetTarget(out var target) && ReferenceEquals(target, multiplexer));
        connection.TokenExpiry = tokenExpiry;
        return connection;
    }
}
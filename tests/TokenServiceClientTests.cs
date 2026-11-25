// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using Azure.Core;
using FakeItEasy;
using Microsoft.Identity.Client;

namespace Microsoft.Azure.StackExchangeRedis.Tests;

[TestClass]
public class TokenServiceClientTests
{
    private const string ClientId = "00000000-0000-0000-0000-000000000001";
    private const string TenantId = "00000000-0000-0000-0000-000000000002";

    [TestMethod]
    public async Task CreateForTokenCredential_RequestsScopeAndReturnsToken()
    {
        var credential = A.Fake<TokenCredential>();
        var expiresOn = DateTimeOffset.UtcNow.AddMinutes(30);
        TokenRequestContext? requestContext = null;
        CancellationToken capturedToken = default;
        A.CallTo(() => credential.GetTokenAsync(A<TokenRequestContext>._, A<CancellationToken>._))
            .ReturnsLazily((TokenRequestContext context, CancellationToken cancellationToken) =>
            {
                requestContext = context;
                capturedToken = cancellationToken;
                return new ValueTask<AccessToken>(new AccessToken("credential-token", expiresOn));
            });
        using var cts = new CancellationTokenSource();

        var client = TokenServiceClient.CreateForTokenCredential(credential, "https://custom.scope/.default");
        var result = await client.GetTokenAsync(cts.Token);

        Assert.AreEqual("credential-token", result.Token);
        Assert.AreEqual(expiresOn, result.ExpiresOn);
        Assert.IsNotNull(requestContext);
        CollectionAssert.AreEqual(new[] { "https://custom.scope/.default" }, requestContext.Value.Scopes);
        Assert.AreEqual(cts.Token, capturedToken);
    }

    [TestMethod]
    public async Task CreateForTokenCredential_PropagatesCredentialFailures()
    {
        var credential = A.Fake<TokenCredential>();
        var failure = new InvalidOperationException("credential failure");
        A.CallTo(() => credential.GetTokenAsync(A<TokenRequestContext>._, A<CancellationToken>._)).Throws(failure);

        var client = TokenServiceClient.CreateForTokenCredential(credential, "scope");

        var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => client.GetTokenAsync(CancellationToken.None));
        Assert.AreSame(failure, ex);
    }

    [TestMethod]
    [DataRow(null, DisplayName = "System-assigned")]
    [DataRow("00000000-0000-0000-0000-000000000003", DisplayName = "User-assigned client ID")]
    [DataRow("/subscriptions/sub/resourceGroups/rg/providers/Microsoft.ManagedIdentity/userAssignedIdentities/id", DisplayName = "User-assigned resource ID")]
    public void CreateForManagedIdentity_SupportsAllIdentityKinds(string? clientId)
    {
        var client = TokenServiceClient.CreateForManagedIdentity(new AuthenticationOptions { ClientId = clientId }, log: null);

        Assert.IsNotNull(client);
    }

    [TestMethod]
    public void CreateForServicePrincipal_WithSecret_Succeeds()
    {
        var options = new AuthenticationOptions
        {
            ClientId = ClientId,
            ServicePrincipalTenantId = TenantId,
            ServicePrincipalSecret = "secret",
        };

        Assert.IsNotNull(TokenServiceClient.CreateForServicePrincipal(options, log: null));
    }

    [TestMethod]
    [DataRow(AzureCloudInstance.AzurePublic, "https://login.microsoftonline.com/")]
    [DataRow(AzureCloudInstance.AzureChina, "https://login.chinacloudapi.cn/")]
    [DataRow(AzureCloudInstance.AzureUsGovernment, "https://login.microsoftonline.us/")]
    public void WithCloudAuthority_UsesCloudInstance_WhenCloudUriNotSet(AzureCloudInstance cloud, string expectedHost)
    {
        var options = new AuthenticationOptions { Cloud = cloud, ServicePrincipalTenantId = TenantId };

        var app = ConfidentialClientApplicationBuilder.Create(ClientId)
            .WithCloudAuthority(options)
            .WithClientSecret("secret")
            .Build();

        StringAssert.StartsWith(app.Authority, expectedHost + TenantId, StringComparison.OrdinalIgnoreCase);
    }

    [TestMethod]
    public void WithCloudAuthority_PrefersCloudUri()
    {
        var options = new AuthenticationOptions
        {
            Cloud = AzureCloudInstance.AzureChina,
            CloudUri = "https://login.custom.cloud",
            ServicePrincipalTenantId = TenantId,
        };

        var app = ConfidentialClientApplicationBuilder.Create(ClientId)
            .WithCloudAuthority(options)
            .WithClientSecret("secret")
            .Build();

        StringAssert.StartsWith(app.Authority, "https://login.custom.cloud/" + TenantId, StringComparison.OrdinalIgnoreCase);
    }

    [TestMethod]
    public void WithCredentials_UsesSecret_WhenNoCertificate()
    {
        var options = new AuthenticationOptions { ServicePrincipalSecret = "the-secret" };

        var app = ConfidentialClientApplicationBuilder.Create(ClientId)
            .WithCredentials(options)
            .Build();

        Assert.AreEqual("the-secret", app.AppConfig.ClientSecret);
        Assert.IsNull(app.AppConfig.ClientCredentialCertificate);
    }

    [TestMethod]
    public void WithCredentials_PrefersCertificate()
    {
        using var certificate = TestHelpers.CreateSelfSignedCertificate();
        var options = new AuthenticationOptions
        {
            ServicePrincipalSecret = "the-secret",
            ServicePrincipalCertificate = certificate,
        };

        var app = ConfidentialClientApplicationBuilder.Create(ClientId)
            .WithCredentials(options)
            .Build();

        Assert.AreSame(certificate, app.AppConfig.ClientCredentialCertificate);
        Assert.IsNull(app.AppConfig.ClientSecret);
    }
}

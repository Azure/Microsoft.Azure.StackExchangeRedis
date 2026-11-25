// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using Microsoft.Identity.Client;

namespace Microsoft.Azure.StackExchangeRedis.Tests;

[TestClass]
public class AuthenticationOptionsTests
{
    [TestMethod]
    public void Defaults_AreConfiguredForPublicCloudRedis()
    {
        var options = new AuthenticationOptions();

        Assert.AreEqual("https://redis.azure.com/.default", options.Scope);
        Assert.AreEqual(AzureCloudInstance.AzurePublic, options.Cloud);
        Assert.IsNull(options.CloudUri);
        Assert.IsNull(options.ClientId);
        Assert.IsNull(options.ServicePrincipalTenantId);
        Assert.IsNull(options.ServicePrincipalSecret);
        Assert.IsNull(options.ServicePrincipalCertificate);
        Assert.IsNull(options.TokenCredential);
        Assert.IsFalse(options.SendX5C);
    }

    [TestMethod]
    public void Defaults_TokenRefreshSettings()
    {
        var options = new AuthenticationOptions();

        Assert.AreEqual(TimeSpan.FromMinutes(2), options.TokenHeartbeatInterval);
        Assert.AreEqual(5, options.MaxTokenRefreshAttempts);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(4)]
    public void DefaultTokenRefreshBackoff_WaitsOneSecondPerCompletedAttempt(int attemptCount)
    {
        var options = new AuthenticationOptions();

        Assert.AreEqual(TimeSpan.FromSeconds(attemptCount), options.TokenRefreshBackoff(attemptCount, new Exception()));
        Assert.AreEqual(TimeSpan.FromSeconds(attemptCount), options.TokenRefreshBackoff(attemptCount, null));
    }

    [TestMethod]
    [DataRow(-10.0, true, DisplayName = "Expired")]
    [DataRow(1.0, true, DisplayName = "Expires in 1 minute")]
    [DataRow(4.9, true, DisplayName = "Expires in 4.9 minutes")]
    [DataRow(5.5, false, DisplayName = "Expires in 5.5 minutes")]
    [DataRow(60.0, false, DisplayName = "Expires in 60 minutes")]
    public void DefaultShouldTokenBeRefreshed_RefreshesWithinFiveMinutesOfExpiry(double minutesUntilExpiry, bool expected)
    {
        var options = new AuthenticationOptions();
        var acquired = DateTime.UtcNow.AddHours(-1);
        var expiry = DateTime.UtcNow.AddMinutes(minutesUntilExpiry);

        Assert.AreEqual(expected, options.ShouldTokenBeRefreshed(acquired, expiry));
    }

    [TestMethod]
    public void DefaultGetUserName_ReturnsOidClaim()
    {
        var options = new AuthenticationOptions();

        Assert.AreEqual("00000000-1111-2222-3333-444444444444", options.GetUserName(TestHelpers.CreateJwt("00000000-1111-2222-3333-444444444444")));
    }

    [TestMethod]
    public void DefaultGetUserName_NullToken_Throws()
    {
        var options = new AuthenticationOptions();

        var ex = Assert.ThrowsExactly<ArgumentNullException>(() => options.GetUserName(null));
        Assert.AreEqual("token", ex.ParamName);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("not-a-jwt")]
    public void DefaultGetUserName_UnreadableToken_Throws(string token)
    {
        var options = new AuthenticationOptions();

        var ex = Assert.ThrowsExactly<Exception>(() => options.GetUserName(token));
        Assert.AreEqual("Invalid token cannot be read", ex.Message);
    }

    [TestMethod]
    public void DefaultGetUserName_TokenWithoutOid_Throws()
    {
        var options = new AuthenticationOptions();

        var ex = Assert.ThrowsExactly<Exception>(() => options.GetUserName(TestHelpers.CreateJwt(oid: null)));
        Assert.AreEqual("oid not found in token claims", ex.Message);
    }
}

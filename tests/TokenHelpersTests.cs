// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using StackExchange.Redis;

namespace Microsoft.Azure.StackExchangeRedis.Tests
{
    [TestClass]
    public sealed class TokenHelpersTests
    {
        [TestMethod]
        public void TryGetOidFromToken_Should_ReturnFalse_When_TokenIsEmpty()
        {
            // Arrange
            const string token = "";

            // Act
            var result = TokenHelpers.TryGetOidFromToken(token, out var oid);

            // Assert
            Assert.IsFalse(result);
            Assert.IsNull(oid);
        }

        [TestMethod]
        public void TryGetOidFromToken_Should_ReturnFalse_When_TokenHasLessThanTwoParts()
        {
            // Arrange
            const string token = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9";

            // Act
            var result = TokenHelpers.TryGetOidFromToken(token, out var oid);

            // Assert
            Assert.IsFalse(result);
            Assert.IsNull(oid);
        }

        [TestMethod]
        public void TryGetOidFromToken_Should_ReturnFalse_When_TokenIsValidButNoOid()
        {
            // Arrange
            const string token =
                "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJvdGhlciI6IjEyMzQ1Njc4OTAifQ==";

            // Act
            var result = TokenHelpers.TryGetOidFromToken(token, out var oid);

            // Assert
            Assert.IsFalse(result);
            Assert.IsNull(oid);
        }

        [TestMethod]
        public void TryGetOidFromToken_Should_ReturnTrueAndOid_When_TokenIsValid()
        {
            // Arrange
            const string token =
                "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJvaWQiOiIxMjM0NTY3ODkwIn0=";

            // Act
            var result = TokenHelpers.TryGetOidFromToken(token, out var oid);

            // Assert
            Assert.IsTrue(result);
            Assert.AreEqual("1234567890", oid);
        }

        [TestMethod]
        public void TryGetOidFromToken_Should_ReturnFalse_When_TokenIsInvalidBase64String()
        {
            // Arrange
            const string token = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.invalidbase64";

            // Act
            var result = TokenHelpers.TryGetOidFromToken(token, out var oid);

            // Assert
            Assert.IsFalse(result);
            Assert.IsNull(oid);
        }

        [TestMethod]
        public void TryGetOidFromToken_Should_ThrowFormatException_When_TokenHasInvalidJsonFormat()
        {
            // Arrange
            const string token = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.W10="; // W10= -> []

            // Act
            var result = TokenHelpers.TryGetOidFromToken(token, out var oid);

            // Assert
            Assert.IsFalse(result);
            Assert.IsNull(oid);
        }
    }
}

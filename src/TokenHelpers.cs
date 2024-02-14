// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Buffers.Text;

namespace StackExchange.Redis
{
    internal static class TokenHelpers
    {
        public static bool TryGetOidFromToken(string token, out string? oid)
        {
            oid = null;

            if (string.IsNullOrEmpty(token))
            {
                return false;
            }

            var parts = token.Split('.');

            if (parts.Length < 2)
            {
                return false;
            }

            try
            {
                var decoded = Convert.FromBase64String(parts[1]);

                var json = System.Text.Encoding.UTF8.GetString(decoded);

                var jwt = System.Text.Json.JsonDocument.Parse(json);

                if (jwt.RootElement.TryGetProperty("oid", out var oidElement))
                {
                    oid = oidElement.GetString();
                    return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }
    }
}

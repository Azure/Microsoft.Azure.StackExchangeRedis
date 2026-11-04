// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using StackExchange.Redis;
using StackExchange.Redis.Configuration;

namespace Microsoft.Azure.StackExchangeRedis;

internal static class AzureCacheOptionsProviderFactory
{
    internal static DefaultOptionsProvider CreateDefaultsForAccessKey(EndPointCollection endPoints)
    {
        if (IsAzureManagedRedisEndpoint(endPoints))
        {
            return new AzureManagedRedisCacheOptionsProvider();
        }

        return new AzureCacheOptionsProvider();
    }

    internal static DefaultOptionsProvider CreateDefaultsWithToken(EndPointCollection endPoints, RedisTokenAuthenticationManager tokenAuthenticationManager)
    {
        if (IsAzureManagedRedisEndpoint(endPoints))
        {
            return new AzureManagedRedisCacheOptionsProviderWithToken(tokenAuthenticationManager);
        }

        return new AzureCacheOptionsProviderWithToken(tokenAuthenticationManager);
    }

    private static bool IsAzureManagedRedisEndpoint(EndPointCollection endPoints)
    {
        var managedRedisOptionsProvider = new AzureManagedRedisCacheOptionsProvider();

        foreach (var endPoint in endPoints)
        {
            if (managedRedisOptionsProvider.IsMatch(endPoint))
            {
                return true;
            }
        }

        return false;
    }

}

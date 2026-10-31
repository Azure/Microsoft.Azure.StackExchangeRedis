// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using StackExchange.Redis;
using StackExchange.Redis.Configuration;

namespace Microsoft.Azure.StackExchangeRedis;

internal static class AzureCacheOptionsProviderFactory
{
    internal static DefaultOptionsProvider CreateDefaultsForAccessKey(EndPointCollection endPoints)
    {
        var managedRedisOptionsProvider = new AzureManagedRedisCacheOptionsProvider();
        if (managedRedisOptionsProvider.IsAzureManagedRedisEndpoint(endPoints))
        {
            return managedRedisOptionsProvider;
        }

        return new AzureCacheOptionsProvider();
    }

    internal static DefaultOptionsProvider CreateDefaultsWithToken(EndPointCollection endPoints, RedisTokenAuthenticationManager tokenAuthenticationManager)
    {
        var managedRedisOptionsProvider = new AzureManagedRedisCacheOptionsProviderWithToken(tokenAuthenticationManager);
        if (managedRedisOptionsProvider.IsAzureManagedRedisEndpoint(endPoints))
        {
            return managedRedisOptionsProvider;
        }

        return new AzureCacheOptionsProviderWithToken(tokenAuthenticationManager);
    }

    private static bool IsAzureManagedRedisEndpoint(this AzureManagedRedisCacheOptionsProvider managedRedisOptionsProvider, EndPointCollection endPoints)
    {
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

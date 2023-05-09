// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using StackExchange.Redis.Configuration;

namespace Microsoft.Azure.StackExchangeRedis;

/// <summary>
/// Redis connection configuration options provider for use with Azure Cache for Redis.
/// </summary>
internal class AzureCacheOptionsProvider : AzureOptionsProvider
{
    /// <summary>
    /// Default to false to continue attempting to connect after an initial failure.
    /// This provides more resilience in cloud application environments. 
    /// </summary>
    public override bool AbortOnConnectFail => false;

    /// <summary>
    /// The identifier used to mark connections being managed by this extension package.
    /// </summary>
    public override string LibraryName => "Az.SE.Redis";

}

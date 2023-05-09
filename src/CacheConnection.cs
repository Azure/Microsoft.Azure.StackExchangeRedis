// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using StackExchange.Redis;

namespace Microsoft.Azure.StackExchangeRedis;

internal class CacheConnection
{
    internal WeakReference<IConnectionMultiplexer> ConnectionMultiplexerReference { get; }

    // The expiry of the last token that was used to successfully re-authenticate all server connections in this cache connection
    internal DateTime TokenExpiry;

    internal CacheConnection(IConnectionMultiplexer connectionMultiplexer, DateTime tokenExpiry)
    {
        ConnectionMultiplexerReference = new(connectionMultiplexer);
        TokenExpiry = tokenExpiry;
    }

}

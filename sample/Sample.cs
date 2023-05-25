// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using Microsoft.Azure.StackExchangeRedis;
using StackExchange.Redis;
using static System.Console;

WriteLine(
@"Options for connecting to an Azure Cache for Redis resource:
    1. Authenticate using an access key
    2. Authenticate using a system-assigned managed identity
    3. Authenticate using a user-assigned managed identity
    4. Authenticate using service principal
    5. Exit");
WriteLine();
Write("Enter a number: ");
var option = ReadLine()?.Trim();

// NOTE: ConnectionMultiplexer instances should be as long-lived as possible. Ideally a single ConnectionMultiplexer per cache is reused over the lifetime of the client application process.
ConnectionMultiplexer? connectionMultiplexer = null;
StringWriter connectionLog = new();

try
{
    switch (option)
    {
        case "1": // Access key
            Write("Redis cache connection string: ");
            var connectionString = ReadLine()?.Trim();
            WriteLine("Connecting with an access key...");

            connectionMultiplexer = ConnectionMultiplexer.Connect(connectionString!, AzureCacheForRedis.ConfigureForAzure, connectionLog);
            break;

        case "2": // System-Assigned managed identity
            Write("Redis cache host name: ");
            var cacheHostName = ReadLine()?.Trim();
            Write("Principal (object) ID of the client resource's system-assigned managed identity ('Username' from the 'Data Access Configuration' blade on the Azure Cache for Redis resource): ");
            var principalId = ReadLine()?.Trim();
            WriteLine("Connecting with a system-assigned managed identity...");

            var configurationOptions = await ConfigurationOptions.Parse($"{cacheHostName}:6380").ConfigureForAzureWithSystemAssignedManagedIdentityAsync(principalId!);
            configurationOptions.AbortOnConnectFail = true; // Fail fast for the purposes of this sample. In production code, this should remain false to retry connections on startup
            LogTokenEvents(configurationOptions);

            connectionMultiplexer = await ConnectionMultiplexer.ConnectAsync(configurationOptions, connectionLog);
            break;

        case "3": // User-Assigned managed identity
            Write("Redis cache host name: ");
            cacheHostName = ReadLine()?.Trim();
            Write("Managed identity Client ID or resource ID: ");
            var managedIdentityId = ReadLine()?.Trim();
            Write("Managed identity Principal (object) ID ('Username' from the 'Data Access Configuration' blade on the Azure Cache for Redis resource): ");
            principalId = ReadLine()?.Trim();
            WriteLine("Connecting with a user-assigned managed identity...");

            configurationOptions = await ConfigurationOptions.Parse($"{cacheHostName}:6380").ConfigureForAzureWithUserAssignedManagedIdentityAsync(managedIdentityId!, principalId!);
            configurationOptions.AbortOnConnectFail = true; // Fail fast for the purposes of this sample. In production code, this should remain false to retry connections on startup
            LogTokenEvents(configurationOptions);

            connectionMultiplexer = await ConnectionMultiplexer.ConnectAsync(configurationOptions, connectionLog);
            break;

        case "4": // Service principal
            Write("Redis cache host name: ");
            cacheHostName = ReadLine()?.Trim();
            Write("Service principal Application (client) ID: ");
            var clientId = ReadLine()?.Trim();
            Write("Principal (object) ID of the service principal ('Username' from the 'Data Access Configuration' blade on the Azure Cache for Redis resource): ");
            principalId = ReadLine()?.Trim();
            Write("Service principal Tenant ID: ");
            var tenantId = ReadLine()?.Trim();
            Write("Service principal secret: ");
            var secret = ReadLine()?.Trim();
            WriteLine("Connecting with a service principal...");

            configurationOptions = await ConfigurationOptions.Parse($"{cacheHostName}:6380").ConfigureForAzureWithServicePrincipalAsync(clientId!, principalId!, tenantId!, secret!);
            configurationOptions.AbortOnConnectFail = true; // Fail fast for the purposes of this sample. In production code, this should remain false to retry connections on startup
            LogTokenEvents(configurationOptions);

            connectionMultiplexer = await ConnectionMultiplexer.ConnectAsync(configurationOptions, connectionLog);
            break;

        default:
            return;
    }

    WriteLine("Connected successfully!");
    WriteLine();
}
catch (Exception ex)
{
    Error.WriteLine($"Failed to connect: {ex}");
    WriteLine();
    return;
}
finally
{
    WriteLine("Connection log from StackExchange.Redis:");
    WriteLine(connectionLog);
}

// This loop will execute commands on the Redis cache every five seconds indefinitely. 
// Let it run for more than 24 hours to see how the connection remains functional even after the initial token has expired. 
var database = connectionMultiplexer?.GetDatabase();
while (true)
{
    // Read a write a key every 2 minutes and output a '+' to show that the connection is working
    try
    {
        // NOTE: Always use the *Async() versions of StackExchange.Redis methods if possible (e.g. StringSetAsync(), StringGetAsync())
        var value = await database!.StringGetAsync("key");
        await database.StringSetAsync("key", DateTime.UtcNow.ToString());
        Write("+");
    }
    catch (Exception ex)
    {
        // NOTE: Production applications should implement a retry strategy to handle any commands that fail
        Error.WriteLine($"Failed to execute a Redis command: {ex}");
    }

    await Task.Delay(TimeSpan.FromMinutes(2));
}

static void LogTokenEvents(ConfigurationOptions configurationOptions)
{
    if (configurationOptions.Defaults is IAzureCacheTokenEvents tokenEvents)
    {
        static void Log(string message) => WriteLine($"{DateTime.Now:s}: {message}");

        tokenEvents.TokenRefreshed += (sender, authenticationResult) => Log($"Token refreshed. New token will expire at {authenticationResult.ExpiresOn}");
        tokenEvents.TokenRefreshFailed += (sender, args) => Log($"Token refresh failed for token expiring at {args.Expiry}: {args.Exception}");
        tokenEvents.ConnectionReauthenticated += (sender, endpoint) => Log($"Re-authenticated connection to '{endpoint}'");
        tokenEvents.ConnectionReauthenticationFailed += (sender, args) => Log($"Re-authentication of connection to '{args.Endpoint}' failed: {args.Exception}");
    }
}

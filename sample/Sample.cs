// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using Microsoft.Azure.StackExchangeRedis;
using StackExchange.Redis;

Console.WriteLine(
@"Options for connecting to an Azure Cache for Redis resource:
    1. Authenticate using an access key
    2. Authenticate using a system-assigned managed identity
    3. Authenticate using a user-assigned managed identity
    4. Authenticate using service principal
    5. Exit");
Console.WriteLine();
Console.Write("Enter a number: ");
var option = Console.ReadLine();

StringWriter connectionLog = new();
ConnectionMultiplexer? connectionMultiplexer = null;
try
{
    switch (option)
    {
        case "1": // Access key
            Console.Write("Redis cache connection string: ");
            var connectionString = Console.ReadLine();
            Console.WriteLine("Connecting with an access key...");

            connectionMultiplexer = ConnectionMultiplexer.Connect(connectionString!, AzureCacheForRedis.ConfigureForAzure, connectionLog);
            break;

        case "2": // System-Assigned managed identity
            Console.Write("Redis cache host name: ");
            var cacheHostName = Console.ReadLine();
            Console.Write("Principal (object) ID of the client resource's system-assigned managed identity ('Username' from the 'Data Access Configuration' blade on the Azure Cache for Redis resource): ");
            var principalId = Console.ReadLine();
            Console.WriteLine("Connecting with a system-assigned managed identity...");

            var configurationOptions = await ConfigurationOptions.Parse($"{cacheHostName}:6380").ConfigureForAzureWithSystemAssignedManagedIdentityAsync(principalId!);
            configurationOptions.AbortOnConnectFail = true; // Fail fast for the purposes of this sample. In production code, this should remain false to retry connections on startup
            LogTokenEvents(configurationOptions);

            connectionMultiplexer = await ConnectionMultiplexer.ConnectAsync(configurationOptions, connectionLog);
            break;

        case "3": // User-Assigned managed identity
            Console.Write("Redis cache host name: ");
            cacheHostName = Console.ReadLine();
            Console.Write("Managed identity Client ID: ");
            var managedIdentityClientId = Console.ReadLine();
            Console.Write("Managed identity Principal (object) ID ('Username' from the 'Data Access Configuration' blade on the Azure Cache for Redis resource): ");
            principalId = Console.ReadLine();
            Console.WriteLine("Connecting with a user-assigned managed identity...");

            configurationOptions = await ConfigurationOptions.Parse($"{cacheHostName}:6380").ConfigureForAzureWithUserAssignedManagedIdentityAsync(managedIdentityClientId!, principalId!);
            configurationOptions.AbortOnConnectFail = true; // Fail fast for the purposes of this sample. In production code, this should remain false to retry connections on startup
            LogTokenEvents(configurationOptions);

            connectionMultiplexer = await ConnectionMultiplexer.ConnectAsync(configurationOptions, connectionLog);
            break;

        case "4": // Service principal
            Console.Write("Redis cache host name: ");
            cacheHostName = Console.ReadLine();
            Console.Write("Service principal Application (client) ID: ");
            var clientId = Console.ReadLine();
            Console.Write("Principal (object) ID of the service principal ('Username' from the 'Data Access Configuration' blade on the Azure Cache for Redis resource): ");
            principalId = Console.ReadLine();
            Console.Write("Service principal Tenant ID: ");
            var tenantId = Console.ReadLine();
            Console.Write("Service principal secret: ");
            var secret = Console.ReadLine();
            Console.WriteLine("Connecting with a service principal...");

            configurationOptions = await ConfigurationOptions.Parse($"{cacheHostName}:6380").ConfigureForAzureWithServicePrincipalAsync(clientId!, principalId!, tenantId!, secret!);
            configurationOptions.AbortOnConnectFail = true; // Fail fast for the purposes of this sample. In production code, this should remain false to retry connections on startup
            LogTokenEvents(configurationOptions);

            connectionMultiplexer = await ConnectionMultiplexer.ConnectAsync(configurationOptions, connectionLog);
            break;

        default:
            return;
    }

    Console.WriteLine("Connected successfully!");
    Console.WriteLine();
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Failed to connect: {ex}");
    Console.WriteLine();
    return;
}
finally
{
    Console.WriteLine("Connection log from StackExchange.Redis:");
    Console.WriteLine(connectionLog);
}

// This loop will execute commands on the Redis cache every five seconds indefinitely. 
// Let it run for more than 24 hours to see how the connection remains functional even after the initial token has expired. 
var database = connectionMultiplexer?.GetDatabase();
while (true)
{
    // Read a write a key every 2 minutes and output a '+' to show that the connection is working
    try
    {
        var value = await database!.StringGetAsync("key");
        await database.StringSetAsync("key", DateTime.UtcNow.ToString());
        Console.Write("+");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Failed to execute a Redis command: {ex}");
    }

    await Task.Delay(TimeSpan.FromMinutes(2));
}

static void LogTokenEvents(ConfigurationOptions configurationOptions)
{
    if (configurationOptions.Defaults is IAzureCacheTokenEvents tokenEvents)
    {
        static void Log(string message) => Console.WriteLine($"{DateTime.Now:s}: {message}");

        tokenEvents.TokenRefreshed += (sender, expiry) => Log($"Token refreshed. New token will expire at {expiry}");
        tokenEvents.TokenRefreshFailed += (sender, args) => Log($"Token refresh failed for token expiring at {args.Expiry}: {args.Exception}");
        tokenEvents.ConnectionReauthenticated += (sender, endpoint) => Log($"Re-authenticated connection to '{endpoint}'");
        tokenEvents.ConnectionReauthenticationFailed += (sender, args) => Log($"Re-authentication of connection to '{args.Endpoint}' failed: {args.Exception}");
    }
}

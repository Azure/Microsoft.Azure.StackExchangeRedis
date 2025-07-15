// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Security.Cryptography.X509Certificates;
using Azure.Identity;
using Microsoft.Azure.StackExchangeRedis;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

using var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.SetMinimumLevel(LogLevel.Trace);
    builder.AddSimpleConsole(options =>
    {
        options.IncludeScopes = true;
        options.SingleLine = true;
        options.UseUtcTimestamp = true;
        options.TimestampFormat = "HH:mm:ss ";
    });
});
var consoleLogger = loggerFactory.CreateLogger("Sample");

ConfigurationOptions configurationOptions = new()
{
    // Use the RESP3 protocol instead of RESP2 so pub/sub messages share the same connection with Redis commands without interruption when tokens expire. 
    Protocol = RedisProtocol.Resp3,

    // Supply this sample app's logger factory so we can get logs from both Microsoft.Azure.StackExchangeRedis and StackExchange.Redis.
    LoggerFactory = loggerFactory,

    // Fail fast for the purposes of this sample. In production code, AbortOnConnectFail should remain false to retry connections on startup.
    AbortOnConnectFail = true,
};

// NOTE: ConnectionMultiplexer instances should be as long-lived as possible.
// A singleton ConnectionMultiplexer instance should be used for the lifetime of the client application process.
ConnectionMultiplexer? connection = null;

Console.WriteLine(@"
This sample shows how to connect to an Azure Redis cache using different types of Microsoft Entra ID authentication. For details see the README.md. 
Documentation on using Entra ID authentication with Azure Redis is available at https://aka.ms/redis/entra-auth.

Once the connection is established, Redis commands will execute every 1 second indefinitely, with dots written to the console to indicate success. 
Any connection disruption will result in exceptions logged to the console. 

If the connection cannot be established due to authentication failure or other issues, exceptions will be logged to the console and no commands will succeed. 
");

Console.Write("Redis cache host name: ");
var hostName = Console.ReadLine()?.Trim();
configurationOptions.EndPoints.Add(hostName!, GetSslPort(hostName!));

Console.WriteLine(@"
Select the type of authentication to use:
    1. DefaultAzureCredential
    2. User-assigned managed identity
    3. System-assigned managed identity
    4. Service principal secret
    5. Service principal certificate
    6. Service principal certificate with Subject Name + Issuer authentication (Microsoft internal use only)
    7. Access key (without Microsoft Entra ID)
    8. Exit
");
Console.Write("Enter a number: ");
var option = Console.ReadLine()?.Trim();

switch (option)
{
    case "1":// DefaultAzureCredential 
        Log("Connecting using DefaultAzureCredential...");

        // Acquire initial token and configure the connection to use it
        await configurationOptions.ConfigureForAzureWithTokenCredentialAsync(new DefaultAzureCredential());
        break;

    case "2": // User-Assigned managed identity
        Console.Write("Managed identity Client ID or resource ID: ");
        var managedIdentityId = Console.ReadLine()?.Trim();

        Log("Connecting with a user-assigned managed identity...");

        // Acquire initial token and configure the connection to use it
        await configurationOptions.ConfigureForAzureWithUserAssignedManagedIdentityAsync(managedIdentityId!);
        break;

    case "3": // System-Assigned managed identity
        Log("Connecting with a system-assigned managed identity...");

        // Acquire initial token and configure the connection to use it
        await configurationOptions.ConfigureForAzureWithSystemAssignedManagedIdentityAsync();
        break;

    case "4": // Service principal secret
        Console.Write("Service principal Application (client) ID: ");
        var clientId = Console.ReadLine()?.Trim();
        Console.Write("Service principal Tenant ID: ");
        var tenantId = Console.ReadLine()?.Trim();
        Console.Write("Service principal secret: ");
        var secret = Console.ReadLine()?.Trim();

        Log("Connecting with a service principal secret...");

        // Acquire initial token and configure the connection to use it
        await configurationOptions.ConfigureForAzureWithServicePrincipalAsync(clientId!, tenantId!, secret!);
        break;

    case "5": // Service principal certificate
        Console.Write("Service principal Application (client) ID: ");
        clientId = Console.ReadLine()?.Trim();
        Console.Write("Service principal Tenant ID: ");
        tenantId = Console.ReadLine()?.Trim();
        Console.Write("Path to certificate file: ");
        var certFilePath = Console.ReadLine()?.Trim();
        Console.Write("Certificate file password: ");
        var certPassword = Console.ReadLine()?.Trim();

        Log("Connecting with a service principal certificate...");

        // Acquire initial token and configure the connection to use it
        await configurationOptions.ConfigureForAzureWithServicePrincipalAsync(
            clientId!,
            tenantId!,
            certificate: new X509Certificate2(certFilePath!, certPassword, X509KeyStorageFlags.EphemeralKeySet)!);
        break;

    case "6": // Service principal certificate with Subject Name + Issuer authentication (Microsoft internal use only)
        Console.Write("Service principal Application (client) ID: ");
        clientId = Console.ReadLine()?.Trim();
        Console.Write("Service principal Tenant ID: ");
        tenantId = Console.ReadLine()?.Trim();
        Console.Write("Path to certificate file: ");
        certFilePath = Console.ReadLine()?.Trim();
        Console.Write("Certificate file password: ");
        certPassword = Console.ReadLine()?.Trim();

        Log("Connecting with a service principal certificate (with Subject Name + Issuer authentication)...");

        // Acquire initial token and configure the connection to use it
        await configurationOptions.ConfigureForAzureAsync(new AzureCacheOptions
        {
            ClientId = clientId!,
            ServicePrincipalTenantId = tenantId!,
            ServicePrincipalCertificate = new X509Certificate2(certFilePath!, certPassword, X509KeyStorageFlags.EphemeralKeySet),
            SendX5C = true // Enables Subject Name + Issuer authentication
        });
        break;

    case "7": // Access key (without Microsoft Entra ID)
        Console.Write("Access key: ");
        configurationOptions.Password = Console.ReadLine()?.Trim();

        Log("Connecting with an access key...");
        break;

    default:
        return;
}

SubscribeToTokenEvents(configurationOptions);
connection = await ConnectionMultiplexer.ConnectAsync(configurationOptions) ?? throw new Exception("Failed to initiate connection to Redis.");
SubscribeToConnectionEvents(connection);

var database = connection.GetDatabase();
var key = "sample";

Console.WriteLine();
Console.WriteLine("Esc to quit. Let the sample run longer than a token lifetime (1+ hours) to see that the connection is sustained despite expiration of the initial token, without any disruption or command failures.");

while (true)
{
    try
    {
        var value = await database.StringGetAsync(key);
    }
    catch (Exception ex)
    {
        LogError($"Failed to GET key '{key}': {ex}");
    }

    try
    {
        var value = $"Set at {DateTime.UtcNow:s}Z";
        await database.StringSetAsync(key, value);
    }
    catch (Exception ex)
    {
        LogError($"Failed to SET key '{key}': {ex}");
    }

    // Write a dot to the console to indicate that the sample has successfully executed Redis commands
    Console.Write(".");

    await Task.Delay(TimeSpan.FromSeconds(1));

    if (Console.KeyAvailable && Console.ReadKey(intercept: true).Key == ConsoleKey.Escape)
    {
        break;
    }
}

int GetSslPort(string hostName)
{
    switch (hostName[hostName.IndexOf('.')..].ToLowerInvariant())
    {
        case ".redis.cache.windows.net":
        case ".redis.cache.chinacloudapi.cn":
        case ".redis.cache.usgovcloudapi.net":
            return 6380;
        case ".redis.azure.net":
        case ".redis.chinacloudapi.cn":
        case ".redis.usgovcloudapi.net":
        case ".redis.sovcloud-api.fr":
        case ".redis.sovcloud-api.de":
        case ".redis.sovcloud-api.sg":
            return 10000;
        default:
            break;
    }

    Console.Write("Port: ");
    return Convert.ToInt32(Console.ReadLine()?.Trim());
}

void SubscribeToTokenEvents(ConfigurationOptions configurationOptions)
{
    if (configurationOptions.Defaults is IAzureCacheTokenEvents tokenEvents)
    {
        tokenEvents.TokenRefreshed += (sender, tokenResult) => Log($"{nameof(tokenEvents.TokenRefreshed)} event raised! New token will expire at {tokenResult.ExpiresOn:s}Z");
        tokenEvents.TokenRefreshFailed += (sender, args) => LogError($"{nameof(tokenEvents.TokenRefreshFailed)} event raised!. Current token will expire at {args.Expiry}: {args.Exception}");
        tokenEvents.ConnectionReauthenticated += (sender, endpoint) => Log($"{nameof(tokenEvents.ConnectionReauthenticated)} event raised! For endpoint '{endpoint}'");
        tokenEvents.ConnectionReauthenticationFailed += (sender, args) => LogError($"{nameof(tokenEvents.ConnectionReauthenticationFailed)} event raised! For endpoint '{args.Endpoint}': {args.Exception}");
    }
    else
    {
        Log($"{nameof(configurationOptions)} does not implement {nameof(IAzureCacheTokenEvents)}. No token events will be logged.");
    }
}

void SubscribeToConnectionEvents(ConnectionMultiplexer connectionMultiplexer)
{
    connectionMultiplexer.ConnectionFailed += (sender, args) => LogError($"{nameof(connectionMultiplexer.ConnectionFailed)} event raised: {args.Exception}");
    connectionMultiplexer.ConnectionRestored += (sender, args) => Log($"{nameof(connectionMultiplexer.ConnectionRestored)} event raised for endpoint '{args.EndPoint}'");
    connectionMultiplexer.ErrorMessage += (sender, args) => LogError($"{nameof(connectionMultiplexer.ErrorMessage)} event raised: {args.Message}");
    connectionMultiplexer.InternalError += (sender, args) => LogError($"{nameof(connectionMultiplexer.InternalError)} event raised: {args.Exception}");
}

void Log(string message)
    => consoleLogger.LogInformation($"{DateTime.UtcNow:s}Z: {message}");

void LogError(string message)
    => consoleLogger.LogError($"{DateTime.UtcNow:s}Z: {message}");

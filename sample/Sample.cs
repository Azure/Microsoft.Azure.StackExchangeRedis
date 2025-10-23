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
var log = loggerFactory.CreateLogger("Sample");

ConfigurationOptions configurationOptions = new()
{
    // Use the RESP3 protocol instead of RESP2 so pub/sub messages share the same connection with Redis commands without interruption when tokens expire. 
    Protocol = RedisProtocol.Resp3,

    // Supply this sample app's logger factory so we can get logs from both Microsoft.Azure.StackExchangeRedis and StackExchange.Redis.
    LoggerFactory = loggerFactory,

    // Fail fast for the purposes of this sample. In production code, AbortOnConnectFail should remain false to retry connections on startup.
    AbortOnConnectFail = true,

    // Fail commands immediately when a connection isn't available, rather than backlogging them for execution when connection is restored.
    // This option is useful for exposing any connection drops for the sample, but production code should always use BacklogPolicy.Default for resilience.
    BacklogPolicy = BacklogPolicy.FailFast,
};

// NOTE: ConnectionMultiplexer instances should be as long-lived as possible.
// A singleton ConnectionMultiplexer instance should be used for the lifetime of the client application process.
ConnectionMultiplexer? connection = null;

Console.WriteLine(@"
This sample shows how to connect to an Azure Redis cache using different types of Microsoft Entra ID authentication. For details see the README.md. 
Documentation on using Entra ID authentication with Azure Redis is available at https://aka.ms/redis/entra-auth.

Once the connection is established, Redis commands will execute every 1 second indefinitely, with '+' written to the console to indicate success. 
Any connection disruption will result in exceptions logged to the console. 

If the connection cannot be established due to authentication failure or other issues, exceptions will be logged to the console and no commands will succeed. 
");

Console.Write("Redis cache endpoint (hostname or hostname:port): ");
var endPoint = Console.ReadLine()?.Trim()!;
var hostNamePort = endPoint.Contains(':') ? endPoint : $"{endPoint}:{GetTlsPort(endPoint!)}";
configurationOptions.EndPoints.Add(hostNamePort);

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
    case "1": // DefaultAzureCredential 
        log.LogInformation("Connecting using DefaultAzureCredential...");

        // Acquire initial token and configure the connection to use it
        await configurationOptions.ConfigureForAzureWithTokenCredentialAsync(new DefaultAzureCredential());
        break;

    case "2": // User-Assigned managed identity
        Console.Write("Managed identity Client ID or resource ID: ");
        var managedIdentityId = Console.ReadLine()?.Trim();

        log.LogInformation("Connecting with a user-assigned managed identity...");

        // Acquire initial token and configure the connection to use it
        await configurationOptions.ConfigureForAzureWithUserAssignedManagedIdentityAsync(managedIdentityId!);
        break;

    case "3": // System-Assigned managed identity
        log.LogInformation("Connecting with a system-assigned managed identity...");

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

        log.LogInformation("Connecting with a service principal secret...");

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

        log.LogInformation("Connecting with a service principal certificate...");

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

        log.LogInformation("Connecting with a service principal certificate (with Subject Name + Issuer authentication)...");

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

        log.LogInformation("Connecting with an access key...");
        break;

    default:
        return;
}

try
{
    SubscribeToTokenEvents(configurationOptions);
    connection = await ConnectionMultiplexer.ConnectAsync(configurationOptions) ?? throw new Exception("Failed to initiate connection to Redis.");
    SubscribeToConnectionEvents(connection);

    var database = connection.GetDatabase();
    var key = "sample";

    Console.WriteLine();
    Console.WriteLine("Ctrl+C to quit. Let the sample run longer than a token lifetime (1+ hours) to see that the connection continues through the expiration of the initial token without any disruption or command failures.");

    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (sender, e) =>
    {
        e.Cancel = true;
        cts.Cancel();
    };

    while (!cts.Token.IsCancellationRequested)
    {
        try
        {
            var previousValue = await database.StringGetAsync(key);
            await database.StringSetAsync(key, $"Set at {DateTime.UtcNow:s}Z");

            // Write a plus to the console to indicate that the sample has successfully executed Redis commands
            Console.Write("+");
        }
        catch (Exception ex)
        {
            log.LogError($"Redis command failed: {ex}");
        }

        await Task.Delay(1000, cts.Token);
    }
    
    Console.Write(Environment.NewLine);

    return;
}
catch (TaskCanceledException)
{
    log.LogInformation("Stopping...");
}
catch (Exception ex)
{
    log.LogError($"Failed to connect to Redis: {ex}");
    return;
}
finally
{
    // Ensure the Redis connection is closed gracefully to prevent connection leaks
    connection?.Dispose();
    connection = null;
}

int GetTlsPort(string hostName)
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
    return int.TryParse(Console.ReadLine()?.Trim(), out var port) ? port : 10000;
}

void SubscribeToTokenEvents(ConfigurationOptions configurationOptions)
{
    if (configurationOptions.Defaults is IAzureCacheTokenEvents tokenEvents)
    {
        tokenEvents.TokenRefreshed += (sender, tokenResult) => log.LogInformation($"{nameof(tokenEvents.TokenRefreshed)} event raised! New token will expire at {tokenResult.ExpiresOn:s}Z");
        tokenEvents.TokenRefreshFailed += (sender, args) => log.LogError($"{nameof(tokenEvents.TokenRefreshFailed)} event raised!. Current token will expire at {args.Expiry}: {args.Exception}");
        tokenEvents.ConnectionReauthenticated += (sender, endpoint) => log.LogInformation($"{nameof(tokenEvents.ConnectionReauthenticated)} event raised! For endpoint '{endpoint}'");
        tokenEvents.ConnectionReauthenticationFailed += (sender, args) => log.LogError($"{nameof(tokenEvents.ConnectionReauthenticationFailed)} event raised! For endpoint '{args.Endpoint}': {args.Exception}");
    }
    else
    {
        log.LogInformation($"{nameof(configurationOptions)} does not implement {nameof(IAzureCacheTokenEvents)}. No token events will be logged.");
    }
}

void SubscribeToConnectionEvents(ConnectionMultiplexer connectionMultiplexer)
{
    connectionMultiplexer.ConnectionFailed += (sender, args) => log.LogError($"{nameof(connectionMultiplexer.ConnectionFailed)} event raised: {args.Exception}");
    connectionMultiplexer.ConnectionRestored += (sender, args) => log.LogInformation($"{nameof(connectionMultiplexer.ConnectionRestored)} event raised for endpoint '{args.EndPoint}'");
    connectionMultiplexer.ErrorMessage += (sender, args) => log.LogError($"{nameof(connectionMultiplexer.ErrorMessage)} event raised: {args.Message}");
    connectionMultiplexer.InternalError += (sender, args) => log.LogError($"{nameof(connectionMultiplexer.InternalError)} event raised: {args.Exception}");
}

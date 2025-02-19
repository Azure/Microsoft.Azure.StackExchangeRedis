﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Security.Cryptography.X509Certificates;
using Azure.Identity;
using Microsoft.Azure.StackExchangeRedis;
using StackExchange.Redis;
using static System.Console;

WriteLine(@"
This sample shows how to connect to an Azure Redis cache using various types of authentication including Microsoft Entra ID. 

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
Write("Enter a number: ");
var option = ReadLine()?.Trim();

// NOTE: ConnectionMultiplexer instances should be as long-lived as possible. Ideally a single ConnectionMultiplexer per cache is reused over the lifetime of the client application process.
ConnectionMultiplexer? connectionMultiplexer = null;
StringWriter connectionLog = new(); // Collects detailed connection logs from StackExchange.Redis

try
{
    switch (option)
    {
        case "1": // DefaultAzureCredential 
            Write("Redis cache host name: ");
            var cacheHostName = ReadLine()?.Trim();

            Write("Connecting using DefaultAzureCredential...");
            var configurationOptions = await ConfigurationOptions.Parse($"{cacheHostName}:6380").ConfigureForAzureWithTokenCredentialAsync(new DefaultAzureCredential());
            configurationOptions.AbortOnConnectFail = true; // Fail fast for the purposes of this sample. In production code, this should remain false to retry connections on startup
            LogTokenEvents(configurationOptions);

            connectionMultiplexer = await ConnectionMultiplexer.ConnectAsync(configurationOptions, connectionLog);
            break;

        case "2": // User-Assigned managed identity
            Write("Redis cache host name: ");
            cacheHostName = ReadLine()?.Trim();
            Write("Managed identity Client ID or resource ID: ");
            var managedIdentityId = ReadLine()?.Trim();

            WriteLine("Connecting with a user-assigned managed identity...");
            configurationOptions = await ConfigurationOptions.Parse($"{cacheHostName}:6380").ConfigureForAzureWithUserAssignedManagedIdentityAsync(managedIdentityId!);
            configurationOptions.AbortOnConnectFail = true; // Fail fast for the purposes of this sample. In production code, this should remain false to retry connections on startup
            LogTokenEvents(configurationOptions);

            connectionMultiplexer = await ConnectionMultiplexer.ConnectAsync(configurationOptions, connectionLog);
            break;

        case "3": // System-Assigned managed identity
            Write("Redis cache host name: ");
            cacheHostName = ReadLine()?.Trim();

            WriteLine("Connecting with a system-assigned managed identity...");
            configurationOptions = await ConfigurationOptions.Parse($"{cacheHostName}:6380").ConfigureForAzureWithSystemAssignedManagedIdentityAsync();
            configurationOptions.AbortOnConnectFail = true; // Fail fast for the purposes of this sample. In production code, this should remain false to retry connections on startup
            LogTokenEvents(configurationOptions);

            connectionMultiplexer = await ConnectionMultiplexer.ConnectAsync(configurationOptions, connectionLog);
            break;

        case "4": // Service principal secret
            Write("Redis cache host name: ");
            cacheHostName = ReadLine()?.Trim();
            Write("Service principal Application (client) ID: ");
            var clientId = ReadLine()?.Trim();
            Write("Service principal Tenant ID: ");
            var tenantId = ReadLine()?.Trim();
            Write("Service principal secret: ");
            var secret = ReadLine()?.Trim();

            WriteLine("Connecting with a service principal secret...");
            configurationOptions = await ConfigurationOptions.Parse($"{cacheHostName}:6380").ConfigureForAzureWithServicePrincipalAsync(clientId!, tenantId!, secret!);
            configurationOptions.AbortOnConnectFail = true; // Fail fast for the purposes of this sample. In production code, this should remain false to retry connections on startup
            LogTokenEvents(configurationOptions);

            connectionMultiplexer = await ConnectionMultiplexer.ConnectAsync(configurationOptions, connectionLog);
            break;

        case "5": // Service principal certificate
            Write("Redis cache host name: ");
            cacheHostName = ReadLine()?.Trim();
            Write("Service principal Application (client) ID: ");
            clientId = ReadLine()?.Trim();
            Write("Service principal Tenant ID: ");
            tenantId = ReadLine()?.Trim();
            Write("Path to certificate file: ");
            var certFilePath = ReadLine()?.Trim();
            Write("Certificate file password: ");
            var certPassword = ReadLine()?.Trim();

            WriteLine("Loading certificate...");
            var certificate = new X509Certificate2(certFilePath!, certPassword, X509KeyStorageFlags.EphemeralKeySet);

            WriteLine("Connecting with a service principal certificate...");
            configurationOptions = await ConfigurationOptions.Parse($"{cacheHostName}:6380").ConfigureForAzureWithServicePrincipalAsync(clientId!, tenantId!, certificate: certificate!);
            configurationOptions.AbortOnConnectFail = true; // Fail fast for the purposes of this sample. In production code, this should remain false to retry connections on startup
            LogTokenEvents(configurationOptions);

            connectionMultiplexer = await ConnectionMultiplexer.ConnectAsync(configurationOptions, connectionLog);
            break;

        case "6": // Service principal certificate with Subject Name + Issuer authentication (Microsoft internal use only)
            Write("Redis cache host name: ");
            cacheHostName = ReadLine()?.Trim();
            Write("Service principal Application (client) ID: ");
            clientId = ReadLine()?.Trim();
            Write("Service principal Tenant ID: ");
            tenantId = ReadLine()?.Trim();
            Write("Path to certificate file: ");
            certFilePath = ReadLine()?.Trim();
            Write("Certificate file password: ");
            certPassword = ReadLine()?.Trim();

            WriteLine("Loading certificate...");
            certificate = new X509Certificate2(certFilePath!, certPassword, X509KeyStorageFlags.EphemeralKeySet);

            WriteLine("Connecting with a service principal certificate (with Subject Name + Issuer authentication)...");
            var azureCacheOptions = new AzureCacheOptions
            {
                ClientId = clientId!,
                ServicePrincipalTenantId = tenantId!,
                ServicePrincipalCertificate = certificate,
                SendX5C = true // Enables Subject Name + Issuer authentication
            };
            configurationOptions = await ConfigurationOptions.Parse($"{cacheHostName}:6380").ConfigureForAzureAsync(azureCacheOptions);
            configurationOptions.AbortOnConnectFail = true; // Fail fast for the purposes of this sample. In production code, this should remain false to retry connections on startup
            LogTokenEvents(configurationOptions);

            connectionMultiplexer = await ConnectionMultiplexer.ConnectAsync(configurationOptions, connectionLog);
            break;

        case "7": // Access key (without Microsoft Entra ID)
            Write("Redis cache connection string: ");
            var connectionString = ReadLine()?.Trim();

            WriteLine("Connecting with an access key...");
            connectionMultiplexer = await ConnectionMultiplexer.ConnectAsync(connectionString!, AzureCacheForRedis.ConfigureForAzure, connectionLog);
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

LogConnectionEvents(connectionMultiplexer);

// This loop will execute commands on the Redis cache every 2 minutes indefinitely. 
// Let it run for longer than a token lifespan (1-24 hours depending on Entra tenant configuration)
// to see how the connection remains functional even after the initial token has expired. 
var database = connectionMultiplexer?.GetDatabase();
while (true)
{
    // Read and write a key every 2 minutes and output a '+' to show that the connection is working
    try
    {
        // NOTE: Always use the *Async() versions of StackExchange.Redis methods if possible (e.g. StringSetAsync(), StringGetAsync())
        var value = await database!.StringGetAsync("key");
        await database.StringSetAsync("key", DateTime.UtcNow.ToString());
        Log($"Success! Previous value: {value}");
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
        tokenEvents.TokenRefreshed += (sender, tokenResult) => Log($"Token refreshed. New token will expire at {tokenResult.ExpiresOn:s}");
        tokenEvents.TokenRefreshFailed += (sender, args) => Log($"Token refresh failed for token expiring at {args.Expiry}: {args.Exception}");
        tokenEvents.ConnectionReauthenticated += (sender, endpoint) => Log($"Re-authenticated connection to '{endpoint}'");
        tokenEvents.ConnectionReauthenticationFailed += (sender, args) => Log($"Re-authentication of connection to '{args.Endpoint}' failed: {args.Exception}");
    }
}

static void LogConnectionEvents(ConnectionMultiplexer connectionMultiplexer)
{
    connectionMultiplexer.ConnectionFailed += (sender, args) => Log($"Connection failed: {args.Exception}");
    connectionMultiplexer.ConnectionRestored += (sender, args) => Log($"Connection restored to '{args.EndPoint}'");
    connectionMultiplexer.ErrorMessage += (sender, args) => Log($"Error: {args.Message}");
    connectionMultiplexer.InternalError += (sender, args) => Log($"Internal error: {args.Exception}");
}

static void Log(string message)
    => WriteLine($"{DateTime.Now:s}: {message}");

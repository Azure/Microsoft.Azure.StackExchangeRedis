---
ArtifactType: nupkg
Documentation: https://learn.microsoft.com/azure/azure-cache-for-redis
Language: C#
Tags: Redis,Cache,StackExchange.Redis,Microsoft,Azure
---

# Microsoft.Azure.StackExchangeRedis Extension
The Microsoft.Azure.StackExchangeRedis package is an extension for the StackExchange.Redis client library that enables using Microsoft Entra ID to authenticate connections from a Redis client application to an Azure Cache for Redis resource. This extension acquires an access token for an Azure [managed identity](https://learn.microsoft.com/azure/active-directory/managed-identities-azure-resources/overview), [service principal](https://learn.microsoft.com/azure/active-directory/develop/app-objects-and-service-principals), or user and configures a StackExchange.Redis connection to use the token for authentication. Before the token expires, it acquires a fresh token and re-authenticates the connection to maintain uninterrupted communication with the cache indefinitely. 

### Subscription connections
With the default RESP2 protocol, StackExchange.Redis actually creates two connections behind the scenes: an "interactive" connection for normal Redis commands (GET, SET, etc.), plus a "subscription" connection for [pub/sub](https://redis.io/glossary/pub-sub/) messages. The interactive connection is the one that's proactively re-authenticated with fresh tokens, while the subscription connection cannot be re-authenticated. When using RESP2 you will see subscription connections being closed by the Redis server when their token expires, and then immediately restored by StackExchange.Redis using a current token. To avoid these interruptions, we recommend using the RESP3 protocol which bundles all traffic (interactive and pub/sub) on a single connection to Redis, which will be proactively re-authenticated. Opt in to RESP3 by specifying `protocol=resp3` in the connection string, or by setting `configurationOptions.Protocol = RedisProtocol.Resp3` as shown in [sample/Sample.cs](./sample/Sample.cs). If RESP2 is used, it's expected to see "MicrosoftEntraTokenExpired" in Redis error metrics due to the subscription connections.

## Usage
See [sample/Sample.cs](./sample/Sample.cs) for detailed examples of how to use the extension for all supported authentication scenarios.

High level instructions:

1. Add a reference to the [Microsoft.Azure.StackExchangeRedis](https://www.nuget.org/packages/Microsoft.Azure.StackExchangeRedis) NuGet package in your Redis client project.

2. In your Redis connection code, first create a `ConfigurationOptions` instance. You can use the `.Parse()` method to create an instance from a Redis connection string or the cache host name alone.

```csharp
var configurationOptions = ConfigurationOptions.Parse($"{cacheHostName}:6380");
```

3. Use one of the _ConfigureForAzure*_ extension methods supplied by this package to configure the authentication options:

```csharp
// DefaultAzureCredential
await configurationOptions.ConfigureForAzureWithTokenCredentialAsync(new DefaultAzureCredential());

// User-assigned managed identity
await configurationOptions.ConfigureForAzureWithUserAssignedManagedIdentityAsync(managedIdentityClientId);

// System-assigned managed identity
await configurationOptions.ConfigureForAzureWithSystemAssignedManagedIdentityAsync();

// Service principal secret
await configurationOptions.ConfigureForAzureWithServicePrincipalAsync(clientId, tenantId, secret);

// Service principal certificate
await configurationOptions.ConfigureForAzureWithServicePrincipalAsync(clientId, tenantId, certificate);

// Service principal certificate with Subject Name + Issuer (SNI) authentication (Microsoft internal use only)
await configurationOptions.ConfigureForAzureAsync(new AzureCacheOptions
{
    ClientId = clientId,
    ServicePrincipalTenantId = tenantId,
    ServicePrincipalCertificate = certificate,
    SendX5C = true // Enables Subject Name + Issuer authentication
});
```

4. Create the connection, passing in the `ConfigurationOptions` instance

```csharp
var connectionMultiplexer = await ConnectionMultiplexer.ConnectAsync(configurationOptions);
```

5. Use the `connectionMultiplexer` to interact with  Redis as you normally would. 

## Running the sample
The [sample](./sample) directory contains a project showing how to connect to an Azure Redis cache using the various authentication mechanisms supported by this extension. Borrow code from this sample for your own project, or simply run it to test the authentication configuration on your cache. It will prompt you for the type of authentication to use and then the necessary credentials. To run the sample: 
1. [Create an Azure Cache for Redis resource](https://learn.microsoft.com/azure/azure-cache-for-redis/quickstart-create-redis)
1. Configure AAD authentication on your cache using the instructions in [Use Microsoft Entra ID for cache authentication](https://learn.microsoft.com/azure/azure-cache-for-redis/cache-azure-active-directory-for-authentication)
    - Optional: if you're using `DefaultAzureCredential` authentication, ensure that either an Azure user is signed on the machine where you're running your code, or environment variables have been set to supply Azure credentials. For details see: [How to authenticate .NET apps to Azure services using the .NET Azure SDK](https://learn.microsoft.com/dotnet/azure/sdk/authentication/). 
1. `dotnet run <path to Microsoft.Azure.StackExchangeRedis.Sample.csproj>`, or run the project in Visual Studio or your favorite IDE
1. Follow the prompts to enter your credentials and test the connection to the cache
1. To see how the connection is maintained by periodically re-authenticating with fresh tokens, let the sample run for longer than a token lifespan (1+ hours).

NOTE: The sample project uses a `<ProjectReference>` to the extension project in this repo. To run the project on its own using the released Microsoft.Azure.StackExchangeRedis NuGet package, replace the `<ProjectReference>` in `Microsoft.Azure.StackExchangeRedis.Sample.csproj` with a `<PackageReference>`.

## Contributing
Please read our [CONTRIBUTING.md](CONTRIBUTING.md) which outlines all of our policies, procedures, and requirements for contributing to this project.

## Versioning
We use [SemVer](https://semver.org/) for versioning. For the versions available, see the [releases](https://github.com/Azure/Microsoft.Azure.StackExchangeRedis/releases).

## License
This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

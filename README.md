---
ArtifactType: nupkg
Documentation: https://learn.microsoft.com/azure/azure-cache-for-redis
Language: C#
Tags: Redis,Cache,StackExchange.Redis,Microsoft,Azure
---

# Microsoft.Azure.StackExchangeRedis Extension
The Microsoft.Azure.StackExchangeRedis package is an extension for the StackExchange.Redis client library that enables using Azure Active Directory to authenticate connections from a Redis client application to an Azure Cache for Redis resource. This extension acquires an authentication token for a [Managed Identity](https://learn.microsoft.com/en-us/azure/active-directory/managed-identities-azure-resources/overview) or [Service Principal](https://learn.microsoft.com/en-us/azure/active-directory/develop/app-objects-and-service-principals) and configures a StackExchange.Redis connection to use the token for authentication. It also maintains the token, proactively refreshing it and re-authenticating the connection to maintain uninterrupted communication with the cache over multiple days.

## Usage
1. Add a reference to the [Microsoft.Azure.StackExchangeRedis](https://www.nuget.org/packages/Microsoft.Azure.StackExchangeRedis) NuGet package in your Redis client project

2. In your Redis connection code, create a `ConfigurationOptions` instance
```csharp
var configurationOptions = ConfigurationOptions.Parse($"{cacheHostName}:6380");
```

3. Use one of the extension's methods to configure the options for Azure:
```csharp
// With a system-assigned managed identity
await configurationOptions.ConfigureForAzureWithSystemAssignedManagedIdentityAsync(principalId);

// With a user-assigned managed identity
await configurationOptions.ConfigureForAzureWithUserAssignedManagedIdentityAsync(managedIdentityClientId, principalId);

// With a service principal
await configurationOptions.ConfigureForAzureWithServicePrincipalAsync(clientId, principalId, tenantId, secret);
```

4. Create the connection, passing in the ConfigurationOptions instance
```csharp
var connectionMultiplexer = await ConnectionMultiplexer.ConnectAsync(configurationOptions);
```

## Running the sample
The `sample` directory contains a project showing how to connect to a Redis cache using the various authentication mechanisms supported by this extension. You can borrow code from this sample for your own project, or simply run it to test the authentication configuration on your cache. It will prompt you for the type of authentication to use, and the specific credentials needed. To run the sample: 
1. [Create an Azure Cache for Redis resource](https://learn.microsoft.com/azure/azure-cache-for-redis/quickstart-create-redis)
1. Configure AAD authentication on your cache using the instructions in [Use Azure Active Directory for cache authentication](https://learn.microsoft.com/azure/azure-cache-for-redis/cache-azure-active-directory-for-authentication)
1. `dotnet run <path to Microsoft.Azure.StackExchangeRedis.Sample.csproj>`, or run the project in Visual Studio or your favorite IDE
1. Follow the prompts to enter your credentials and test the connection to the cache

NOTE: The sample project uses a `<ProjectReference>` to the extension project in this repo. To run the project on its own using the released Microsoft.Azure.StackExchangeRedis NuGet package, replace the `<ProjectReference>` in `Microsoft.Azure.StackExchangeRedis.Sample.csproj` with a `<PackageReference>`.

## Contributing
Please read our [CONTRIBUTING.md](CONTRIBUTING.md) which outlines all of our policies, procedures, and requirements for contributing to this project.

## Versioning
We use [SemVer](https://semver.org/) for versioning. For the versions available, see the [releases](https://github.com/Azure/Microsoft.Azure.StackExchangeRedis/releases).

## License
This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

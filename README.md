---
ArtifactType: nupkg
Documentation: https://learn.microsoft.com/azure/azure-cache-for-redis
Language: C#
Tags: Redis,Cache,StackExchange.Redis,Microsoft,Azure
---

# Microsoft.Azure.StackExchangeRedis Extension
Microsoft.Azure.StackExchangeRedis is an extension of StackExchange.Redis that enables using Azure Active Directory to authenticate connections from a Redis client application to an Azure Cache for Redis. The extension manages the authentication token, including proactively refreshing tokens before they expire to maintain persistent Redis connections over multiple  days.

## Usage
1. Add a reference to the [Microsoft.Azure.StackExchangeRedis NuGet package](https://www.nuget.org/packages/Microsoft.Azure.StackExchangeRedis) in your Redis client project

2. In your Redis connection code, create a `ConfigurationOptions` instance
```csharp
var configurationOptions = await ConfigurationOptions.Parse($"{cacheHostName}:6380");
```

3. Use one of the extension's methods to configure it for Azure:
```csharp
// With a system-assigned managed identity
await configurationOptions.ConfigureForAzureWithSystemAssignedManagedIdentityAsync(principalId);

// With a user-assigned managed identity
await configurationOptions.ConfigureForAzureWithUserAssignedManagedIdentityAsync(managedIdentityClientId, principalId);

// With a service principal
await configurationOptions.ConfigureForAzureWithServicePrincipalAsync(clientId, principalId, tenantId, secret);
```

4. Create the a connection by creating and passing in the ConfigurationOptions to the ConnectionMultiplexer.ConenctAsync
```csharp
var connectionMultiplexer = await ConnectionMultiplexer.ConnectAsync(configurationOptions);
```

## Running the sample
The `sample` directory contains a project showing how to connect to a Redis cache using the various authentication mechanisms supported by this extension. To run the sample: 
1. [Create an Azure Cache for Redis resource](https://learn.microsoft.com/azure/azure-cache-for-redis/quickstart-create-redis)
1. Create a [managed identity](https://learn.microsoft.com/azure/active-directory/managed-identities-azure-resources/qs-configure-cli-windows-vm#code-try-10) or [service principal](https://learn.microsoft.com/azure/active-directory/develop/howto-create-service-principal-portal)
1. `dotnet run <path to Microsoft.Azure.StackExchangeRedis.Sample.csproj>`
1. Follow the prompts to enter your credentials and test the connection to the cache

NOTE: The sample project uses a `<ProjectReference>` to the extension project in this repo. To run the project on its own using the released Microsoft.Azure.StackExchangeRedis NuGet package, replace the `<ProjectReference>` in `Microsoft.Azure.StackExchangeRedis.Sample.csproj` with a `<PackageReference>`.

## Contributing
Please read our [CONTRIBUTING.md](CONTRIBUTING.md) which outlines all of our policies, procedures, and requirements for contributing to this project.

## Versioning
We use [SemVer](https://semver.org/) for versioning. For the versions available, see the [releases](https://github.com/Azure/Microsoft.Azure.StackExchangeRedis/releases).

## License
This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.
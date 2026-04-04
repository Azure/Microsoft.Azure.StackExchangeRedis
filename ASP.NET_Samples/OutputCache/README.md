# ASP.NET Core Redis Output Cache Sample Application

This sample demonstrates ASP.NET Core output caching backed by an Azure Redis resource.

## Prerequisites

- .NET 10 SDK
- An Azure Redis resource

## Configure Azure Redis Connection

Use user-secrets so credentials are not stored in source control:

```bash
dotnet user-secrets init
dotnet user-secrets set RedisConnectionString "<redis-name>.redis.cache.windows.net:6380,password=<access-key>,ssl=True,abortConnect=False"
```

You can also set `RedisConnectionString` as an environment variable.

## Restore, Build, Run

```bash
dotnet restore ./RedisOutputCache.csproj
dotnet build ./RedisOutputCache.csproj
dotnet run --project ./RedisOutputCache.csproj
```

## Endpoints

- `/cached` - Cached with default 10-second policy
- `/api/expensive` - Cached with named 30-second policy

Call each endpoint repeatedly within its cache window to see the timestamp stay the same.

## Notes

- `appsettings.Development.json` includes an Azure Redis connection string template.
- For production, keep secrets out of config files and use secure secret storage.

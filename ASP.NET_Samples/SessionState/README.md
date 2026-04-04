# ASP.NET Core Redis Session State Sample Application

This sample demonstrates ASP.NET Core session state backed by Redis distributed caching.

It mirrors the structure of the OutputCache sample, but uses session middleware and `IDistributedCache` via Redis.

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
dotnet restore ./RedisSessionCache.csproj
dotnet build ./RedisSessionCache.csproj
dotnet run --project ./RedisSessionCache.csproj
```

## Endpoints

- `GET /` - Increments and returns a per-session request counter and current session values
- `POST /session/username/{name}` - Sets a `Username` session value
- `POST /session/clear` - Clears the current session

Use the same client/cookie jar to observe persistent session values across requests.

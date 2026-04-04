# Setup Instructions for Redis Output Cache Sample App

This project is configured to run against an Azure Redis resource.

## Prerequisites

- .NET 10 SDK
- Azure Redis resource

## 1) Configure connection string

From Azure portal, copy the primary connection string details (host + key), then set:

```bash
dotnet user-secrets init
dotnet user-secrets set RedisConnectionString "<redis-name>.redis.cache.windows.net:6380,password=<access-key>,ssl=True,abortConnect=False"
```

## 2) Restore, build, run

```bash
dotnet restore ./RedisOutputCache.csproj
dotnet build ./RedisOutputCache.csproj
dotnet run --project ./RedisOutputCache.csproj
```

## 3) Verify caching behavior

```bash
curl http://localhost:5000/cached
curl http://localhost:5000/cached

curl http://localhost:5000/api/expensive
curl http://localhost:5000/api/expensive
```

Within each policy window, timestamps should remain the same.

## Troubleshooting

### Unable to connect to Redis
- Confirm the host, port, and key are correct.
- Confirm TLS is enabled (`ssl=True`).
- Confirm your network/firewall allows outbound access to Azure Redis.

### Check configured secret

```bash
dotnet user-secrets list
```

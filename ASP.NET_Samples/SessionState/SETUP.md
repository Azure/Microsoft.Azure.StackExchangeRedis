# Setup Instructions for Redis Session State Sample App

This project is configured to run against an Azure Redis resource and uses Redis for ASP.NET Core session state.

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
dotnet restore ./RedisSessionCache.csproj
dotnet build ./RedisSessionCache.csproj
dotnet run --project ./RedisSessionCache.csproj
```

## 3) Verify session behavior

```bash
curl -c cookies.txt http://localhost:5000/
curl -b cookies.txt -c cookies.txt http://localhost:5000/
curl -b cookies.txt -c cookies.txt -X POST http://localhost:5000/session/username/Ada
curl -b cookies.txt -c cookies.txt http://localhost:5000/
```

The `RequestCount` should increment for the same cookie/session and `Username` should persist.

## Troubleshooting

### Unable to connect to Redis
- Confirm the host, port, and key are correct.
- Confirm TLS is enabled (`ssl=True`).
- Confirm your network/firewall allows outbound access to Azure Redis.

### Check configured secret

```bash
dotnet user-secrets list
```

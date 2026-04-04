using Azure.Identity;
using Microsoft.Azure.StackExchangeRedis;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

var redisConnectionString = builder.Configuration["RedisConnectionString"];
if (string.IsNullOrWhiteSpace(redisConnectionString))
{
    throw new InvalidOperationException("RedisConnectionString is not configured.");
}

var redisConfig = ConfigurationOptions.Parse(redisConnectionString);

// Hook up StackExchange.Redis logging
var redisLoggerFactory = LoggerFactory.Create(logging =>
{
    logging.AddConfiguration(builder.Configuration.GetSection("Logging"));
    logging.AddConsole();
});
redisConfig.LoggerFactory = redisLoggerFactory;

// Configure Redis authentication using Microsoft Entra ID
await redisConfig.ConfigureForAzureWithTokenCredentialAsync(new DefaultAzureCredential());

// Create Redis connection
var redis = await ConnectionMultiplexer.ConnectAsync(redisConfig);

// Use Redis as the distributed cache backing store for Session
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.InstanceName = "SessionCacheSample:";
    options.ConnectionMultiplexerFactory = () => Task.FromResult<IConnectionMultiplexer>(redis);
});

builder.Services.AddSession(options =>
{
    options.Cookie.Name = ".RedisSessionCache.Session";
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.IdleTimeout = TimeSpan.FromMinutes(20);
});

var app = builder.Build();

app.Lifetime.ApplicationStopping.Register(() =>
{
    redis.Dispose();
    redisLoggerFactory.Dispose();
});

app.UseSession();

app.MapGet("/", (HttpContext context) =>
{
    var count = context.Session.GetInt32("Count") ?? 0;
    count++;
    context.Session.SetInt32("Count", count);

    var username = context.Session.GetString("Username") ?? "(not set)";

    return Results.Ok(new
    {
        Message = "Session state stored in Redis distributed cache",
        SessionId = context.Session.Id,
        RequestCount = count,
        Username = username,
        ServerTime = DateTime.UtcNow
    });
});

app.MapPost("/session/username/{name}", (HttpContext context, string name) =>
{
    context.Session.SetString("Username", name);
    return Results.Ok(new { Message = "Username stored in session", Username = name, SessionId = context.Session.Id });
});

app.MapPost("/session/clear", (HttpContext context) =>
{
    context.Session.Clear();
    return Results.Ok(new { Message = "Session cleared", SessionId = context.Session.Id });
});

app.Run();

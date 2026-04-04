using Azure.Identity;
using Microsoft.Azure.StackExchangeRedis;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

var redisConfig = ConfigurationOptions.Parse(builder.Configuration["RedisConnectionString"]);

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

// Add Redis Output Cache Middleware service
builder.Services.AddStackExchangeRedisOutputCache(options =>
{
    // Always use the singleton Redis connection multiplexer
    options.ConnectionMultiplexerFactory = () => Task.FromResult<IConnectionMultiplexer>(redis);
});

// Add output cache policies
builder.Services.AddOutputCache(options => {
    // Define a default cache policy of 10 seconds
    options.AddBasePolicy(builder => builder.Expire(TimeSpan.FromSeconds(10)));
    
    // Define a named cache policy for the /api/expensive endpoint (30 seconds)
    options.AddPolicy("expensive", builder => builder.Expire(TimeSpan.FromSeconds(30)));
});

var app = builder.Build();

app.Lifetime.ApplicationStopping.Register(() =>
{
    redis.Dispose();
    redisLoggerFactory.Dispose();
});

// Use Redis Output Caching Middleware service
app.UseOutputCache();

// Endpoint 1: Cached endpoint with timestamp (uses default 10-second cache)
app.MapGet("/cached", () => 
{
    var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
    return new { Message = "Cached Response", Timestamp = timestamp };
})
.CacheOutput()
.WithName("GetCached");

// Endpoint 2: API endpoint with named cache policy (30 seconds)
app.MapGet("/api/expensive", () => 
{
    var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
    return new { 
        Message = "This is an expensive operation result (cached for 30 seconds)", 
        Timestamp = timestamp,
        OperationDuration = "500ms"
    };
})
.CacheOutput("expensive")
.WithName("GetExpensiveData");

app.Run();

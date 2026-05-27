using Microsoft.Azure.StackExchangeRedis.Sample.AspNet.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Register Redis singleton
builder.Services.AddSingleton<Redis>();

var app = builder.Build();

// Initialize Redis connection
var redis = app.Services.GetRequiredService<Redis>();

var redisEndpoint = app.Configuration.GetValue<string>("Redis:Endpoint");
if (string.IsNullOrWhiteSpace(redisEndpoint))
{
    throw new InvalidOperationException("Redis endpoint must be provided via configuration (Redis:Endpoint) in the format 'name.region.redis.azure.net:10000'.");
}

try
{
    await redis.ConnectAsync(redisEndpoint);
}
catch (Exception ex)
{
    Console.WriteLine($"Failed to connect to Redis: {ex.Message}");
    throw;
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();

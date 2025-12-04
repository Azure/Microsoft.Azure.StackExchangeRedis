using Microsoft.Azure.StackExchangeRedis.Sample.AspNet.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Register Redis singleton
builder.Services.AddSingleton<Redis>();

var app = builder.Build();

// Initialize Redis connection
using (var scope = app.Services.CreateScope())
{
    var redis = scope.ServiceProvider.GetRequiredService<Redis>();
    var endpoint = app.Configuration.GetValue<string>("Redis:Endpoint") ?? string.Empty; // Value should be in the format "name.region.redis.azure.net:10000"
    await redis.ConnectAsync(endpoint);
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

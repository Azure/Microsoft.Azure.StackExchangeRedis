using Azure.Identity;
using StackExchange.Redis;

namespace Microsoft.Azure.StackExchangeRedis.Sample.AspNet.Services;

public class Redis : IDisposable
{
    private readonly ILoggerFactory _loggerFactory;
    private ConnectionMultiplexer? _connection;
    private IDatabase? _db;
    private bool _disposed = false;

    public Redis(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
    }

    public IDatabase Db => _db ?? throw new InvalidOperationException($"Redis connection not initialized. Call {nameof(ConnectAsync)}() first.");

    public async Task ConnectAsync(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            throw new ArgumentException("Redis endpoint must be provided via configuration (Redis:Endpoint).", nameof(endpoint));
        }

        var options = new ConfigurationOptions()
        {
            EndPoints = { endpoint },
            LoggerFactory = _loggerFactory,
        };

        await options.ConfigureForAzureWithTokenCredentialAsync(new DefaultAzureCredential());

        _connection = await ConnectionMultiplexer.ConnectAsync(options);
        _db = _connection.GetDatabase();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_connection != null)
        {
            _connection.Dispose();
            _connection = null;
        }
        _db = null;
        GC.SuppressFinalize(this);
    }
}

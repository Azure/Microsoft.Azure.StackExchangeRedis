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

    private bool _isConnected = false;
    private int _connectionInProgress = 0;

    public async Task ConnectAsync(string? endpoint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);

        if (_isConnected || 1 == Interlocked.Exchange(ref _connectionInProgress, 1))
        {
            // Already connected or a connection is in progress
            return;
        }

        try
        {
            var options = new ConfigurationOptions()
            {
                EndPoints = { endpoint },
                LoggerFactory = _loggerFactory,
            };

            await options.ConfigureForAzureWithTokenCredentialAsync(new DefaultAzureCredential());

            _connection = await ConnectionMultiplexer.ConnectAsync(options);
            _db = _connection.GetDatabase();
            _isConnected = true;
        }
        finally
        {
            // Release the lock
            Interlocked.Exchange(ref _connectionInProgress, 0);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _isConnected = false;
        _connection?.Dispose();
        _connection = null;
        _db = null;

        GC.SuppressFinalize(this);
    }
}

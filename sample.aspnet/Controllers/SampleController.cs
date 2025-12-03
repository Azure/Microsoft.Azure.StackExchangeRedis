using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.StackExchangeRedis.Sample.AspNet.Services;

namespace Microsoft.Azure.StackExchangeRedis.Sample.AspNet.Controllers;

[ApiController]
[Route("[controller]")]
public class SampleController : ControllerBase
{
    private readonly Redis _redis;
    private readonly ILogger<SampleController> _logger;

    public SampleController(Redis redis, ILogger<SampleController> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    [HttpGet(Name = "Get")]
    public string Get()
    {
        // Read current value from Redis
        var previousVisitTime = _redis.Db.StringGet("PreviousVisitTime");

        // Update value in Redis
        _redis.Db.StringSet("PreviousVisitTime", DateTimeOffset.UtcNow.ToString("s"));

        _logger.LogInformation("Handled GET request. Previous visit time: {PreviousVisitTime}", previousVisitTime);

        return $"Previous visit was at: {previousVisitTime}";
    }
}

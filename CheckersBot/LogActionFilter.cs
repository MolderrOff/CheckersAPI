using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Text.Json;

namespace CheckersBot;

public class LogActionFilter : IAsyncResultFilter
{
    private readonly ILogger<LogActionFilter> _logger;

    public LogActionFilter(ILogger<LogActionFilter> logger) => _logger = logger;

    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {        
        var resultContext = await next();

        if (resultContext.Result is OkObjectResult okResult && okResult.Value is EngineResponse engineResponse)
        {
            var logEntry = new RequestLog
            {
                RequestId = context.HttpContext.TraceIdentifier, 
                TimeMs = engineResponse.Info.TimeMs,
                Depth = engineResponse.Depth,
                Nodes = engineResponse.Nodes,
                TablebaseHit = engineResponse.Info.TablebaseHit,
                PositionKey = engineResponse.PositionKey
            };

            
            _logger.LogInformation("{@RequestLog}", logEntry);
        }
    }
}
using CheckersBot.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Server.HttpSys;
using Microsoft.Extensions.Caching.Memory;

namespace CheckersBot.Controllers;

[Route("api/[controller]")]
[ApiController]
[ServiceFilter(typeof(LogActionFilter))]
public class CheckersController : ControllerBase
{
    private EnginePoolService _pool;
    private IMemoryCache _cache;    
    public CheckersController(EnginePoolService pool, IMemoryCache cache)
    {
        _pool = pool;
        _cache = cache;
    }  
    //POST /v1/move/suggest
    [HttpPost("suggest")]
    public async Task<IActionResult> SuggestMove([FromBody] SuggestRequest request)
    {
        if (request?.State?.Position == null || string.IsNullOrEmpty(request.State.Position))
            return UnprocessableEntity(new { error = "Invalid PDN" }); // 422

        if (!ModelState.IsValid) return BadRequest(ModelState);

        string cacheKey = $"suggest_{request.State.Position}_{request.Level}_{request.Limits.MaxDepth}";
        if (_cache.TryGetValue(cacheKey, out EngineResponse cachedResponse))
        {
            return Ok(cachedResponse);
        }

        try
        {
            var worker = _pool.GetNextWorker();
            var adapter = new ChinookAdapter(worker);

            using var cts = new CancellationTokenSource(request.Limits.HardTimeMs);

            var result = await adapter.SearchAsync(request, cts.Token);

            var cacheOptions = new MemoryCacheEntryOptions()
                .SetAbsoluteExpiration(TimeSpan.FromMinutes(15))
                .SetSize(1);
            _cache.Set(cacheKey, result, cacheOptions);

            return Ok(result);
        }
        catch (OperationCanceledException)
        {
            return StatusCode(504, "Engine timeout");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error: {ex.Message}");
            return StatusCode(500, ex.Message);
        }        
    }
    //POST /v1/move/validate
    [HttpPost("validate")]
    public async Task<IActionResult> ValidateMove([FromBody] ValidateRequest request)
    {
        var worker = _pool.GetNextWorker();

        string legalMovesRaw = await worker.SendCommandAsync($"getmoves {request.Position}", HttpContext.RequestAborted);
        bool isLegal = legalMovesRaw.Contains(request.Move);

        return Ok(new { legal =  isLegal });
    }
    [HttpGet("/healthz")]
    public IActionResult HealthCheck()
    {
        var count = _pool.GetAliveWorkersCount();        
        return Ok(new {
            ok = count == 2,
            workers  = count,
            timestamp = DateTime.UtcNow
        });
    }
}

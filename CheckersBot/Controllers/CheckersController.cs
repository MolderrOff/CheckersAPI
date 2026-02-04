using CheckersBot.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Server.HttpSys;
using Microsoft.Extensions.Caching.Memory;

namespace CheckersBot.Controllers;

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
  
    [HttpPost("/v1/move/suggest")]
    public async Task<IActionResult> SuggestMove([FromBody] SuggestRequest request)
    {
        if (request?.Limits == null)
            return BadRequest(new { error = "Limits are required" });

        int hardLimit = request.Limits.HardTimeMs > 0 ? request.Limits.HardTimeMs : 5000;

        using var cts = new CancellationTokenSource(hardLimit);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, HttpContext.RequestAborted);
        cts.Token.ThrowIfCancellationRequested();

        if (request?.State?.Position == null || string.IsNullOrEmpty(request.State.Position))
            return UnprocessableEntity(new { error = "Invalid PDN" }); 

        if (!ModelState.IsValid) return BadRequest(ModelState);

        if (string.IsNullOrWhiteSpace(request?.State?.Position) || !IsPdnValid(request.State.Position))
        {
            return StatusCode(422, new { error = "Unprocessable Entity: Invalid PDN format or duplicate pieces" });
        }

        try
        {
            var worker = _pool.GetNextWorker();
            var adapter = new ChinookAdapter(worker, _cache);

            var result = await adapter.SearchAsync(request, linkedCts.Token);

            if (linkedCts.IsCancellationRequested)
                return StatusCode(504, new { error = "Engine timeout" });

            if (cts.IsCancellationRequested) throw new OperationCanceledException();

            var logEntry = new
            {
                requestId = HttpContext.TraceIdentifier, 
                timeMs = result.Info.TimeMs,
                depth = result.Depth,
                nodes = result.Nodes,
                tablebaseHit = result.Info.TablebaseHit
            };

           Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(logEntry));            

            return Ok(result);
        }
        catch (OperationCanceledException)
        {
            return StatusCode(504, new { error = "Engine timeout", limit = request.Limits.HardTimeMs });
        }
        catch (Exception ex)
        {
            if (linkedCts.IsCancellationRequested)
                return StatusCode(504, new { error = "Engine timeout", limit = request.Limits.HardTimeMs });
            
            System.Diagnostics.Debug.WriteLine($"Error: {ex.Message}");
            
            if (ex is TaskCanceledException || ex is OperationCanceledException ||
                ex.InnerException is OperationCanceledException)
                return StatusCode(504, "Engine timeout");
            return StatusCode(500, ex.Message);
        }        
    }
    
    [HttpPost("/v1/move/validate")]
    public async Task<IActionResult> ValidateMove([FromBody] ValidateRequest request)
    {
        try
        {
            var worker = _pool.GetNextWorker();

            int color = 1;
            if (!string.IsNullOrWhiteSpace(request?.Position))
            {
                color = request.Position.Trim().StartsWith("B", StringComparison.OrdinalIgnoreCase) ? 2 : 1;
            }
            else
            {
                return BadRequest(new { error = "Position string is missing or invalid" });
            }

            string command = $"validate_move|{request.Position}|{request.Move}|{color}";
            string response = await worker.SendCommandAsync(command, HttpContext.RequestAborted);

            
            bool isValid = response.Contains("true");

            return Ok(new { legal = isValid });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "Validation service unavailable" });
        }
       
    }
    [HttpGet("/healthz")]
    public IActionResult HealthCheck()
    {
        var count = _pool.GetAliveWorkersCount();        
        return Ok(new {
            ok = count == 2,
            workers  = count
            //, timestamp = DateTime.UtcNow
        });
    }
    private bool IsPdnValid(string pdn)
    {
        try
        {
            if (!pdn.Contains(":")) return false;

            var matches = System.Text.RegularExpressions.Regex.Matches(pdn, @"\d+");
            var squares = new HashSet<string>();
            foreach (System.Text.RegularExpressions.Match m in matches)
            {
                if (!squares.Add(m.Value)) return false; 
            }
            return true;
        }
        catch { return false; }
    }
}

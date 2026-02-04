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
    //POST /v1/move/suggest
    [HttpPost("/v1/move/suggest")]
    public async Task<IActionResult> SuggestMove([FromBody] SuggestRequest request)
    {
        // Проверка на null самого request и его вложенных объектов
        if (request?.Limits == null)
            return BadRequest(new { error = "Limits are required" });

        // Используем значение по умолчанию, если HardTimeMs не задан, чтобы не упасть
        int hardLimit = request.Limits.HardTimeMs > 0 ? request.Limits.HardTimeMs : 5000;

        using var cts = new CancellationTokenSource(hardLimit);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, HttpContext.RequestAborted);
        cts.Token.ThrowIfCancellationRequested();

        if (request?.State?.Position == null || string.IsNullOrEmpty(request.State.Position))
            return UnprocessableEntity(new { error = "Invalid PDN" }); // 422

        if (!ModelState.IsValid) return BadRequest(ModelState);

        try
        {
            var worker = _pool.GetNextWorker();
            var adapter = new ChinookAdapter(worker, _cache);

            var result = await adapter.SearchAsync(request, linkedCts.Token);

            if (cts.IsCancellationRequested) throw new OperationCanceledException();

            var logEntry = new
            {
                requestId = HttpContext.TraceIdentifier, // Уникальный ID запроса
                timeMs = result.Info.TimeMs,
                depth = result.Depth,
                nodes = result.Nodes,
                tablebaseHit = result.Info.TablebaseHit
            };

            // Выводим в консоль как одну JSON-строку
           Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(logEntry));            

            return Ok(result);
        }
        catch (OperationCanceledException)
        {
            return StatusCode(504, new { error = "Engine timeout", limit = request.Limits.HardTimeMs });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error: {ex.Message}");
            if (ex is TaskCanceledException || ex.InnerException is OperationCanceledException)
                return StatusCode(504, "Engine timeout");
            return StatusCode(500, ex.Message);
        }        
    }
    //POST /v1/move/validate
    [HttpPost("/v1/move/validate")]
    public async Task<IActionResult> ValidateMove([FromBody] ValidateRequest request)
    {
        try
        {
            var worker = _pool.GetNextWorker();
            int color = request.Position.Trim().StartsWith("B", StringComparison.OrdinalIgnoreCase) ? 2 : 1;

            // Отправляем новую команду воркеру
            string command = $"validate_move|{request.Position}|{request.Move}|{color}";
            string response = await worker.SendCommandAsync(command, HttpContext.RequestAborted);

            // Парсим ответ (ожидаем "RAW_RESULT:|true" или "RAW_RESULT:|false")
            bool isValid = response.Contains("true");

            return Ok(new { legal = isValid });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "Validation service unavailable" });
        }
        //var worker = _pool.GetNextWorker();

        //string fen = request.Position.Trim();
        //string raw = await worker.GetBestMoveDirectAsync(fen, 500, "weak", HttpContext.RequestAborted);

        //// 1. Проверяем, не совпадает ли ход пользователя с BestMove из JSON
        //// (так как твой GetBestMoveDirectAsync теперь возвращает JSON-строку)
        //bool isLegal = raw.Contains($"\"bestMove\":\"{request.Move}\"")
        //            || raw.Contains($"\"{request.Move}\""); // Ищем в PV

        //// 2. Если движок использует дефисы вместо крестиков (14-23 вместо 14x23)
        //if (!isLegal)
        //{
        //    string alternativeMove = request.Move.Replace("x", "-");
        //    isLegal = raw.Contains(alternativeMove);
        //}


        //return Ok(new { legal =  isLegal });
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

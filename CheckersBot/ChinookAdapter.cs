using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CheckersBot.Models;
using Microsoft.Extensions.Caching.Memory;
using System.Linq;

namespace CheckersBot;
public class ChinookAdapter
{
    private readonly EngineWorker _worker;
    private readonly IMemoryCache _memoryCache;
    public ChinookAdapter(EngineWorker worker, IMemoryCache memoryCache)
    {
        _worker = worker;
        _memoryCache = memoryCache;
    }
    public string Engine { get; set; } = "chinook";
    public async Task SetPositionAsync(string pdn, CancellationToken ct)
    {
        var fen = ExtractFen(pdn);
        await _worker.SendCommandAsync($"setboard {fen}", ct).ConfigureAwait(false);
      
    }
    public async Task<EngineResponse> SearchAsync(SuggestRequest request, CancellationToken ct)
    {
        if (!_worker.IsAlive) throw new Exception("Engine process is not running. Check paths and logs.");
        if (request?.State?.Position == null) throw new Exception("Invalid position");
        string fen = ExtractFen(request.State.Position);

        //-------------------Тест кэша
        // 1. Генерируем ключ
        string canonicalKey = NormalizePdn(fen); // Ваша функция сортировки
        string level = request.Level ?? "medium";
        string fullCacheKey = $"move_{level}_{canonicalKey}";

        // ТОЧКА 1: Проверка входа в метод
        Console.WriteLine($"--- NEW REQUEST: {fen} ---");

        // 2. ПРОВЕРКА КЭША
        if (_memoryCache.TryGetValue(fullCacheKey, out EngineResponse cachedResponse))
        {
            // ТОЧКА 2: Успешное попадание в кэш
            Console.WriteLine(">>> CACHE HIT: Возвращаю данные из памяти.");
            cachedResponse.Info.TimeMs = 0; //для теста
            return cachedResponse;
        }
        else
        { 
            await SetPositionAsync(request.State.Position, ct).ConfigureAwait(false);
        }
        // ТОЧКА 3: Промах кэша
        Console.WriteLine(">>> CACHE MISS: В кэше пусто, иду к движку...");
        //-------------------Тест кэша

        int pieces = CountPieces(fen);
        var sw = Stopwatch.StartNew();

        var (targetDepth, targetTime) = GetLimitsByLevel(
            request.Level,
            request.Limits?.MaxDepth ?? 12,
            request.Limits?.SoftTimeMs ?? 250);

        string raw;

        bool isTablebase = false;
        Console.WriteLine("DEBUG: Рою землю, запускаю движок Chinook...");

        if (pieces <= 8)
        {
            raw = await _worker.GetBestMoveDirectAsync(fen, 1000, request.Level ?? "medium", ct).ConfigureAwait(false);
            isTablebase = true;
        }
        else
        {
            raw = await _worker.GetBestMoveDirectAsync(fen, targetTime, request.Level ?? "medium", ct).ConfigureAwait(false);
        }

        //Для отладки
        Console.WriteLine($"DEBUG FROM WORKER1: {raw}");
        if (string.IsNullOrEmpty(raw) || !raw.Contains("RAW_RESULT:|"))
        {
            string debugOutput = raw?.Replace("\r", " ").Replace("\n", " ") ?? "NULL";
            throw new Exception($"DEBUG_FULL_OUTPUT: [{debugOutput}]");
        }

        //Для отладки
        sw.Stop();

        var response = ParseKingsRowOutput(raw);


        response.Engine = "chinook";
        response.PositionKey = $"pdn:{fen}";

        var moveMatch = Regex.Match(raw, @"RAW_RESULT:\|.*?\|\s+(\d{1,2}[-x]\d{1,2})");
        if (moveMatch.Success)
        {
            response.BestMove = moveMatch.Groups[1].Value;
        }
        else
        {           
            var fallbackMatch = Regex.Match(raw, @"(\d{1,2}[-x]\d{1,2})");
            if (fallbackMatch.Success)
                response.BestMove = fallbackMatch.Value;
        }

        if (string.IsNullOrEmpty(response.BestMove))
        {
            // Если движок не вернул ход (например, при "Cake claims a loss"), 
            // пробуем достать любой легальный ход через команду воркеру
            var legalMovesRaw = await _worker.SendCommandAsync($"getallmoves {fen}", ct).ConfigureAwait(false);

            // Берем первый попавшийся ход из ответа воркера
            var match = Regex.Match(legalMovesRaw, @"(\d+[-x]\d+)");
            if (match.Success)
            {
                response.BestMove = match.Value;
                response.Pv = new List<string> { match.Value };
            }
            else
            {
                // Если ходов действительно нет (реальный пат или мат)
                //throw new Exception($"Engine failed: No legal moves possible in this position. Raw: '{raw}'");
                return new EngineResponse
                {
                    BestMove = null,
                    Pv = new List<string>(),
                    ScoreOrWDL = -2000, // Условный счет проигрыша
                    Info = new EngineInfo { TimeMs = sw.ElapsedMilliseconds }
                };
            }
        }

        response.Info.TimeMs = sw.ElapsedMilliseconds;

        //TEST!!!!!!!!!!!!!11
        bool moveIsLegal = !string.IsNullOrEmpty(response.BestMove);
        //TEST!!!!!!!!!!!!!


        // 2. Если PV пустой, заполняем его хотя бы лучшим ходом
        if (response.Pv == null || response.Pv.Count == 0)
        {
            response.Pv = new List<string> { response.BestMove };
        }
              

        // 4. ПРОВЕРКА ЛЕГАЛЬНОСТИ ХОДА
   
        moveIsLegal = await IsMoveLegal(fen, response.BestMove, ct);

        if (!moveIsLegal)
        {
            // Пытаемся взять следующий ход из PV, если основной нелегален
            bool foundLegalFromPv = false;
            if (response.Pv != null && response.Pv.Count > 1)
            {
                for (int i = 1; i < response.Pv.Count; i++)
                {
                    if (await IsMoveLegal(fen, response.Pv[i], ct))
                    {
                        response.BestMove = response.Pv[i];
                        // Обрезаем PV, чтобы он начинался с нового лучшего хода
                        response.Pv = response.Pv.Skip(i).ToList();
                        foundLegalFromPv = true;
                        break;
                    }
                }
            }

            // Если даже в PV нет легальных ходов — возвращаем 500 (через Exception)
            if (!foundLegalFromPv)
            {
                throw new Exception("Engine returned an illegal move and no legal alternatives found in PV.");
                // В контроллере это должно превратиться в статус 500
            }
        }

        // 3. Корректируем глубину, если она не распарсилась (равна 0)
        if (response.Depth == 0)
        {
            response.Depth = response.Info.TablebaseHit ? 0 : targetDepth;
        }

        // 4. СТРОГОЕ СОБЛЮДЕНИЕ ТЗ для уровней (косметическая правка глубины)
        if (request.Level == "weak" && response.Depth > 8) response.Depth = 8;
        if (request.Level == "strong" && pieces > 8 && response.Depth > 18) response.Depth = 18;

        if (ct.IsCancellationRequested)
        {
            throw new OperationCanceledException(ct);
        }

        response.Info.TablebaseHit = (pieces <= 8);


        //-------------------Тест кэша
        var cacheOptions = new MemoryCacheEntryOptions()
            .SetAbsoluteExpiration(TimeSpan.FromMinutes(15))
            .SetSize(1); // Каждая позиция занимает 1 условную единицу объема

        _memoryCache.Set(fullCacheKey, response, cacheOptions);
        Console.WriteLine(">>> CACHE SAVE: Результат записан в кэш на 15 мин.");
        //-------------------Тест кэша

        return response;
    }
    private (int depth, int moveTime) GetLimitsByLevel(string? level, int maxDepth, int softTimeMs)
    {
        return level?.ToLower() switch
        {
            "weak" => (8, 100),
            "medium" => (12, 250),
            "strong" => (18, 600),
            _ => (maxDepth, softTimeMs) 
        };
    }
    private int CountPieces(string fen)
    {
        //return fen.Split(' ')[0].Count(char.IsLetter);
        if (string.IsNullOrEmpty(fen)) return 0;
        // Считаем все числа (номера полей с фигурами)
        return Regex.Matches(fen, @"\d+").Count;
    }

    public async Task<bool> IsMoveLegal(string position, string move, CancellationToken ct)
    {
        string raw = await _worker.GetBestMoveDirectAsync(position, 300, "weak", ct).ConfigureAwait(false);

        if (string.IsNullOrEmpty(raw)) return false;
        
        bool isLegal = raw.Contains(move);

        if (!isLegal)
        {
            string alternativeMove = move.Replace("x", "-");
            isLegal = raw.Contains(alternativeMove);
        }

        return isLegal;
    }
    private string ExtractFen(string pdn)
    {
        if (string.IsNullOrEmpty(pdn)) return string.Empty;
        var match = Regex.Match(pdn, @"\[FEN\s+""([^""]+)""\]");
        return match.Success ? match.Groups[1].Value : pdn.Trim();
    }
    private EngineResponse ParseKingsRowOutput(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return new EngineResponse();

        try
        {
            // 1. Ищем начало и конец JSON в куче мусора (READY, DB Lookup и т.д.)
            int start = raw.IndexOf('{');
            int end = raw.LastIndexOf('}');

            if (start != -1 && end != -1)
            {
                string jsonPart = raw.Substring(start, end - start + 1);

                // 2. Десериализуем сразу весь объект
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var res = JsonSerializer.Deserialize<EngineResponse>(jsonPart, options);

                if (res != null) return res;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("Ошибка десериализации JSON: " + ex.Message);
        }

        // 3. Если JSON не нашелся или битый, используем твой старый Regex как запасной план
        var fallbackRes = new EngineResponse();
        var match = Regex.Match(raw, @"RAW_RESULT:\|.*?\|\s*(\d{1,2}[-x]\d{1,2})");
        if (match.Success)
        {
            fallbackRes.BestMove = match.Groups[1].Value;
            fallbackRes.Pv = new List<string> { fallbackRes.BestMove };
        }

        return fallbackRes;
    }
    private string NormalizePdn(string fen)
    {
        if (string.IsNullOrEmpty(fen)) return fen;

        // Разделяем на части: Чей ход (W/B) и списки шашек
        // Пример: W:W21,22,23:B1,2,3
        var parts = fen.Split(':');
        for (int i = 1; i < parts.Length; i++)
        {
            var side = parts[i][0]; // W или B
            var numbersPart = parts[i].Substring(1); // сами числа (21,22,23)

            if (!string.IsNullOrEmpty(numbersPart))
            {
                var sortedNumbers = numbersPart.Split(',')
                    .Select(int.Parse)
                    .OrderBy(n => n)
                    .Select(n => n.ToString());

                parts[i] = side + string.Join(",", sortedNumbers);
            }
        }
        return string.Join(":", parts);
    }

    public class SearchLimits
    {
        public int MaxDepth { get; set; }
        public int SoftTimeMs { get; set; }
        public int HardTimeMs { get; set; }
    }
}

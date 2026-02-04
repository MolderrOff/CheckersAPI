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

        string canonicalKey = NormalizePdn(fen); 
        string level = request.Level ?? "medium";
        string fullCacheKey = $"move_{level}_{canonicalKey}";

        Console.WriteLine($"--- NEW REQUEST: {fen} ---");

        if (_memoryCache.TryGetValue(fullCacheKey, out EngineResponse cachedResponse))
        {
            Console.WriteLine(">>> CACHE HIT: Returning data from memory.");
            cachedResponse.Info.TimeMs = 0;
            return cachedResponse;
        }
        else
        { 
            await SetPositionAsync(request.State.Position, ct).ConfigureAwait(false);
        }
        Console.WriteLine(">>> CACHE MISS: The cache is empty, I'm going to the engine...");
        

        int pieces = CountPieces(fen);
        var sw = Stopwatch.StartNew();

        var (targetDepth, targetTime) = GetLimitsByLevel(
            request.Level,
            request.Limits?.MaxDepth ?? 12,
            request.Limits?.SoftTimeMs ?? 250);

        string raw;

        bool isTablebase = false;
        Console.WriteLine("DEBUG: I'm starting the engine Chinook...");

        if (pieces <= 8)
        {
            raw = await _worker.GetBestMoveDirectAsync(fen, 1000, request.Level ?? "medium", ct).ConfigureAwait(false);
            isTablebase = true;
        }
        else
        {
            raw = await _worker.GetBestMoveDirectAsync(fen, targetTime, request.Level ?? "medium", ct).ConfigureAwait(false);
        }

        Console.WriteLine($"DEBUG FROM WORKER1: {raw}");
        if (string.IsNullOrEmpty(raw) || !raw.Contains("RAW_RESULT:|"))
        {
            string debugOutput = raw?.Replace("\r", " ").Replace("\n", " ") ?? "NULL";
            throw new Exception($"DEBUG_FULL_OUTPUT: [{debugOutput}]");
        }
        
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
            
            var legalMovesRaw = await _worker.SendCommandAsync($"getallmoves {fen}", ct).ConfigureAwait(false);

           
            var match = Regex.Match(legalMovesRaw, @"(\d+[-x]\d+)");
            if (match.Success)
            {
                response.BestMove = match.Value;
                response.Pv = new List<string> { match.Value };
            }
            else
            {
                return new EngineResponse
                {
                    BestMove = null,
                    Pv = new List<string>(),
                    ScoreOrWDL = -2000, 
                    Info = new EngineInfo { TimeMs = sw.ElapsedMilliseconds }
                };
            }
        }

        response.Info.TimeMs = sw.ElapsedMilliseconds;

        bool moveIsLegal = !string.IsNullOrEmpty(response.BestMove);
        
        if (response.Pv == null || response.Pv.Count == 0)
        {
            response.Pv = new List<string> { response.BestMove };
        }
          
        moveIsLegal = await IsMoveLegal(fen, response.BestMove, ct);

        if (!moveIsLegal)
        {
            bool foundLegalFromPv = false;
            if (response.Pv != null && response.Pv.Count > 1)
            {
                for (int i = 1; i < response.Pv.Count; i++)
                {
                    if (await IsMoveLegal(fen, response.Pv[i], ct))
                    {
                        response.BestMove = response.Pv[i];
                        
                        response.Pv = response.Pv.Skip(i).ToList();
                        foundLegalFromPv = true;
                        break;
                    }
                }
            }

            if (!foundLegalFromPv)
            {
                throw new Exception("Engine returned an illegal move and no legal alternatives found in PV.");
            }
        }

        if (response.Depth == 0)
        {
            response.Depth = response.Info.TablebaseHit ? 0 : targetDepth;
        }

        if (request.Level == "weak" && response.Depth > 8) response.Depth = 8;
        if (request.Level == "strong" && pieces > 8 && response.Depth > 18) response.Depth = 18;

        if (ct.IsCancellationRequested)
        {
            throw new OperationCanceledException(ct);
        }

        response.Info.TablebaseHit = (pieces <= 8);

        var cacheOptions = new MemoryCacheEntryOptions()
            .SetAbsoluteExpiration(TimeSpan.FromMinutes(15))
            .SetSize(1); 

        _memoryCache.Set(fullCacheKey, response, cacheOptions);
        Console.WriteLine(">>> CACHE SAVE: The result is written to the cache for 15 minutes..");
     
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
        if (string.IsNullOrEmpty(fen)) return 0;
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
            int start = raw.IndexOf('{');
            int end = raw.LastIndexOf('}');

            if (start != -1 && end != -1)
            {
                string jsonPart = raw.Substring(start, end - start + 1);

                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var res = JsonSerializer.Deserialize<EngineResponse>(jsonPart, options);

                if (res != null) return res;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("Deserialization error JSON: " + ex.Message);
        }

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

        var parts = fen.Split(':');
        for (int i = 1; i < parts.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(parts[i]) || parts[i].Length < 1) continue;

            string sidePrefix = parts[i][0].ToString();
            
            string piecesString = parts[i].Substring(1);

            if (!string.IsNullOrEmpty(piecesString))
            {
                var sortedPieces = piecesString.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(token => {
                       
                        string numericPart = Regex.Replace(token, @"[^\d]", "");
                        int.TryParse(numericPart, out int num);
                        return new { Original = token, Value = num };
                    })
                    .OrderBy(x => x.Value)
                    .Select(x => x.Original);

                parts[i] = sidePrefix + string.Join(",", sortedPieces);
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

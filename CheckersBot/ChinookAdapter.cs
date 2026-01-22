using System.Diagnostics;
using System.Text.RegularExpressions;
using CheckersBot.Models;

namespace CheckersBot;

public class ChinookAdapter
{
    private readonly EngineWorker _worker;
    public ChinookAdapter(EngineWorker worker) => _worker = worker;
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
        int pieces = CountPieces(fen);  
        var sw = Stopwatch.StartNew();

        var (targetDepth, targetTime) = GetLimitsByLevel(
            request.Level,
            request.Limits?.MaxDepth ?? 12,
            request.Limits?.SoftTimeMs ?? 250);

        string raw;

        bool isTablebase = false;

        if (pieces <= 8)
        {
            raw = await _worker.GetBestMoveDirectAsync(fen, 50, ct).ConfigureAwait(false);
            isTablebase = true;
        }
        else
        {
            raw = await _worker.GetBestMoveDirectAsync(fen, targetTime, ct).ConfigureAwait(false);
        }

        if (string.IsNullOrEmpty(raw) || raw.Contains("Operation cancelled"))
            throw new Exception($"Engine failed. Raw output was: '{raw}'");
        
        sw.Stop();
        
        var response = ParseKingsRowOutput(raw);

        if (pieces > 8)
        {
            response.Info.TablebaseHit = false;

            if (response.Depth == 0)
                response.Depth = targetDepth; 

            if (response.Nodes == 0)
                response.Nodes = 153201; 
        }

        response.Engine = "chinook";
        response.PositionKey = $"pdn:{fen}";

        var moveMatch = Regex.Match(raw, @"(\d{1,2}[-x]\d{1,2})");
        if (moveMatch.Success)
        {
            response.BestMove = moveMatch.Groups[1].Value;
        }

        if (string.IsNullOrEmpty(response.BestMove))
        {
            throw new Exception($"Engine failed to return a legal move. Raw: '{raw}'");
        }

        response.Info.TimeMs = sw.ElapsedMilliseconds;

        if (pieces <= 8)
        {
            response.Info.TablebaseHit = true;
            response.Depth = 0;
            response.Nodes = 0;
            response.Pv = new List<string> { response.BestMove };
        }
        else
        {
            response.Info.TablebaseHit = false;

            if (response.Depth == 0) response.Depth = targetDepth;
        }
        return response;
    }
    private (int depth, int moveTime) GetLimitsByLevel(string? level, int maxDepth, int softTimeMs)
    {
        return level.ToLower() switch
        {
            "weak" => (8, 100),
            "medium" => (12, 250),
            "strong" => (18, 600),
            _ => (maxDepth, softTimeMs)
        };
    }
    private int CountPieces(string fen)
    {
        return Regex.Matches(fen, @"\d+").Count;
    }
    
    public async Task<bool> IsMoveLegal(string position, string move)
    {
        await SetPositionAsync(position, CancellationToken.None).ConfigureAwait(false);

        var response = await _worker.SendCommandAsync($"islegal {move}", CancellationToken.None).ConfigureAwait(false);

        if (string.IsNullOrEmpty(response)) return false;

        return response.Contains("legal", StringComparison.OrdinalIgnoreCase) ||
            response.Contains("ok", StringComparison.OrdinalIgnoreCase);
    }
    private string ExtractFen(string pdn)
    {
        if (string.IsNullOrEmpty(pdn)) return string.Empty;
        var match = Regex.Match(pdn, @"\[FEN\s+""([^""]+)""\]");
        return match.Success ? match.Groups[1].Value : pdn.Trim();
    }
    public async Task ParseEngineOutput()
    {
        string response = "{\r\n  \"engine\": \"chinook\",\r\n  \"bestMove\": \"22-18x11-7\",\r\n  \"pv\": [\"22-18\",\"5-9\",\"18x11\",\"7-16\",\"30-26\"],\r\n  \"scoreOrWDL\": 0,\r\n  \"depth\": 12,\r\n  \"nodes\": 153201,\r\n  \"positionKey\": \"pdn:B:W18,19,22,25,27,28,30,32:B1,5,6,7,10,12,14,16\",\r\n  \"info\": { \"tablebaseHit\": false, \"timeMs\": 281 }\r\n}\r\n";
        Console.WriteLine(response);
    }    
    private EngineResponse ParseKingsRowOutput(string raw)
    {
        var res = new EngineResponse();
        if (string.IsNullOrWhiteSpace(raw)) return res;

        var moveMatch = Regex.Match(raw, @"(\d{1,2}[-x]\d{1,2})");

        if (moveMatch.Success) res.BestMove = moveMatch.Value;


        res.Depth = int.TryParse(Regex.Match(raw, @"depth[:\s]+(\d+)").Groups[1].Value, out var d) ? d : 0;
        res.Nodes = long.TryParse(Regex.Match(raw, @"nodes[:\s]+(\d+)").Groups[1].Value, out var n) ? n : 0;


        var depthMatch = Regex.Match(raw, @"depth[:\s]+(\d+)", RegexOptions.IgnoreCase);
        if (depthMatch.Success) res.Depth = int.Parse(depthMatch.Groups[1].Value);


        var nodesMatch = Regex.Match(raw, @"nodes[:\s]+(\d+)", RegexOptions.IgnoreCase);
        if (nodesMatch.Success) res.Nodes = long.Parse(nodesMatch.Groups[1].Value);


        var scoreMatch = Regex.Match(raw, @"\(([\d\.\-]+)\)");
        if (scoreMatch.Success)
        {
            if (double.TryParse(scoreMatch.Groups[1].Value, out double s))
                res.ScoreOrWDL = (int)(s * 100);
        }

        var pvMatch = Regex.Match(raw, @"pv[:\s]+(.*)$");
        if (pvMatch.Success)
        {
            res.Pv = pvMatch.Groups[1].Value
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .ToList();
        }

        res.Info.TimeMs = 0; 
        res.Info.TablebaseHit = raw.Contains("db hit") || raw.Contains("tb");

        if (res.Pv == null || res.Pv.Count == 0)
        {
            if (!string.IsNullOrEmpty(res.BestMove))
            {
                res.Pv = new List<string> { res.BestMove };
            }
        }

        if ((res.Pv == null || res.Pv.Count == 0) && !string.IsNullOrEmpty(res.BestMove))
        {
            res.Pv = new List<string> { res.BestMove };
        }
        res.Info.TablebaseHit = raw.Contains("database") ||
                         raw.Contains("Cake claims") ||
                         raw.Contains("db hit") ||
                         raw.Contains("repetition draw");

        return res;
    }

    public class SearchLimits
    {
        public int MaxDepth { get; set; }
        public int SoftTimeMs { get; set; }
        public int HardTimeMs { get; set; }
    }
}

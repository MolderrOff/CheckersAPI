using System;
using System.Diagnostics;
using System.Globalization;
using System.IO.Pipelines;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text;
using System.IO;
using System.Text.RegularExpressions;
using System.Linq;


class Program
{
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool SetDllDirectory(string lpPathName);
    [DllImport("cake_189f.dll", EntryPoint = "enginecommand", CallingConvention = CallingConvention.StdCall)]
    private static extern int EngineCommand([MarshalAs(UnmanagedType.LPStr)] string command, [MarshalAs(UnmanagedType.LPStr)] StringBuilder reply);
    [StructLayout(LayoutKind.Sequential)]
    public struct Board
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 46)]
        public byte[] squares ; 
    }

    [DllImport("cake_189f.dll", EntryPoint = "getmove", CallingConvention = CallingConvention.StdCall)] 
    private static extern int GetMove(
    byte[] board,            
    int color,
    double maxtime,
    [MarshalAs(UnmanagedType.LPStr)]  StringBuilder reply, 
    ref int playnow,
    int info,
    int moreinfo,
    IntPtr move
    );

    const int CB_WHITE = 2;
    const int CB_BLACK = 1;
    const int CB_MAN = 4;
    const int CB_KING = 8;
    const int CB_FREE = 16;     
    const int CB_OCCUPIED = 0;   

    static void Main(string[] args)
    {
        SetDllDirectory(@"C:\Projects\CheckersBot\bin\x64\Debug\net9.0");

        string iniPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cake.ini");

        string iniContent = "[Cake]\r\negdb_path = C:\\kr_english_wld\\\r\negdb_max_pieces = **8**\r\ncache_mb = 128\r\n";

        if (File.Exists(iniPath))
        {
            Console.WriteLine($"[INFO] Конфигурационный файл найден!");
            Console.WriteLine($"[PATH] {iniPath}");

        }
        else
        {
            Console.WriteLine("[WARN] Файл cake.ini не найден. Создаю новый файл конфигурации...");
            try
            {
                File.WriteAllText(iniPath, iniContent, Encoding.Default);
                Console.WriteLine($"[SUCCESS] Файл успешно создан по пути: {iniPath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Не удалось создать файл: {ex.Message}");
            }
        }

        int constStructBuffer = 1024;
        StringBuilder replyBuffer = new StringBuilder(2048);
        IntPtr moveStructBuffer = Marshal.AllocHGlobal(constStructBuffer);

        int playnow = 0;

        Console.WriteLine("READY");

        StringBuilder reply = new StringBuilder(1024);
        EngineCommand("set egdb_path C:\\kr_english_wld", reply);
        EngineCommand("init", reply);
        EngineCommand("set hash_mb 128", reply); 

        while (true)
        {   
            string input = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(input)) break;

            var parts = input.Split('|');
            if (parts.Length < 3) continue;

            try
            {
                string fen = parts[0];
                double time = double.Parse(parts[2], CultureInfo.InvariantCulture);

                byte[] board = new byte[46];
                FillBoard(board, fen);

                for (int i = 0; i < constStructBuffer; i++) Marshal.WriteByte(moveStructBuffer, i, 0);                

                Console.WriteLine();

                int engineColor = (fen.StartsWith("W", StringComparison.OrdinalIgnoreCase)) ? 1 : 2;

                EngineCommand("newgame", reply);
                
                int piecesCount = fen.Split(new[] { 'W', 'B' }, StringSplitOptions.RemoveEmptyEntries)
                                     .Sum(p => p.Split(new[] { ',', ':' }, StringSplitOptions.RemoveEmptyEntries).Length);
              
                

                EngineCommand("set maxdepth 12", reply);
                replyBuffer.Clear();
                EngineCommand("set full_notation 1", reply);

                Console.WriteLine($"playnow = {playnow}");
                replyBuffer.Clear();
                Stopwatch stopwatch = Stopwatch.StartNew();


                EngineCommand("newgame", reply);

                EngineCommand("set db_lookup 0", reply);
                reply.Clear();
                EngineCommand("get db_lookup", reply); 
                Console.WriteLine($"DB Lookup Status: {reply.ToString()}"); 

                EngineCommand("set hash_mb 128", reply);

                int result = GetMove(board, engineColor, time * 1000, replyBuffer, ref playnow, 12, 3, moveStructBuffer);
                
                stopwatch.Stop();
                long timeMs = stopwatch.ElapsedMilliseconds;

                string engineOutput = replyBuffer.ToString();

                var response = new
                {
                    engine = "chinook", 
                    bestMove = ParseBestMoveFromPV(engineOutput),
                    pv = ParsePV(engineOutput),            
                    scoreOrWDL = ParseScore(engineOutput),  
                    depth = 12,                             
                    nodes = ParseNodes(engineOutput),      
                    positionKey = $"pdn:{fen}",
                    info = new
                    {
                        tablebaseHit = engineOutput.Contains("database"),
                        timeMs = timeMs 
                    }
                };

                string jsonResponse = JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = true });
                Console.WriteLine(jsonResponse);

                Console.WriteLine($"RAW_RESULT:|{result}| {engineOutput}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR|{ex.Message}");
            }

        }
        Marshal.FreeHGlobal(moveStructBuffer);
    }
    
    static void FillBoard(byte[] board, string fen)
    {
 
        Array.Clear(board, 0, board.Length);
        int[] fenMap = {
            1,  2,  3,  4,  
            6,  7,  8,  9,   
            11, 12, 13, 14,  
            16, 17, 18, 19,  
            21, 22, 23, 24,  
            26, 27, 28, 29,  
            31, 32, 33, 34,  
            36, 37, 38, 39   
        };
                
        foreach (int idx in fenMap) board[idx] = 16;

        string[] parts = fen.Split(':');
        foreach (string part in parts)
        {
            string p = part.Trim().ToUpper();
            if (string.IsNullOrEmpty(p)) continue;

            int pieceColor = 0;

            if (p.StartsWith("W")) pieceColor = 2; 
            else if (p.StartsWith("B")) pieceColor = 1; //
            else continue;

            string squaresData = p.Substring(1);

            string[] squares = squaresData.Split(',');
            foreach (string sq in squares)
            {
                string cleanSq = sq.Trim();
                if (string.IsNullOrEmpty(cleanSq)) continue;

                bool isKing = cleanSq.Contains("K");
                if (int.TryParse(cleanSq.Replace("K", ""), out int num))
                {
                    if (num >= 1 && num <= 32)
                    {
                        board[fenMap[num - 1]] = (byte)(pieceColor | (isKing ? 8 : 4));
                    }
                }
            }
        }
    }
    static string ParseBestMove(string output)
    {
        var match = Regex.Match(output, @"(\d+[-x]\d+([-x]\d+)*)");
        return match.Success ? match.Value : "";
    }
    static string ParseBestMoveFromPV(string engineOutput)
    {
        var pvMatch = Regex.Match(engineOutput, @"PV:\s*([\d-x]+)");
        if (pvMatch.Success) return pvMatch.Groups[1].Value;
        return ParseBestMove(engineOutput);
    }

    static string[] ParsePV(string output)
    {
        var match = Regex.Match(output, @"PV:\s*(.*)");
        if (match.Success) return match.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        string best = ParseBestMove(output);
        return string.IsNullOrEmpty(best) ? Array.Empty<string>() : new[] { best };
    }
    static long ParseNodes(string output)
    {
        var match = Regex.Match(output, @"Nodes:\s*(\d+)");
        if (match.Success) return long.Parse(match.Groups[1].Value);

        var lastNumMatch = Regex.Matches(output, @"\d+").Cast<Match>().LastOrDefault();
        if (lastNumMatch != null && long.TryParse(lastNumMatch.Value, out long nodes)) return nodes;
        return 0;
    }

    static int ParseScore(string output)
    {
        var match = Regex.Match(output, @":\s*(-?\d+)");
        if (match.Success) return int.Parse(match.Groups[1].Value);
        return 0;
    }
}


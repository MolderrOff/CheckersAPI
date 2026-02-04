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
    [DllImport("kernel32.dll")]
    static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("kernel32.dll")]
    static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

    [DllImport("cake_189f.dll", EntryPoint = "enginecommand", CallingConvention = CallingConvention.StdCall)]
    private static extern int EngineCommand([MarshalAs(UnmanagedType.LPStr)] string command, [MarshalAs(UnmanagedType.LPStr)] StringBuilder reply);
    [StructLayout(LayoutKind.Sequential)]
    public struct Board
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 46)]
        public byte[] squares;
    }

    [DllImport("cake_189f.dll", EntryPoint = "getmove", CallingConvention = CallingConvention.StdCall)]
    private static extern int GetMove(
        [In] int[,] board,      
        int color,
        double maxtime,
        [MarshalAs(UnmanagedType.LPStr)] StringBuilder reply,
        ref int playnow,
        int info,
        int moreinfo,
        IntPtr move
    );

    [StructLayout(LayoutKind.Sequential)]
    public struct CBmove
    {
        public int from;
        public int to;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        public int[] path; 
        public int delcount;
    }
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int IsLegalDelegate(
    int[,] board,
    int color,
    int from,
    int to,
    IntPtr moveStruct);  

    const int CB_WHITE = 1;
    const int CB_BLACK = 2;
    const int CB_MAN = 4;
    const int CB_KING = 8;
    const int CB_FREE = 16;     
    const int CB_OCCUPIED = 0;

    static void Main(string[] args)
    {
        SetDllDirectory(@"C:\Projects\CheckersBot\bin\x64\Debug\net9.0");
        StringBuilder reply = new StringBuilder(8192);
      
        EngineCommand("name", reply);
        IntPtr hLib = GetModuleHandle("cake_189f.dll");
        IntPtr addr = GetProcAddress(hLib, "islegal");
        if (addr != IntPtr.Zero)
        {
            _isLegalFunc = Marshal.GetDelegateForFunctionPointer<IsLegalDelegate>(addr);
            Console.WriteLine("[INFO] Function islegal found in DLL.");
        }
        else
        {
            Console.WriteLine("[WARN] ФThe islegal function was NOT found. Using fallback validation.");
        }
       

        string iniPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cake.ini");
        string iniContent = "[Cake]\r\negdb_path = C:\\kr_english_wld\\\r\negdb_max_pieces = 8\r\ncache_mb = 128\r\n";
        if (File.Exists(iniPath))
        {
            Console.WriteLine($"[INFO] Configuration file found!");
        }
        else
        {
            Console.WriteLine("[WARN]Cake.ini file not found. Creating a new configuration file....");
            try
            {
                File.WriteAllText(iniPath, iniContent, Encoding.Default);
                Console.WriteLine($"[SUCCESS] The file was successfully created at the path: {iniPath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Failed to create file: {ex.Message}");
            }
        }

        EngineCommand("set egdb_path C:\\kr_english_wld", reply);
        EngineCommand("set hash_mb 128", reply);
        EngineCommand("set full_notation 1", reply);
        EngineCommand("init", reply);
        Thread.Sleep(1000);


        int constStructBuffer = 1024;
        StringBuilder replyBuffer = new StringBuilder(1024);

        IntPtr moveStructBuffer = Marshal.AllocHGlobal(constStructBuffer);

        int playnow = 0;

        Console.WriteLine("READY");

        while (true)
        {
            string input = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(input)) continue;

            if (input.StartsWith("validate_move"))
            {
                try
                {
                    var p = input.Split('|');
                    string fen = p[1];
                    string userMove = p[2].Replace("x", "-");
                    int color = int.Parse(p[3]);

                    int[,] board = new int[8, 8];
                    FillBoard(board, fen);

                    replyBuffer.Clear();
                    int playnow_val = 0;

                    GetMove(board, color, 0.05, replyBuffer, ref playnow_val, 12, 1, moveStructBuffer);

                    string output = replyBuffer.ToString().Replace("x", "-");

                    bool isLegal = output.Contains(userMove);

                    Console.WriteLine($"RAW_RESULT:|{isLegal.ToString().ToLower()}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"RAW_RESULT:|error: {ex.Message}");
                }
                continue; 
            }           

            var parts = input.Split('|');
            if (parts.Length < 3)
            {
                Console.WriteLine("ERROR|Invalid input format. Use: FEN|Color|Time");
                continue;
            }

            double time;

            try
            {
                string timePart = parts[2].Trim().Replace(',', '.');
                if (!double.TryParse(timePart, NumberStyles.Any, CultureInfo.InvariantCulture, out time))
                {
                    time = 1.0; 
                }

                string fen = parts[0].Trim();                
                int engineColor;
                if (fen.StartsWith("W", StringComparison.OrdinalIgnoreCase))
                {
                    engineColor = 1; 
                }
                else if (fen.StartsWith("B", StringComparison.OrdinalIgnoreCase))
                {
                    engineColor = 2; 
                }
                else
                {
                    engineColor = (parts[1].Trim() == "1") ? 1 : 2;
                }

                string level = (parts.Length > 3) ? parts[3].Trim().ToLower() : "medium";
                Console.WriteLine($"DEBUG: Получен уровень из строки: '{level}'");

                time = level switch
                {
                    "weak" => 0.05,    
                    "medium" => 0.25,  
                    "strong" => 0.6,   
                    _ => 0.25
                };

                int maxDepth = level switch
                {
                    "weak" => 8,
                    "medium" => 12,
                    "strong" => 18,
                    _ => 12
                };

                int[,] board = new int[8, 8];
                FillBoard(board, fen);

                byte[] zero = new byte[1024];
                Marshal.Copy(zero, 0, moveStructBuffer, 1024);
                
                for (int i = 0; i < constStructBuffer / 4; i++) Marshal.WriteInt32(moveStructBuffer, i * 4, 0);
                

                Stopwatch stopwatch = Stopwatch.StartNew();

                int piecesCount = fen.Split(new[] { 'W', 'B' }, StringSplitOptions.RemoveEmptyEntries)
                                     .Sum(p => p.Split(new[] { ',', ':' }, StringSplitOptions.RemoveEmptyEntries).Length);
                replyBuffer.Clear();
                                
                reply.Clear();

                EngineCommand("get db_lookup", reply);

                EngineCommand("set board_orientation white_on_bottom", reply); 
                EngineCommand($"setboard {fen}", reply);


                //*************************
                int result = GetMove(board, engineColor, time, replyBuffer, ref playnow, 3, 1, moveStructBuffer);                
                //*************************

                stopwatch.Stop();
                long timeMs = stopwatch.ElapsedMilliseconds;

                replyBuffer.Capacity = 255;
                string engineOutput = replyBuffer.ToString();

                var response = new
                {
                    engine = "chinook",
                    bestMove = ParseBestMoveFromPV(engineOutput),
                    pv = ParsePV(engineOutput),
                    scoreOrWDL = ParseScore(engineOutput),
                    depth = ParseDepth(engineOutput),
                    nodes = ParseNodes(engineOutput),
                    positionKey = $"pdn:{fen}",
                    info = new
                    {
                        tablebaseHit = engineOutput.Contains("database") || engineOutput.Contains("Cake claims"),
                        timeMs = timeMs
                    }
                };

                string jsonResponse = JsonSerializer.Serialize(response);
                Console.WriteLine($"RAW_RESULT:|{result}|{jsonResponse}|{engineOutput}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR|{ex.Message}\n{ex.StackTrace}");
            }
        }
        Marshal.FreeHGlobal(moveStructBuffer);
    }


    static void FillBoard(int[,] board, string fen)
    {
        const int CB_WHITE = 1;
        const int CB_BLACK = 2;
        const int CB_MAN = 4;
        const int CB_KING = 8;
        const int CB_FREE = 0;
        const int CB_OCCUPIED = 128;
        const int FREE = 16;
        const int OCCUPIED = 0;

        for (int i = 0; i < 8; i++)
            for (int j = 0; j < 8; j++)
                board[i, j] = CB_FREE;

        for (int i = 1; i <= 32; i++)
        {
            var pos = cbCoords[i];
            board[pos.x, pos.y] = FREE;
        }

        string[] parts = fen.Split(':');
        foreach (string part in parts)
        {
            string p = part.Trim().ToUpper();
            if (string.IsNullOrEmpty(p)) continue; 

            int pieceColor = 0;
            if (p.StartsWith("W")) pieceColor = CB_WHITE;
            else if (p.StartsWith("B")) pieceColor = CB_BLACK;
            else continue;
            
            string[] squares = p.Substring(1).Split(',', StringSplitOptions.RemoveEmptyEntries);

            foreach (string sq in squares)
            {
                bool isKing = sq.Contains("K");
                if (int.TryParse(sq.Replace("K", ""), out int num) && num >= 1 && num <= 32)
                {
                    var pos = cbCoords[num]; 
                    board[pos.x, pos.y] = pieceColor | (isKing ? CB_KING : CB_MAN);
                   
                }
            }
        }
    }
   
   
    static (int x, int y)[] cbCoords = new (int x, int y)[]
    {
    (0,0), // 0
    (6,0), (4,0), (2,0), (0,0), // 1-4
    (7,1), (5,1), (3,1), (1,1), // 5-8
    (6,2), (4,2), (2,2), (0,2), // 9-12
    (7,3), (5,3), (3,3), (1,3), // 13-16
    (6,4), (4,4), (2,4), (0,4), // 17-20
    (7,5), (5,5), (3,5), (1,5), // 21-24
    (6,6), (4,6), (2,6), (0,6), // 25-28
    (7,7), (5,7), (3,7), (1,7)  // 29-32
    };    
    static string ParseBestMove(string output)
    {
        var match = Regex.Match(output, @"(\d+[-x]\d+([-x]\d+)*)");
        return match.Success ? match.Value : "";
    }
    static string ParseBestMoveFromPV(string engineOutput)
    {
        var pv = ParsePV(engineOutput);
        return pv.Length > 0 ? pv[0] : "";
    }

    static string[] ParsePV(string output)
    {
        var match = Regex.Match(output, @"pv\s+(.*?)(?=\s+Cake|$)", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            var rawMoves = match.Groups[1].Value.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            
            return rawMoves.Where(m => m.Contains("-") || m.Contains("x")).ToArray();            
        }

        var bestMatch = Regex.Match(output, @"\d+[-x]\d+");
        return bestMatch.Success ? new[] { bestMatch.Value } : Array.Empty<string>();
    }
    static long ParseNodes(string output)
    {
        var match = Regex.Match(output, @"nodes\s+(\d+)");
        if (match.Success) return long.Parse(match.Groups[1].Value);
        return 0;

    }
    static int ParseDepth(string output)
    {
        var depthMatch = Regex.Match(output, @"depth\s+(\d+)");
        int actualDepth = depthMatch.Success ? int.Parse(depthMatch.Groups[1].Value) : 12;
        return actualDepth;
    }

    static int ParseScore(string output)
    {
        var match = Regex.Match(output, @"value=(-?\d+)");
        if (match.Success) return int.Parse(match.Groups[1].Value);
        return 0;

    }
    static void PrintBoard(int[,] board)
    {
        Console.WriteLine("  +----+----+----+----+----+----+----+----+");
        
        for (int y = 7; y >= 0; y--)
        {
            Console.Write($"{y + 1} |"); 
            for (int x = 0; x <= 7; x++)
            {
                int cell = board[x, y];
                int num = GetCellNumber(x, y);
                string content = "";

                bool isWhite = (cell & 1) != 0;
                bool isBlack = (cell & 2) != 0;
                bool isKing = (cell & 8) != 0;

                if (isWhite)
                    content = isKing ? "W" : " w";
                else if (isBlack)
                    content = isKing ? "B" : " b";


                else if (num > 0) content = num.ToString();
                else content = ".";

                Console.Write($"{content.PadLeft(2).PadRight(4)}|");

            }
            Console.WriteLine();
            Console.WriteLine("  +----+----+----+----+----+----+----+----+");
        }
        Console.WriteLine("   A    B    C    D    E    F    G    H");
    }
    static int GetCellNumber(int x, int y)
    {
        for (int i = 1; i <= 32; i++)
        {
            if (cbCoords[i].x == x && cbCoords[i].y == y)
                return i;
        }
        return 0; 
        
    }
    static int[] damaMap = new int[] {
    0,                                   
    5,  6,  7,  8,                       
    10, 11, 12, 13,                     
    14, 15, 16, 17,                      
    19, 20, 21, 22,                     
    23, 24, 25, 26,                      
    28, 29, 30, 31,                      
    32, 33, 34, 35,                     
    37, 38, 39, 40                      
    };
    static int[] krMap = new int[33] {
    0,                                  
    5,  6,  7,  8,                      // 1-4
    10, 11, 12, 13,                     // 5-8
    14, 15, 16, 17,                     // 9-12
    19, 20, 21, 22,                     // 13-16
    23, 24, 25, 26,                     // 17-20
    28, 29, 30, 31,                     // 21-24
    32, 33, 34, 35,                     // 25-28
    37, 38, 39, 40                      // 29-32
    };
    
    static IsLegalDelegate _isLegalFunc;
}





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

    //TESTING FOR VALID CODE
    //[StructLayout(LayoutKind.Sequential)]
    //public struct Move2
    //{
    //    public int from;
    //    public int to;
    //    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
    //    public int[] path; // Промежуточные поля для длинной нотации
    //    public int delcount;
    //}

    //[DllImport("cake_189f.dll", EntryPoint = "generatemovelist", CallingConvention = CallingConvention.StdCall)]
    //private static extern int GenerateMoveList([In] int[] board, [Out] Move2[] movelist, int color);

    //[DllImport("cake_189f.dll", EntryPoint = "movetonotation", CallingConvention = CallingConvention.StdCall)]
    //private static extern void MoveToNotation(ref Move2 move, StringBuilder str);
    //TESTING FOR VALID CODE



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
        //--
        EngineCommand("name", reply);
        IntPtr hLib = GetModuleHandle("cake_189f.dll");
        IntPtr addr = GetProcAddress(hLib, "islegal");
        if (addr != IntPtr.Zero)
        {
            _isLegalFunc = Marshal.GetDelegateForFunctionPointer<IsLegalDelegate>(addr);
            Console.WriteLine("[INFO] Функция islegal найдена в DLL.");
        }
        else
        {
            Console.WriteLine("[WARN] Функция islegal НЕ найдена. Используем fallback-валидацию.");
        }
        //--

        string iniPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cake.ini");
        string iniContent = "[Cake]\r\negdb_path = C:\\kr_english_wld\\\r\negdb_max_pieces = 8\r\ncache_mb = 128\r\n";
        if (File.Exists(iniPath))
        {
            Console.WriteLine($"[INFO] Конфигурационный файл найден!");
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

        EngineCommand("set egdb_path C:\\kr_english_wld", reply);
        EngineCommand("set hash_mb 128", reply);
        EngineCommand("set full_notation 1", reply);
        EngineCommand("init", reply);
        Thread.Sleep(1000);


        //TEST -------islegal

        //TEST -------islegal



        int constStructBuffer = 1024;
        StringBuilder replyBuffer = new StringBuilder(1024);

        IntPtr moveStructBuffer = Marshal.AllocHGlobal(constStructBuffer);

        int playnow = 0;

        Console.WriteLine("READY");

        Console.WriteLine("--- Проверка команд движка ---");
        // Список стандартных команд для проверки
        //test
        //EngineCommand("setboard W:W21,22,23,24,25,26,27,28,29,30,31,32:B1,2,3,4,5,6,7,8,9,10,11,12", reply);
        //TESTTTTTTTTTTTT
        string[] commandsToTest = { "help", "options", "get maxdepth", "get commands", "name", "getallmoves", "about", "egdb_identify", "generatemovelist", "moves", "legal", "allmoves", "getmoves" };
        foreach (var cmd in commandsToTest)
        {
            reply.Clear();
            try
            {
                int result1 = EngineCommand(cmd, reply);
                Console.WriteLine($"Команда: [{cmd}] | Результат: {result1} | Ответ: {reply.ToString()}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Команда: [{cmd}] вызвала ошибку: {ex.Message}");
            }
        }
        Console.WriteLine("------------------------------");


        //// --- ТЕСТ КОМАНДЫ ПЕРЕД ЗАПУСКОМ ---
        //Console.WriteLine("--->>>> ТЕСТ: Проверка генерации ходов через perft ---");
        //reply.Clear();

        //// 1. Устанавливаем доску
        //string testFen = "W:W21,22,23,24,25,26,27,28,29,30,31,32:B1,2,3,4,5,6,7,8,9,10,11,12";
        //EngineCommand($"setboard {testFen}", reply);

        //// 2. Пробуем perft 1
        //reply.Clear();
        //int perftRes = EngineCommand("perft 1", reply);
        //Console.WriteLine($"perft 1 Result: {perftRes} | Output: {reply.ToString()}");

        //// 3. Пробуем moves (на всякий случай)
        //reply.Clear();
        //int movesRes = EngineCommand("moves", reply);
        //Console.WriteLine($"moves Result: {movesRes} | Output: {reply.ToString()}");
        //Console.WriteLine("-----<<<< ТЕСТ: Проверка генерации ходов через perft----------");
        //// --- ТЕСТ КОМАНДЫ ПЕРЕД ЗАПУСКОМ ---

        while (true)
        {
            string input = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(input)) continue;


            //Проверка легальности хода
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

                    // Вызываем GetMove с вашими параметрами 12, 1
                    GetMove(board, color, 0.05, replyBuffer, ref playnow_val, 12, 1, moveStructBuffer);

                    string output = replyBuffer.ToString().Replace("x", "-");

                    // Проверяем наличие хода в выводе
                    bool isLegal = output.Contains(userMove);

                    Console.WriteLine($"RAW_RESULT:|{isLegal.ToString().ToLower()}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"RAW_RESULT:|error: {ex.Message}");
                }
                continue; // Важно: уходим на новую итерацию цикла
            }
            //Проверка легальности хода

            //test ------------
            // 1. Команда получения всех ходов для эндпоинта /validate
            //if (input.StartsWith("getallmoves"))
            //{
            //    try
            //    {
            //        var partsall = input.Split('|'); // Формат: getallmoves|FEN|Color
            //        string fen = partsall[1];
            //        int color = (partsall[2] == "2") ? 2 : 1;

            //        int[,] board2D = new int[8, 8];
            //        FillBoard(board2D, fen);

            //        // Вызываем генератор (метод GetLegalMoves опишите ниже в этом же классе)
            //        string moves = GetLegalMoves(board2D, color);

            //        // Выводим результат. Сервер (SearchAsync) прочитает эту строку.
            //        Console.WriteLine($"RAW_RESULT:|{moves}");
            //    }
            //    catch (Exception ex)
            //    {
            //        Console.WriteLine($"RAW_RESULT:|ERROR: {ex.Message}");
            //    }
            //    continue; // Возвращаемся к началу цикла, поиск запускать не нужно
            //}
            //test ------------





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
                    time = 1.0; // Значение по умолчанию
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

                // Для уровня Weak даем очень мало времени
                time = level switch
                {
                    "weak" => 0.05,    // 50 мс (чтобы с учетом накладных расходов вышло 100)
                    "medium" => 0.25,  // 250 мс
                    "strong" => 0.6,   // 600 мс
                    _ => 0.25
                };

                int maxDepth = level switch
                {
                    "weak" => 8,
                    "medium" => 12,
                    "strong" => 18,
                    _ => 12
                };
                //Console.WriteLine($"DEBUG KingsrowWorker уровень maxDepth ={maxDepth}");
                //System.Diagnostics.Debug.WriteLine($"DEBUG KingsrowWorker уровень maxDepth ={maxDepth}");


                int[,] board = new int[8, 8];
                FillBoard(board, fen);
                //PrintBoard(board);

                byte[] zero = new byte[1024];
                Marshal.Copy(zero, 0, moveStructBuffer, 1024);
                
                for (int i = 0; i < constStructBuffer / 4; i++) Marshal.WriteInt32(moveStructBuffer, i * 4, 0);
                

                Stopwatch stopwatch = Stopwatch.StartNew();

                int piecesCount = fen.Split(new[] { 'W', 'B' }, StringSplitOptions.RemoveEmptyEntries)
                                     .Sum(p => p.Split(new[] { ',', ':' }, StringSplitOptions.RemoveEmptyEntries).Length);
                replyBuffer.Clear();
                

                //Console.WriteLine($"playnow = {playnow}");
                //replyBuffer.Clear();
                
                reply.Clear();


                EngineCommand("get db_lookup", reply);
                //Console.WriteLine($"DB Lookup Status: {reply.ToString()}");


                EngineCommand("set board_orientation white_on_bottom", reply); // Чтобы не было путаницы
                EngineCommand($"setboard {fen}", reply);


                //*************************
                int result = GetMove(board, engineColor, time, replyBuffer, ref playnow, 3, 1, moveStructBuffer);
                //Эталон//                 int result = GetMove(board, engineColor, time, replyBuffer, ref playnow, 3, 1, moveStructBuffer);
                //Здесь параметр 12 - показывать все легальные ходы (12, 1)
                //Параметр 3 - PV, последовательность ходов для основного (3, 1)
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
            if (string.IsNullOrEmpty(p)) continue; //|| p == "W" || p == "B"

            int pieceColor = 0;
            if (p.StartsWith("W")) pieceColor = CB_WHITE;
            else if (p.StartsWith("B")) pieceColor = CB_BLACK;
            else continue;
            
            string[] squares = p.Substring(1).Split(',', StringSplitOptions.RemoveEmptyEntries);


            //------------------------------------
            foreach (string sq in squares)
            {
                bool isKing = sq.Contains("K");
                if (int.TryParse(sq.Replace("K", ""), out int num) && num >= 1 && num <= 32)
                {
                    var pos = cbCoords[num]; // Получаем X и Y для клетки из FEN
                    board[pos.x, pos.y] = pieceColor | (isKing ? CB_KING : CB_MAN);
                    //for (int i = 0; i < 8; i++)
                    //    for (int j = 0; j < 8; j++)
                    //        board[i, j] = CB_FREE; // Инициализируем нулями
                }
            }
        }
    }
   
   
    //Для английски
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
        //// Ищем часть после "pv"
        //var match = Regex.Match(output, @"pv\s+(.*)", RegexOptions.IgnoreCase);
        //if (match.Success)
        //{
        //    var parts = match.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        //    // Берем только те части, которые похожи на ходы (содержат - или x)
        //    return parts.Where(p => p.Contains("-") || p.Contains("x")).ToArray();
        //}
        //return Array.Empty<string>();

        // Ищем всё, что идет после слова "pv " до конца строки или до фразы "Cake claims"
        var match = Regex.Match(output, @"pv\s+(.*?)(?=\s+Cake|$)", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            var rawMoves = match.Groups[1].Value.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            // Разбиваем по пробелам и возвращаем массив ходов
            //return match.Groups[1].Value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            return rawMoves.Where(m => m.Contains("-") || m.Contains("x")).ToArray();
            //return match.Groups[1].Value.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        }

        var bestMatch = Regex.Match(output, @"\d+[-x]\d+");
        return bestMatch.Success ? new[] { bestMatch.Value } : Array.Empty<string>();
    }
    static long ParseNodes(string output)
    {
        // Ищем "nodes число"
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
        // Ищем "value=число"
        var match = Regex.Match(output, @"value=(-?\d+)");
        if (match.Success) return int.Parse(match.Groups[1].Value);
        return 0;

    }
    static void PrintBoard(int[,] board)
    {
        Console.WriteLine("  +----+----+----+----+----+----+----+----+");
        // Идем сверху вниз (от 7-й строки к 0-й)
        for (int y = 7; y >= 0; y--)
        {
            Console.Write($"{y + 1} |"); // Номер строки
            for (int x = 0; x <= 7; x++)
            {
                int cell = board[x, y];
                int num = GetCellNumber(x, y);
                string content = "";

                bool isWhite = (cell & 1) != 0;
                bool isBlack = (cell & 2) != 0;
                bool isKing = (cell & 8) != 0;

   
                // 1. Если на клетке есть фигура, рисуем её
                if (isWhite)
                    content = isKing ? "W" : " w";
                else if (isBlack)
                    content = isKing ? "B" : " b";


                // 2. Если клетка игровая, но пустая — рисуем её номер

                else if (num > 0) content = num.ToString();
                // 3. Если клетка неиграбельная (белая) — рисуем точку
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
        return 0; // Если клетка не игровая
        
    }
    static int[] damaMap = new int[] {
    0,                                   // индекс 0 не используется
    5,  6,  7,  8,                       // клетки 1-4
    10, 11, 12, 13,                      // клетки 5-8
    14, 15, 16, 17,                      // клетки 9-12
    19, 20, 21, 22,                      // клетки 13-16
    23, 24, 25, 26,                      // клетки 17-20
    28, 29, 30, 31,                      // клетки 21-24
    32, 33, 34, 35,                      // клетки 25-28
    37, 38, 39, 40                       // клетки 29-32
    };
    static int[] krMap = new int[33] {
    0,                                  // не используется
    5,  6,  7,  8,                      // 1-4
    10, 11, 12, 13,                     // 5-8
    14, 15, 16, 17,                     // 9-12
    19, 20, 21, 22,                     // 13-16
    23, 24, 25, 26,                     // 17-20
    28, 29, 30, 31,                     // 21-24
    32, 33, 34, 35,                     // 25-28
    37, 38, 39, 40                      // 29-32
    };

    //public static string GetLegalMoves(int[,] board2D, int color)
    //{
    //    // 1. Создаем массив 46 и заполняем FREE (16) и границами (0)
    //    int[] b46 = new int[46];
    //    for (int i = 0; i < 46; i++) b46[i] = 0; // Граница

    //    // 2. Переносим фигуры из вашей матрицы в массив Cake
    //    for (int i = 1; i <= 32; i++)
    //    {
    //        var pos = cbCoords[i];
    //        int piece = board2D[pos.x, pos.y];

    //        // В Cake: если пусто, пишем 16. Если фигура — ее код (1,2,4,8...)
    //        b46[krMap[i]] = (piece == 0 || piece == 16) ? 16 : piece;
    //    }

    //    // 3. Генерируем список
    //    Move2[] movelist = new Move2[256];
    //    int count = GenerateMoveList(b46, movelist, color);

    //    // 4. Превращаем в нотацию (22-18x11-7)
    //    StringBuilder allMoves = new StringBuilder();
    //    for (int i = 0; i < count; i++)
    //    {
    //        StringBuilder moveStr = new StringBuilder(80);
    //        MoveToNotation(ref movelist[i], moveStr);
    //        allMoves.Append(moveStr.ToString()).Append(" ");
    //    }

    //    return allMoves.ToString().Trim();
    //}
    static IsLegalDelegate _isLegalFunc;
}





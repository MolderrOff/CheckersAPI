using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System.Text;

namespace CheckersBot;

public class EngineWorker : IDisposable
{
    private Process _process;
    private readonly SemaphoreSlim _lock = new(1, 1);
    public bool IsFree => _lock.CurrentCount > 0;
    public bool IsAlive => _process != null && !_process.HasExited;
    public bool IsExited => _process == null || _process.HasExited;

    public EngineWorker(string exePath, string dbPath)
    {
        _process = new Process()
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = dbPath,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        _process.StartInfo.StandardOutputEncoding = System.Text.Encoding.GetEncoding(866);
        _process.StartInfo.WorkingDirectory = Path.GetDirectoryName(exePath);
        _process.ErrorDataReceived += (s, e) => {
            if (!string.IsNullOrEmpty(e.Data))
                System.Diagnostics.Debug.WriteLine($"ENGINE ERR: {e.Data}");
        };

        try
        {
            _process.Start();
            _process.BeginErrorReadLine();
            System.Diagnostics.Debug.WriteLine("Worker warming up...");
            // ПРОГРЕВ: Отправляем стандартную начальную позицию
            string warmUpFen = "W:W21,22,23,24,25,26,27,28,29,30,31,32:B1,2,3,4,5,6,7,8,9,10,11,12";

            // Даем 500мс на инициализацию баз и хеша
            _process.StandardInput.WriteLine($"{warmUpFen}|1|0.5");
            _process.StandardInput.Flush();

            // 2. Ждем ответа RAW_RESULT. Это заставит конструктор "подвиснуть" 
            // до тех пор, пока Kingsrow реально не загрузит базы.
            while (true)
            {
                string line = _process.StandardOutput.ReadLine();
                if (line == null) break;
                if (line.Contains("RAW_RESULT:|")) break; // Базы загружены, движок ответил
            }
            System.Diagnostics.Debug.WriteLine("Worker FULLY warmed up and ready.");
        }
        catch (Exception ex)
        {
            throw new Exception($"Не удалось запустить KingsrowWorker.exe по пути {exePath}. Ошибка: {ex.Message}");
        }

        Console.WriteLine($"Worker started");

    }
   
    public async Task<string> GetBestMoveDirectAsync(string fen, int timeMs, string level = "medium", CancellationToken ct = default)
    {
        Console.WriteLine($"[Worker {this.GetHashCode()}] Ожидаю семафор...");
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        //CancellationTokenRegistration? registration = null;


        var fullOutput = new StringBuilder();
        try
        {
            Console.WriteLine($"[Worker {this.GetHashCode()}] СЕМАФОР ЗАХВАЧЕН. Начинаю расчет.");

            ct.ThrowIfCancellationRequested();
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            


            // Регистрируем колбэк: если токен отменяется, мы "убиваем" процесс KingsRow.
            using var registration = ct.Register(() =>
            {
                if (!IsExited)
                {
                    System.Diagnostics.Debug.WriteLine("CancellationToken fired. Killing engine process.");
                    try { _process.Kill(); } catch (InvalidOperationException) { }
                }
            });

            int color = fen.StartsWith("B", StringComparison.OrdinalIgnoreCase) ? 2 : 1;
            double timeSec = timeMs / 1000.0;
            double engineTime = Math.Max(0.1, (timeMs - 200) / 1000.0);
            string timeStr = timeSec.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);

            string cleanFen = fen.Replace("\r", "").Replace("\n", "").Trim();
            string command = $"{cleanFen}|{color}|{timeStr}|{level}";

            System.Diagnostics.Debug.WriteLine($"SENDING TO ENGINE: {command}");




            ct.ThrowIfCancellationRequested();



            await _process.StandardInput.WriteLineAsync(command).ConfigureAwait(false);
            await _process.StandardInput.FlushAsync();

            string lastResponse = null;

           
            linkedCts.CancelAfter(timeMs);

            while (!linkedCts.IsCancellationRequested)
            {
                string? response = await _process.StandardOutput.ReadLineAsync(linkedCts.Token).ConfigureAwait(false);
                if (response == null) break;

                System.Diagnostics.Debug.WriteLine($"ENGINE RAW: {response}");

                fullOutput.AppendLine(response); 

                
                if (response.Contains("RAW_RESULT:|")) 
                {
                    return fullOutput.ToString();
                }
            }    
        }
        catch (OperationCanceledException e)
        {
             System.Diagnostics.Debug.WriteLine("Таймаут поиска. Возвращаем частичный результат.");
             throw;
        }
        catch (Exception e) 
        {
            Console.WriteLine("Просто исключение" + e.StackTrace); // Посмотри StackTrace
            Console.WriteLine($"Ошибка движка: {e.Message}");                                 // Возможно, здесь тебе тоже нужно бросить или обработать ошибку

        }
        finally
        {
            Console.WriteLine($"[Worker {this.GetHashCode()}] СЕМАФОР ОСВОБОЖДЕН.");
            _lock.Release();
            //registration?.Dispose();
        }
        return fullOutput.ToString();
        //finally
        //{
        //    registration?.Dispose();

        //    // Сначала сохраняем состояние, чтобы не обращаться к _process после пересоздания
        //    bool needsRestart = false;

        //    try
        //    {
        //        // Проверяем на null и на завершение
        //        if (_process == null || _process.HasExited || ct.IsCancellationRequested || linkedCts.IsCancellationRequested)
        //        {
        //            needsRestart = true;
        //        }
        //    }
        //    catch (InvalidOperationException)
        //    {
        //        // Если процесс уже "рассыпался", значит точно нужен рестарт
        //        needsRestart = true;
        //    }

        //    if (needsRestart)
        //    {
        //        System.Diagnostics.Debug.WriteLine("Restarting engine process...");
        //        SetupProcess();
        //    }

        //    _lock.Release();
        //}

    }
    private void SetupProcess()
    {
        var newProcess = new Process();
        newProcess.StartInfo = new ProcessStartInfo
        {
            FileName = "C:\\Projects\\KingsrowWorker\\bin\\x64\\Debug\\net9.0\\KingsrowWorker.exe", // Подставьте ваш путь
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
            WorkingDirectory = "C:\\Projects\\KingsrowWorker\\bin\\x64\\Debug\\net9.0"
        };

        try
        {        
            newProcess.Start();

            string line;
            int maxLines = 50;

            while (maxLines-- > 0 && (line = newProcess.StandardOutput.ReadLine()) != null)
            {
                System.Diagnostics.Debug.WriteLine($"BOOT: {line}");
                if (line.Trim() == "READY") break;
            }

            Thread.Sleep(50);

            var oldProcess = _process;
            _process = newProcess;

            if (oldProcess != null)
            {
                try 
                {
                    if (!oldProcess.HasExited) oldProcess.Kill();
                } 
                catch { }
                oldProcess.Dispose();
            }          
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"CRITICAL ERROR STARTING ENGINE: {ex.Message}");
            throw;
        }
    }
    public async Task<string> SendCommandAsync(string cmd, CancellationToken ct)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await _process.StandardInput.WriteLineAsync(cmd).ConfigureAwait(false);

            string? response = await _process.StandardOutput.ReadLineAsync()
                .WaitAsync(ct)
                .ConfigureAwait(false);
            return response ?? "";
        }
        finally
        {
            _lock.Release(); 
        }
    }
    public void Dispose()
    {
        // Сначала пытаемся корректно завершить процесс
        if (!IsExited)
        {
            try
            {
                _process.Kill(true);
                _process.WaitForExit(1000); // Ждем 1 секунду на завершение
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error killing process: {ex.Message}");
            }
        }
        _process?.Dispose();
        _lock.Dispose();
    }

}


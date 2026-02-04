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
         
            string warmUpFen = "W:W21,22,23,24,25,26,27,28,29,30,31,32:B1,2,3,4,5,6,7,8,9,10,11,12";

            _process.StandardInput.WriteLine($"{warmUpFen}|1|0.5");
            _process.StandardInput.Flush();

            while (true)
            {
                string line = _process.StandardOutput.ReadLine();
                if (line == null) break;
                if (line.Contains("RAW_RESULT:|")) break; 
            }
            System.Diagnostics.Debug.WriteLine("Worker FULLY warmed up and ready.");
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to start KingsrowWorker.exe from path {exePath}. Error: {ex.Message}");
        }

        Console.WriteLine($"Worker started");

    }
   
    public async Task<string> GetBestMoveDirectAsync(string fen, int timeMs, string level = "medium", CancellationToken ct = default)
    {
        Console.WriteLine($"[Worker {this.GetHashCode()}] Waiting for the semaphore...");
        await _lock.WaitAsync(ct).ConfigureAwait(false);
      
        var fullOutput = new StringBuilder();
        try
        {
            Console.WriteLine($"[Worker {this.GetHashCode()}] SEMAPHORE CAPTURED. Counting begins.");

            ct.ThrowIfCancellationRequested();
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            
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
             System.Diagnostics.Debug.WriteLine("Search timed out. Returning partial results..");
             throw;
        }
        catch (Exception e) 
        {
            Console.WriteLine("Just an exception" + e.StackTrace); 
            Console.WriteLine($"Engine error: {e.Message}");      
        }
        finally
        {
            Console.WriteLine($"[Worker {this.GetHashCode()}] SEMAPHORE CLEARED.");
            _lock.Release();
        }
        return fullOutput.ToString();       
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
        if (!IsExited)
        {
            try
            {
                _process.Kill(true);
                _process.WaitForExit(1000); 
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


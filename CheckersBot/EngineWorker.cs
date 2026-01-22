using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Runtime.InteropServices;
using System.Text;

namespace CheckersBot;

public class EngineWorker : IDisposable
{
    private readonly Process _process;
    private readonly SemaphoreSlim _lock = new(1, 1);
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
        }
        catch (Exception ex)
        {
            throw new Exception($"Не удалось запустить KingsrowWorker.exe по пути {exePath}. Ошибка: {ex.Message}");
        }

        Console.WriteLine($"Worker started");

    }
   
    public async Task<string> GetBestMoveDirectAsync(string fen, int timeMs, CancellationToken ct)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);       
        try
        {            
            int color = fen.StartsWith("B") ? 2 : 1;
            double timeSec = timeMs / 1000.0;
            string timeStr = timeSec.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
            
            await _process.StandardInput.WriteLineAsync($"{fen}|{color}|{timeStr}").ConfigureAwait(false);

            string response;

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linkedCts.CancelAfter(timeMs + 500); 

            while ((response = await _process.StandardOutput.ReadLineAsync(linkedCts.Token).ConfigureAwait(false)) != null)
            {
                System.Diagnostics.Debug.WriteLine($"ENGINE RAW: {response}");
                if (string.IsNullOrWhiteSpace(response) || response.Trim() == "READY")
                    continue;

                if (response.Contains("RESULT|") || response.Contains("claims a database draw"))
                {
                    return response;
                }
            }
            
            return "No response from engine (process crashed?)";

        }
        catch (OperationCanceledException)
        {            
            throw;
        }
        finally 
        {
            _lock.Release();
        }
        
    }    
    public async Task<string> SendCommandAsync(string cmd, CancellationToken ct)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await _process.StandardInput.WriteLineAsync(cmd).ConfigureAwait(false);

            string response = await _process.StandardOutput.ReadLineAsync().ConfigureAwait(false);
            return response ?? "";
        }
        finally { _lock.Release(); }

    }
    
    public void Dispose() => _lock.Dispose();
}


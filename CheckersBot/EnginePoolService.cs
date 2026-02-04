using System;
using System.Diagnostics;
using System.Reflection;

namespace CheckersBot;

public class EnginePoolService : IHostedService, IDisposable
{
    private readonly List<EngineWorker> _workers = new();
    private int _nextWorkerIndex = -1;
    private readonly object _lock = new();
    private readonly IConfiguration _config;

    public EnginePoolService(IConfiguration config)
    {
        foreach (var oldProcess in Process.GetProcessesByName("KingsrowWorker")) 
        {
            try { oldProcess.Kill(true); } catch { }
        }

        _config = config;
        string exePath = _config["Engine:Path"] ?? throw new FileNotFoundException("Engine path not found in config.");
        string dbPath = _config["Engine:Databases"] ?? throw new DirectoryNotFoundException("Database path not found in config.");
        int workerCount = _config.GetValue<int>("Engine:Workers", 2); 

        for (int i = 0; i < workerCount; i++)
        {            
            try
            {
                if (!File.Exists(exePath))
                {
                    throw new FileNotFoundException($"EXE not found: {exePath}");
                }               

                var worker = new EngineWorker(exePath, dbPath); 
                _workers.Add(worker);

                Console.WriteLine($"Worker {i} initialized with DB: {dbPath} and warming up."); 
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to start worker {i}: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"CRITICAL: Worker {i} failed: {ex.Message}");
            }
        }

        if (_workers.Count == 0)
            throw new Exception("Critical error: Failed to start any workers Kingsrow!");

        Console.WriteLine($"Pool initialized with {_workers.Count} workers.");
    }
    public async Task StartAsync(CancellationToken cancellationToken)
    {

        Console.WriteLine("--- STARTING DEEP WORKER WARMUP ---");
        var globalSw = Stopwatch.StartNew();

        var warmupTasks = _workers.Select(async (worker, index) =>
        {
            try
            {
                var sw = Stopwatch.StartNew();
                
                await worker.GetBestMoveDirectAsync("B:W21,22,23,24,25,26,27,28,29,30,31,32:B1,2,3,4,5,6,7,8,9,10,11,12", 2000, "medium", cancellationToken);
                Console.WriteLine($"[Worker {index}] Search warmed up in {sw.ElapsedMilliseconds}ms");
                
                sw.Restart();
                var dbResult = await worker.GetBestMoveDirectAsync("W:W21,22,23,24,25,26,27,28:B1", 2000, "strong", cancellationToken);

                bool dbActive = dbResult.Contains("tablebaseHit\":true") || dbResult.Contains("claims");
                Console.WriteLine($"[Worker {index}] DB warmed up in {sw.ElapsedMilliseconds}ms. DB Active: {dbActive}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Worker {index} WARMUP FAILED: {ex.Message}");
            }
        });

        await Task.WhenAll(warmupTasks);
        Console.WriteLine($"--- ALL WORKERS READY. Total warmup time: {globalSw.ElapsedMilliseconds}ms ---");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public List<EngineWorker> Workers => _workers;
    public EngineWorker GetNextWorker()
    {
        if (_workers.Count == 0) throw new Exception("Pool is empty");

        int index = Interlocked.Increment(ref _nextWorkerIndex);
            for (int i = 0; i < _workers.Count; i++)
            {
                var worker = _workers[(index + i) % _workers.Count];
                if (!worker.IsExited)
                {
                    Console.WriteLine($"[POOL] Appointed {worker.GetHashCode()} (Index {(index + i) % _workers.Count})");
                    return worker;
                }                
            }
        //}
        throw new Exception("All Kingsrow workers have completed their work (Crash)!");
    }
    public int GetAliveWorkersCount() => _workers.Count(w => !w.IsExited);
    public void Dispose()
    {
        Console.WriteLine("Disposing EnginePoolService and killing all workers...");
        foreach (var worker in _workers)
        {
            worker.Dispose();
        }
        _workers.Clear();
        Console.WriteLine("All workers terminated.");
    }

}

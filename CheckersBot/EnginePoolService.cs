using System.Diagnostics;

namespace CheckersBot;

public class EnginePoolService
{
    private readonly List<EngineWorker> _workers = new();
    private int _nextWorkerIndex = -1;
    private readonly object _lock = new();
    private readonly IConfiguration _config;

    public EnginePoolService(IConfiguration config)
    {
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
                    throw new FileNotFoundException($"EXE не найден: {exePath}");
                }
               

                var worker = new EngineWorker(exePath, dbPath); 
                _workers.Add(worker);

                _ = worker.GetBestMoveDirectAsync("W:W21,22:B1", 5000, CancellationToken.None);
                Console.WriteLine($"Worker {i} initialized with DB: {dbPath} and warming up."); 
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to start worker {i}: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"CRITICAL: Worker {i} failed: {ex.Message}");
            }
        }

        if (_workers.Count == 0)
            throw new Exception("Критическая ошибка: Не удалось запустить ни одного воркера Kingsrow!");

        Console.WriteLine($"Pool initialized with {_workers.Count} workers.");
    }
    public List<EngineWorker> Workers => _workers;
    public EngineWorker GetNextWorker()
    {
        if (_workers.Count == 0) throw new Exception("Пул пуст");

        for (int i = 0; i < _workers.Count; i++)
        {
            int index = Interlocked.Increment(ref _nextWorkerIndex);
            var worker = _workers[index % _workers.Count];

            if (!worker.IsExited)
            {
                return worker;
            }
            System.Diagnostics.Debug.WriteLine($"WARNING: Worker at index {index % _workers.Count} is dead.");
        }

        throw new Exception("Все воркеры Kingsrow завершили работу (Crash)!");
    }
    public int GetAliveWorkersCount() => _workers.Count(w => !w.IsExited);
    
}

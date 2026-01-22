namespace CheckersBot;

public class EngineResponse
{
    public string Engine { get; set; } = "chinook";
    public string BestMove { get; set; }
    public List<string> Pv { get; set; } = new();
    public int ScoreOrWDL { get; set; }
    public int Depth { get; set; }
    public long Nodes { get; set; }
    public string PositionKey { get; set; }
    public EngineInfo Info { get; set; } = new();
}

public class EngineInfo
{
    public bool TablebaseHit { get; set; }
    public long TimeMs { get; set; }
}

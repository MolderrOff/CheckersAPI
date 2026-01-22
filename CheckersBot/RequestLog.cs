using System.Text.Json.Serialization;

namespace CheckersBot;

public class RequestLog
{
    [JsonPropertyName("requestId")]
    public string RequestId { get; set; }
    [JsonPropertyName("timeMs")]
    public long TimeMs { get; set; }
    [JsonPropertyName("depth")]
    public int Depth { get; set; }
    [JsonPropertyName("nodes")]
    public long Nodes { get; set; }
    [JsonPropertyName("tablebaseHit")]
    public bool TablebaseHit { get; set; }
    [JsonPropertyName("positionKey")]
    public string PositionKey { get; set; }
}
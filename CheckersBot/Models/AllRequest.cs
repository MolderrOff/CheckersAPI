using static CheckersBot.ChinookAdapter;

namespace CheckersBot.Models;

public class SuggestRequest
{
    public string GameId { get; set; } = "checkers-8x8";
    public GameState State { get; set; } = new();
    public string Level { get; set; } = "medium";
    public SearchLimits Limits { get; set; } = new();
}
public class ValidateRequest
{
    public required string Position {  get; set; }
    public required string Move { get; set; }
}
public class GameState
{
    public string Notation { get; set; } = "PDN";
    public string Position { get; set; } = "";
}
using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Text.Json.Serialization; 

namespace CheckersTestClient;

public record State(
    [property: JsonPropertyName("position")] string Position,
    [property: JsonPropertyName("notation")] string Notation = "PDN"
);

public record Limits(
    [property: JsonPropertyName("maxDepth")] int MaxDepth,
    [property: JsonPropertyName("hardTimeMs")] int HardTimeMs
);

public record SuggestRequest(
    [property: JsonPropertyName("gameId")] string GameId,
    [property: JsonPropertyName("state")] State State,
    [property: JsonPropertyName("level")] string Level,
    [property: JsonPropertyName("limits")] Limits Limits
);

public record EngineResponse(
    [property: JsonPropertyName("bestMove")] string Move,
    [property: JsonPropertyName("scoreOrWDL")] int Score,
    [property: JsonPropertyName("depth")] int Depth,
    [property: JsonPropertyName("nodes")] long Nodes
);

class Program
{
    private const string DefaultBaseUrl = "https://localhost:7224";
    private const string DefaultPdn = "W:W21,22,23,24,25,26,27,28,29,30,31,32:B1,2,3,4,5,6,7,8,9,10,11,12";
    private const string DefaultLevel = "weak";
    private const int DefaultMaxDepth = 12;
    private const int DefaultHardTimeMs = 1200;

    static async Task Main(string[] args)
    {
        var handler = new HttpClientHandler();
        handler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true;

        using var client = new HttpClient(handler);
        string selectedBaseUrl = DefaultBaseUrl;

        Console.WriteLine("--- SERVER ADDRESS SELECTION ---");
        Console.WriteLine($"1. Use default address ({DefaultBaseUrl}) , API endpoints: POST /v1/move/suggest");
        Console.WriteLine("2. Enter address manually ");
        Console.Write("Select option (1 or 2): ");
        if (Console.ReadLine() == "2")
        {
            Console.Write("Enter server address: ");
            selectedBaseUrl = Console.ReadLine() ?? DefaultBaseUrl;
        }

        client.BaseAddress = new Uri(selectedBaseUrl.EndsWith("/") ? selectedBaseUrl : selectedBaseUrl + "/");

        while (true)
        {
            Console.WriteLine("\n--- TESTING MENU ---");
            Console.WriteLine("1. Use default test PDN string");
            Console.WriteLine("2. Enter PDN manually");
            Console.WriteLine("3. Run HealthCheck");
            Console.WriteLine("4. Validate Move (v1/move/validate)");
            Console.WriteLine("0. Exit");
            Console.Write("Select action: ");

            string menuChoice = Console.ReadLine();
            if (menuChoice == "0") break;
            if (menuChoice == "3") { await CheckHealth(client); continue; }
            if (menuChoice == "4") { await ValidateMoveRequest(client); continue; }

            string pdn;

            if (menuChoice == "2")
            {
                Console.Write("Enter your PDN string: ");
                pdn = Console.ReadLine() ?? DefaultPdn;
                await SendSuggestRequest(client, pdn, DefaultMaxDepth, DefaultHardTimeMs);

            }
            else
            {
                pdn = DefaultPdn;
                Console.WriteLine($"Using default test string: {pdn}");
                await SendSuggestDefaultRequest(client);
            }
        }
    }

    static StringContent CreateJsonContent(object requestData)
    {
        var options = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
        string jsonRequest = System.Text.Json.JsonSerializer.Serialize(requestData, options);

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n--- REQUEST JSON PAYLOAD ---");
        Console.WriteLine(jsonRequest);
        Console.ResetColor();

        return new StringContent(jsonRequest, System.Text.Encoding.UTF8, "application/json");
    }

    static async Task SendSuggestRequest(HttpClient client, string pdn, int depth, int time)
    {
        Console.WriteLine("\n--- EDIT REQUEST FIELDS (Press Enter for default) ---");

        string Prompt(string label, string defaultValue)
        {
            Console.Write($"{label} [{defaultValue}]: ");
            string input = Console.ReadLine();
            return string.IsNullOrWhiteSpace(input) ? defaultValue : input;
        }

        string gameId = Prompt("gameId", "checkers-8x8");
        string notation = Prompt("notation", "PDN");
        string level = Prompt("level", "weak");

        int softTime = int.Parse(Prompt("softTimeMs", "250"));
        int finalDepth = int.Parse(Prompt("maxDepth", depth.ToString()));
        int finalHardTime = int.Parse(Prompt("hardTimeMs", time.ToString()));

        var requestData = new
        {
            gameId = gameId,
            state = new { position = pdn, notation = notation },
            level = level,
            limits = new
            {
                maxDepth = finalDepth,
                softTimeMs = softTime,
                hardTimeMs = finalHardTime
            }
        };

        Console.WriteLine("\nSending request to v1/move/suggest...");
        try
        {
            var content = CreateJsonContent(requestData);
            var response = await client.PostAsync("v1/move/suggest", content);

            if (response.IsSuccessStatusCode)
            {
                string rawJsonResponse = await response.Content.ReadAsStringAsync();
                var result = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(rawJsonResponse);

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("\n--- FULL JSON RESPONSE ---");
                var options = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
                Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(result, options));
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                string errorBody = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"\nError {(int)response.StatusCode}: {errorBody}");
                Console.ResetColor();
            }
        }
        catch (Exception ex) { Console.WriteLine($"\nError: {ex.Message}"); }
    }
    static async Task SendSuggestDefaultRequest(HttpClient client)
    {
        var requestData = new
        {
            gameId = "checkers-8x8",
            state = new
            {
                notation = "PDN",
                position = "B:W18,19,22,25,27,28,30,32:B1,5,6,7,10,12,14,16"
            },
            level = "weak",
            limits = new
            {
                maxDepth = 12,
                softTimeMs = 250,
                hardTimeMs = 1200
            }
        };

        Console.WriteLine("\n--- SENDING SPECIFIC DEFAULT JSON ---");
        Console.WriteLine($"Target: {client.BaseAddress}v1/move/suggest");

        try
        {
            var content = CreateJsonContent(requestData);
            var response = await client.PostAsync("v1/move/suggest", content);

            if (response.IsSuccessStatusCode)
            {
                string rawJsonResponse = await response.Content.ReadAsStringAsync();
                var result = System.Text.Json.JsonSerializer.Deserialize<object>(rawJsonResponse);
                var options = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("SUCCESS!");
                Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(result, options));
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                string errorBody = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"SERVER ERROR {(int)response.StatusCode}: {errorBody}");
                Console.ResetColor();
            }
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.DarkRed;
            Console.WriteLine($"CONNECTION ERROR: {ex.Message}");
            Console.ResetColor();
        }
    }
    static async Task ValidateMoveRequest(HttpClient client)
    {
        Console.WriteLine("\n--- VALIDATE MOVE ---");

        Console.Write("Enter PDN position: ");
        string pdn = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(pdn)) pdn = DefaultPdn;

        Console.Write("Enter move to validate (e.g., '11-15' or '11x18'): ");
        string move = Console.ReadLine();

        var requestData = new
        {
            gameId = "checkers-8x8",
            position = pdn,
            notation = "PDN",
            move = move
        };

        try
        {
            var content = CreateJsonContent(requestData);
            var response = await client.PostAsync("v1/move/validate", content);

            if (response.IsSuccessStatusCode)
            {
                string raw = await response.Content.ReadAsStringAsync();
                
                using var doc = System.Text.Json.JsonDocument.Parse(raw);
                var root = doc.RootElement;

                if (root.TryGetProperty("legal", out var legalProp))
                {
                    bool isLegal = legalProp.GetBoolean();

                    Console.ForegroundColor = isLegal ? ConsoleColor.Green : ConsoleColor.Red;
                    Console.WriteLine($"\nRESULT: Move is {(isLegal ? "LEGAL" : "ILLEGAL")}");

                    // Если есть причина (на случай illegal), выводим и её
                    if (root.TryGetProperty("reason", out var reasonProp))
                    {
                        Console.WriteLine($"Reason: {reasonProp.GetString()}");
                    }
                    Console.ResetColor();
                }
                else
                {
                    // Если вдруг пришло что-то другое, просто печатаем JSON
                    Console.WriteLine("\n--- RAW RESPONSE ---");
                    Console.WriteLine(raw);
                }
            }
        }
        catch (Exception ex) { HandleConnectionError(ex); }
    }
    static async Task CheckHealth(HttpClient client)
    {
        try
        {
            var response = await client.GetAsync("healthz");
            Console.WriteLine($"Status: {response.StatusCode} | Data: {await response.Content.ReadAsStringAsync()}");
        }
        catch (Exception ex) { Console.WriteLine(ex.Message); }
    }
    static async Task HandleHttpError(HttpResponseMessage response)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        string errorBody = await response.Content.ReadAsStringAsync();
        Console.WriteLine($"\nSERVER ERROR {(int)response.StatusCode}: {errorBody}");
        Console.ResetColor();
    }

    static void HandleConnectionError(Exception ex)
    {
        // 10061 is the socket error code for "Connection Refused"
        if (ex.ToString().Contains("10061") || ex.Message.Contains("connection refused") || ex.Message.Contains("отверг подключение"))
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("\n[!] Databases are warming up, please wait. Try again in a few seconds.");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.DarkRed;
            Console.WriteLine($"\nCRITICAL ERROR: {ex.Message}");
        }
        Console.ResetColor();
    }
}
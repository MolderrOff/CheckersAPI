using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Text.Json.Serialization; // Обязательно!

namespace CheckersTestClient;

// ВСЕ МОДЕЛИ ОБЪЯВЛЕНЫ ОДИН РАЗ ЗДЕСЬ
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
    // ИСПРАВЛЕНО: Убрана лишняя буква W перед 21
    private const string DefaultPdn = "W:W21,22,23,24,25,26,27,28,29,30,31,32:B1,2,3,4,5,6,7,8,9,10,11,12";
    private const string DefaultLevel = "weak";
    private const int DefaultMaxDepth = 250;
    private const int DefaultHardTimeMs = 1200;

    static async Task Main(string[] args)
    {
        // Игнорируем ошибки сертификатов для localhost (частая проблема в .NET)
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
            Console.WriteLine("0. Exit");
            Console.Write("Select action: ");

            string menuChoice = Console.ReadLine();
            if (menuChoice == "0") break;
            if (menuChoice == "3") { await CheckHealth(client); continue; }

            string pdn;
            if (menuChoice == "2")
            {
                Console.Write("Enter your PDN string: ");
                pdn = Console.ReadLine() ?? DefaultPdn; // Если ввели пустоту, возьмет Default
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

    static async Task SendSuggestRequest(HttpClient client, string pdn, int depth, int time)
    {
        Console.WriteLine("\n--- EDIT REQUEST FIELDS (Press Enter for default) ---");

        // Функция-помощник для ввода значений по умолчанию
        string Prompt(string label, string defaultValue)
        {
            Console.Write($"{label} [{defaultValue}]: ");
            string input = Console.ReadLine();
            return string.IsNullOrWhiteSpace(input) ? defaultValue : input;
        }

        // Заполнение полей
        string gameId = Prompt("gameId", "checkers-8x8");
        string notation = Prompt("notation", "PDN");
        string level = Prompt("level", "weak");

        int softTime = int.Parse(Prompt("softTimeMs", "250"));
        int finalDepth = int.Parse(Prompt("maxDepth", depth.ToString()));
        int finalHardTime = int.Parse(Prompt("hardTimeMs", time.ToString()));

        // Формируем объект запроса
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
            var response = await client.PostAsJsonAsync("v1/move/suggest", requestData);

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
        // Тот самый конкретный JSON из твоего вопроса
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
            var response = await client.PostAsJsonAsync("v1/move/suggest", requestData);

            if (response.IsSuccessStatusCode)
            {
                string rawJsonResponse = await response.Content.ReadAsStringAsync();

                // Десериализуем для "красивого" вывода (indentation)
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

    //static async Task SendSuggestRequest(HttpClient client, string pdn, int depth, int time)
    //{
    //    var requestData = new
    //    {
    //        gameId = "checkers-8x8",
    //        state = new { position = pdn, notation = "PDN" },
    //        level = "weak",
    //        limits = new { maxDepth = depth, softTimeMs = 250, hardTimeMs = time }
    //    };

    //    Console.WriteLine("\nSending request...");
    //    try
    //    {
    //        // ИСПРАВЛЕНО: Путь изменен на v1/move/suggest для устранения 404 ошибки
    //        var response = await client.PostAsJsonAsync("v1/move/suggest", requestData);

    //        if (response.IsSuccessStatusCode)
    //        {
    //            // Читаем сырой JSON как строку, чтобы вывести его целиком
    //            string rawJsonResponse = await response.Content.ReadAsStringAsync();

    //            // Также десериализуем, чтобы иметь доступ к типизированным данным
    //            var result = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(rawJsonResponse);

    //            Console.ForegroundColor = ConsoleColor.Green;
    //            Console.WriteLine("--- FULL JSON RESPONSE RECEIVED ---");

    //            // Вывод всего JSON с отступами (красиво)
    //            var options = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
    //            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(result, options));

    //            Console.ResetColor();

    //            // Краткая сводка (опционально)
    //            if (result.TryGetProperty("bestMove", out var move))
    //                Console.WriteLine($"\nQuick Summary -> Best Move: {move}");
    //        }
    //        else
    //        {
    //            Console.ForegroundColor = ConsoleColor.Red;
    //            string errorBody = await response.Content.ReadAsStringAsync();
    //            Console.WriteLine($"Error {(int)response.StatusCode}: {errorBody}");
    //            Console.ResetColor();
    //        }
    //    }
    //    catch (Exception ex) { Console.WriteLine($"Error: {ex.Message}"); }
    //}

    static async Task CheckHealth(HttpClient client)
    {
        try
        {
            var response = await client.GetAsync("healthz");
            Console.WriteLine($"Status: {response.StatusCode} | Data: {await response.Content.ReadAsStringAsync()}");
        }
        catch (Exception ex) { Console.WriteLine(ex.Message); }
    }
}
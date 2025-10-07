using Microsoft.Extensions.Configuration;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

public sealed class GroqJsonClient
{
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly string _model;

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public GroqJsonClient(HttpClient http, IConfiguration cfg)
    {
        _http = http;

        // Accept common env/config keys
        _apiKey =
            cfg["APP:GROQ:API_KEY"]
            ?? cfg["APP__GROQ__API_KEY"]
            ?? cfg["GROQ:API_KEY"]
            ?? cfg["GROQ_API_KEY"]
            ?? Environment.GetEnvironmentVariable("APP__GROQ__API_KEY")
            ?? Environment.GetEnvironmentVariable("GROQ_API_KEY")
            ?? throw new InvalidOperationException("APP__GROQ__API_KEY (or GROQ_API_KEY) is not set.");

        // Allow override via config/env; default to a JSON-capable, fast model
        _model =
            cfg["APP:GROQ:MODEL"]
            ?? cfg["APP__GROQ__MODEL"]
            ?? cfg["GROQ:MODEL"]
            ?? cfg["GROQ_MODEL"]
            ?? Environment.GetEnvironmentVariable("APP__GROQ__MODEL")
            ?? Environment.GetEnvironmentVariable("GROQ_MODEL")
            ?? "gemma2-9b-it";

        // BaseAddress is usually set in Program.cs AddHttpClient<GroqJsonClient>(...), but keep it safe here:
        if (_http.BaseAddress is null)
            _http.BaseAddress = new Uri("https://api.groq.com/openai/v1/");

        _http.Timeout = TimeSpan.FromSeconds(60);
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        _http.DefaultRequestHeaders.Accept.Clear();
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    // ----------------------------------------------------------------
    // PUBLIC API
    // ----------------------------------------------------------------

    /// <summary>
    /// Strict JSON-mode completion (original signature). Deterministic (temperature=0.0).
    /// </summary>
    public async Task<JsonDocument> CompleteJsonAsync(
        string system,
        string user,
        object? data = null,
        CancellationToken ct = default)
        => await CompleteJsonAsync(system, user, data, temperature: 0.0, ct);

    /// <summary>
    /// Strict JSON-mode completion with explicit temperature.
    /// </summary>
    public async Task<JsonDocument> CompleteJsonAsync(
        string system,
        string user,
        object? data,
        double temperature,
        CancellationToken ct = default)
    {
        var strictSystem =
            (system ?? string.Empty).Trim() +
            "\n\nYou MUST return ONLY strict JSON. No explanations, no markdown, no code fences. " +
            "If unsure, return an empty JSON object {}.";

        var req = new
        {
            model = _model,
            temperature = temperature,
            response_format = new { type = "json_object" },
            messages = BuildMessages(strictSystem, user, data)
        };

        using var httpContent = new StringContent(JsonSerializer.Serialize(req, _jsonOpts), Encoding.UTF8, "application/json");
        var resp = await SendWithRetriesAsync(() => _http.PostAsync("chat/completions", httpContent, ct), ct);

        var txt = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"Groq HTTP {(int)resp.StatusCode} {resp.StatusCode}: {txt}");

        using var outer = JsonDocument.Parse(txt);
        var content = outer.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();

        if (string.IsNullOrWhiteSpace(content))
            throw new InvalidOperationException("Groq returned empty content (no assistant message).");

        return EnsureJson(content!);
    }

    /// <summary>
    /// Convenience: send a facts JSON and require exact schema keys; returns JsonObject.
    /// </summary>
    public async Task<JsonObject> CompleteJsonAsync(
        string system,
        JsonObject factsJson,
        IEnumerable<string> schemaKeys,
        double temperature,
        CancellationToken ct = default)
    {
        var keys = string.Join(", ", schemaKeys);
        var user =
            "Using ONLY the provided facts, write concise text and " +
            $"return strict JSON with exactly these keys: {keys}. " +
            "Do NOT invent numbers, dates, or percentages. Keep each field to 2–3 sentences.";

        using var doc = await CompleteJsonAsync(system, user, data: factsJson, temperature: temperature, ct: ct);
        return ToJsonObject(doc);
    }

    /// <summary>
    /// Convenience: tailored for our sales narratives (performance, trends, best_sellers_tips).
    /// </summary>
    public Task<JsonObject> CompleteNarrativesAsync(
        string system,
        JsonObject factsJson,
        double temperature,
        CancellationToken ct = default)
    {
        var keys = new[] { "performance", "trends", "best_sellers_tips" };
        return CompleteJsonAsync(system, factsJson, keys, temperature, ct);
    }

    // ----------------------------------------------------------------
    // INTERNALS
    // ----------------------------------------------------------------

    private static object[] BuildMessages(string system, string user, object? data)
    {
        // If we have tool state / rows, put them as a system message so the model
        // doesn't treat them as a new instruction or ask.
        if (data is null)
        {
            return new object[]
            {
                new { role = "system", content = system },
                new { role = "user",   content = user }
            };
        }

        var toolStateJson = JsonSerializer.Serialize(data, _jsonOpts);
        return new object[]
        {
            new { role = "system", content = system },
            new { role = "system", content = "Tool state:\n" + toolStateJson },
            new { role = "user",   content = user }
        };
    }

    private static JsonDocument EnsureJson(string content)
    {
        var trimmed = content.Trim();

        // Fast path
        if (trimmed.Length > 0 && (trimmed[0] == '{' || trimmed[0] == '['))
        {
            try { return JsonDocument.Parse(trimmed); }
            catch (JsonException ex)
            {
                throw new InvalidOperationException("Model returned malformed JSON.", ex);
            }
        }

        // Rescue attempt: find outermost braces
        try
        {
            var start = content.IndexOf('{');
            var end = content.LastIndexOf('}');
            if (start >= 0 && end > start)
            {
                var slice = content[start..(end + 1)];
                return JsonDocument.Parse(slice);
            }
        }
        catch { /* fall through */ }

        // Hard fail with preview
        var preview = content.Length <= 400 ? content : content[..400] + "…";
        throw new InvalidOperationException("Model did not return JSON. Preview:\n" + preview);
    }

    private static JsonObject ToJsonObject(JsonDocument doc) =>
        JsonNode.Parse(doc.RootElement.GetRawText())!.AsObject();

    private static async Task<HttpResponseMessage> SendWithRetriesAsync(
        Func<Task<HttpResponseMessage>> send,
        CancellationToken ct)
    {
        // Simple exponential backoff for rate limits / transient errors
        var delays = new[] { 0, 400, 1000, 2000 }; // milliseconds
        HttpResponseMessage? last = null;

        for (int attempt = 0; attempt < delays.Length; attempt++)
        {
            if (attempt > 0)
                await Task.Delay(delays[attempt], ct);

            last?.Dispose();
            last = await send();

            if (IsRetryable(last.StatusCode)) continue;
            return last;
        }

        return last!;
    }

    private static bool IsRetryable(HttpStatusCode code) =>
        code == (HttpStatusCode)429 // Too Many Requests
        || code == HttpStatusCode.RequestTimeout
        || code == HttpStatusCode.BadGateway
        || code == HttpStatusCode.ServiceUnavailable
        || code == HttpStatusCode.GatewayTimeout;
}

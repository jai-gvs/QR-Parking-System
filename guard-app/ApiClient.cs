using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace BcpGuardApp;

/// <summary>Error from the server (or from reaching it). Message is safe to show to the guard.</summary>
public class ApiException : Exception
{
    public HttpStatusCode? StatusCode { get; }
    public ApiException(string message, HttpStatusCode? statusCode = null) : base(message) => StatusCode = statusCode;
}

/// <summary>Small helpers for reading the JSON the server sends back.</summary>
public static class J
{
    public static string Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? "" : "";

    public static double Num(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetDouble() : 0;

    public static JsonElement Obj(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) ? v : default;
}

/// <summary>Talks to the BCP parking API (Python FastAPI server on port 8000).</summary>
public class ApiClient
{
    private HttpClient? _http;

    public string Username { get; private set; } = "";
    public string Name { get; private set; } = "";
    public string Role { get; private set; } = "";

    // ---------------- login ----------------
    public async Task LoginAsync(string serverUrl, string username, string password)
    {
        if (!Uri.TryCreate(serverUrl.Trim().TrimEnd('/') + "/", UriKind.Absolute, out var baseUri) ||
            (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
            throw new ApiException("The server address is not valid. Example: http://127.0.0.1:8000");

        _http?.Dispose();
        _http = new HttpClient { BaseAddress = baseUri, Timeout = TimeSpan.FromSeconds(10) };

        var json = await SendAsync(HttpMethod.Post, "api/login", new { username, password });

        var token = J.Str(json, "access_token");
        var user = J.Obj(json, "user");
        if (token.Length == 0) throw new ApiException("The server sent an unexpected reply.");

        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        Username = J.Str(user, "username");
        Name = J.Str(user, "name");
        Role = J.Str(user, "role");
    }

    // ---------------- parking ----------------
    public Task<JsonElement> GetSlotsAsync() => SendAsync(HttpMethod.Get, "api/slots");

    public Task<JsonElement> PrepareEntryAsync(string qr) =>
        SendAsync(HttpMethod.Post, "api/parking/entry/prepare", new { qrToken = qr });

    public Task<JsonElement> PayAndEnterAsync(string qr, double amount) =>
        SendAsync(HttpMethod.Post, "api/parking/payment", new { qrToken = qr, amount });

    public Task<JsonElement> PrepareExitAsync(string qr) =>
        SendAsync(HttpMethod.Post, "api/parking/exit/prepare", new { qrToken = qr });

    public Task<JsonElement> ExitAsync(string qr) =>
        SendAsync(HttpMethod.Post, "api/parking/exit", new { qrToken = qr });

    // ---------------- core request ----------------
    private async Task<JsonElement> SendAsync(HttpMethod method, string url, object? body = null)
    {
        if (_http is null) throw new ApiException("Not connected to the server.");

        try
        {
            using var req = new HttpRequestMessage(method, url);
            if (body is not null) req.Content = JsonContent.Create(body);

            using var res = await _http.SendAsync(req);
            var text = await res.Content.ReadAsStringAsync();

            JsonElement json = default;
            if (!string.IsNullOrWhiteSpace(text))
            {
                try
                {
                    using var doc = JsonDocument.Parse(text);
                    json = doc.RootElement.Clone();
                }
                catch (JsonException) { /* not JSON - handled below */ }
            }

            if (!res.IsSuccessStatusCode)
            {
                var detail = $"The server returned an error ({(int)res.StatusCode}).";
                if (json.ValueKind == JsonValueKind.Object &&
                    json.TryGetProperty("detail", out var d) && d.ValueKind == JsonValueKind.String)
                    detail = d.GetString() ?? detail;
                throw new ApiException(detail, res.StatusCode);
            }

            return json;
        }
        catch (HttpRequestException)
        {
            throw new ApiException("Cannot reach the server. Is start-server.bat running?");
        }
        catch (TaskCanceledException)
        {
            throw new ApiException("The server took too long to answer.");
        }
    }
}

using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace PiPiClaw.Team;

/// <summary>
/// Shared helpers for all API handlers.
/// </summary>
internal static class ApiHelpers
{
    public static async Task WriteJsonAsync<T>(HttpListenerResponse res, T value, JsonTypeInfo<T> typeInfo)
    {
        res.ContentType = "application/json; charset=utf-8";
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, typeInfo));
        res.ContentLength64 = bytes.Length;
        await res.OutputStream.WriteAsync(bytes);
    }

    public static async Task WriteErrorAsync(HttpListenerResponse res, string message, int statusCode = 400)
    {
        res.StatusCode = statusCode;
        res.ContentType = "application/json; charset=utf-8";
        var json = $"{{\"error\":{JsonSerializer.Serialize(message, AppJsonContext.Default.String)}}}";
        var bytes = Encoding.UTF8.GetBytes(json);
        res.ContentLength64 = bytes.Length;
        await res.OutputStream.WriteAsync(bytes);
    }

    public static async Task<string> ReadBodyAsync(HttpListenerRequest req)
    {
        using var reader = new StreamReader(req.InputStream, Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }

    public static bool IncludeTestData(HttpListenerRequest req)
    {
        var value = req.QueryString["include_test_data"] ?? req.QueryString["includeTestData"];
        return value is "1" or "true" or "yes";
    }

    public static bool AllowDevelopmentEndpoints()
    {
        var value = Environment.GetEnvironmentVariable("WORLDCUP_ENABLE_DEV_ENDPOINTS")
            ?? Environment.GetEnvironmentVariable("PIPICLAW_ENABLE_DEV_ENDPOINTS");
        return value is "1" or "true" or "yes";
    }
}

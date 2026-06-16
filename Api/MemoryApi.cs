using System.Net;
using System.Text;
using System.Text.Json;

namespace PiPiClaw.Team;

/// <summary>
/// 记忆系统 API。
/// </summary>
public static class MemoryApi
{
    public static async Task<bool> TryHandleAsync(HttpListenerContext ctx)
    {
        var req = ctx.Request;
        var res = ctx.Response;
        string path = req.Url?.AbsolutePath.ToLower() ?? "/";
        var store = AppContext.WorldCupStore;

        if (path == "/api/memories" && req.HttpMethod == "GET")
        {
            var objectId = req.QueryString["object_id"];
            var ownerId = req.QueryString["owner_id"];
            var items = store.GetMemories(objectId, ownerId);
            await ApiHelpers.WriteJsonAsync(res, items, AppJsonContext.Default.ListMemoryRecord);
            return true;
        }
        if (path == "/api/memories" && req.HttpMethod == "POST")
        {
            using var reader = new StreamReader(req.InputStream, Encoding.UTF8);
            var body = await reader.ReadToEndAsync();
            var request = JsonSerializer.Deserialize(body, AppJsonContext.Default.MemoryCreateRequest);
            if (request == null || string.IsNullOrWhiteSpace(request.Content)) { res.StatusCode = 400; await ApiHelpers.WriteErrorAsync(res, "content is required"); return true; }
            var item = store.AddMemory(request);
            await ApiHelpers.WriteJsonAsync(res, item, AppJsonContext.Default.MemoryRecord);
            return true;
        }

        return false;
    }

}

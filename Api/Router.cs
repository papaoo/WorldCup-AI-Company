using System.Net;
using System.Text;

namespace PiPiClaw.Team;

/// <summary>
/// Main HTTP router. Handlers are tried in priority order; unmatched requests fall back to static files or 404.
/// </summary>
public static class Router
{
    public static async Task RouteAsync(HttpListenerContext ctx)
    {
        if (await ProductBffApi.TryHandleAsync(ctx)) return;
        if (await BffApi.TryHandleAsync(ctx)) return;
        if (await WorldCupApi.TryHandleAsync(ctx)) return;
        if (await EntityApi.TryHandleAsync(ctx)) return;
        if (await MemoryApi.TryHandleAsync(ctx)) return;
        if (await DataSourceApi.TryHandleAsync(ctx)) return;
        if (await IntelligenceApi.TryHandleAsync(ctx)) return;
        if (await HarnessApi.TryHandleAsync(ctx)) return;

        if (await BossMarketApi.TryHandleAsync(ctx)) return;
        if (await LegacyCompanyApi.TryHandleAsync(ctx)) return;

        await ServeStaticAsync(ctx);
    }

    private static async Task ServeStaticAsync(HttpListenerContext ctx)
    {
        var req = ctx.Request;
        var res = ctx.Response;
        string path = req.Url?.AbsolutePath.ToLower() ?? "/";

        if (path == "/" || path == "/index.html")
        {
            res.ContentType = "text/html; charset=utf-8";
            byte[] buffer = Encoding.UTF8.GetBytes(LegacyOfficeUI.HtmlContent);
            res.ContentLength64 = buffer.Length;
            await res.OutputStream.WriteAsync(buffer);
        }
        else if (path.EndsWith(".png") || path.EndsWith(".jpeg") || path.EndsWith(".jpg"))
        {
            string resourceName = $"PiPiClaw.Team.{path.Substring(1)}";
            using var stream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
            if (stream != null)
            {
                res.ContentType = path.EndsWith(".png") ? "image/png" : "image/jpeg";
                res.ContentLength64 = stream.Length;
                await stream.CopyToAsync(res.OutputStream);
            }
        }
        else
        {
            res.StatusCode = 404;
        }
    }
}

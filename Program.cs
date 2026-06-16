using System.Net;
using System.Text;
using System.Text.Json;

namespace PiPiClaw.Team;

class Program
{
    static async Task Main(string[] args)
    {
        const string url = "http://localhost:4050/";
        using var listener = new HttpListener();
        listener.Prefixes.Add(url);

        try
        {
            if (File.Exists(AppContext.ConfigPath))
            {
                try
                {
                    var json = File.ReadAllText(AppContext.ConfigPath, Encoding.UTF8);
                    var loadedConfig = JsonSerializer.Deserialize<AppConfig>(json, AppJsonContext.Default.AppConfig);
                    if (loadedConfig != null) AppContext.Config = loadedConfig;
                }
                catch
                {
                    // Continue with defaults if local config is malformed.
                }
            }

            AppContext.WorldCupStore.Initialize();
            AutoCollectionService.EnsureDefaultAutoCollectionConfig();
            _ = Task.Run(AutoCollectionService.RunAutoCollectionLoopAsync);

            listener.Start();
            Console.WriteLine($"[系统] PiPiClaw.Team control server started: {url}");
        }
        catch (HttpListenerException ex)
        {
            Console.WriteLine($"Startup failed. Try running with administrator privileges. Error: {ex.Message}");
            return;
        }

        while (true)
        {
            var context = await listener.GetContextAsync();
            _ = Task.Run(() => HandleRequestAsync(context));
        }
    }

    private static async Task HandleRequestAsync(HttpListenerContext ctx)
    {
        var res = ctx.Response;
        try
        {
            await Router.RouteAsync(ctx);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[错误] Request handling failed: {ex.Message}");
            await ApiHelpers.WriteErrorAsync(res, ex.Message, ex is ArgumentException ? 400 : 500);
        }
        finally
        {
            res.Close();
        }
    }
}

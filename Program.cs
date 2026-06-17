using System.Net;
using System.Text;
using System.Text.Json;

namespace PiPiClaw.Team;

class Program
{
    static async Task Main(string[] args)
    {
        var url = ResolveListenUrl(args);
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

    private static string ResolveListenUrl(string[] args)
    {
        var cliUrl = ReadCliUrl(args);
        var configuredUrl = cliUrl
            ?? Environment.GetEnvironmentVariable("WORLDCUP_URLS")
            ?? Environment.GetEnvironmentVariable("PIPICLAW_URLS")
            ?? Environment.GetEnvironmentVariable("ASPNETCORE_URLS")
            ?? "http://localhost:4050/";

        var firstUrl = configuredUrl.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? "http://localhost:4050/";
        return firstUrl.EndsWith("/", StringComparison.Ordinal) ? firstUrl : $"{firstUrl}/";
    }

    private static string? ReadCliUrl(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg.Equals("--urls", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                return args[i + 1];
            }

            if (arg.StartsWith("--urls=", StringComparison.OrdinalIgnoreCase))
            {
                return arg["--urls=".Length..];
            }
        }

        return null;
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

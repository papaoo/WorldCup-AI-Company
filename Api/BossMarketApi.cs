using System.Net;
using System.Text;
using System.Text.Json;

namespace PiPiClaw.Team;

/// <summary>
/// BOSS 人才市场 API：人才列表、公司注册、员工上传/录用/下架。
/// </summary>
public static class BossMarketApi
{
    public static async Task<bool> TryHandleAsync(HttpListenerContext ctx)
    {
        var req = ctx.Request;
        var res = ctx.Response;
        string path = req.Url?.AbsolutePath.ToLower() ?? "/";
        var httpClient = AppContext.HttpClient;
        var config = AppContext.Config;

        if (!path.StartsWith("/api/boss/")) return false;

        // GET /api/boss/list
        if (path == "/api/boss/list" && req.HttpMethod == "GET")
        {
            try
            {
                using var proxyReq = new HttpRequestMessage(HttpMethod.Get, AppContext.BossMarketUrl + "/api/list");
                using var proxyRes = await httpClient.SendAsync(proxyReq);
                proxyRes.EnsureSuccessStatusCode();
                var responseBytes = await proxyRes.Content.ReadAsByteArrayAsync();
                res.ContentType = "application/json; charset=utf-8";
                res.ContentLength64 = responseBytes.Length;
                await res.OutputStream.WriteAsync(responseBytes);
            }
            catch { res.StatusCode = 500; }
            return true;
        }

        // POST /api/boss/register
        if (path == "/api/boss/register" && req.HttpMethod == "POST")
        {
            using var reader = new StreamReader(req.InputStream);
            var bodyJson = JsonDocument.Parse(await reader.ReadToEndAsync()).RootElement;
            string companyName = bodyJson.GetProperty("companyName").GetString() ?? "";
            if (string.IsNullOrWhiteSpace(companyName)) { res.StatusCode = 400; return true; }

            try
            {
                var regReq = new HttpRequestMessage(HttpMethod.Post, AppContext.BossMarketUrl + "/api/register");
                regReq.Content = new StringContent($"{{\"companyName\":\"{companyName}\"}}", Encoding.UTF8, "application/json");
                var regRes = await httpClient.SendAsync(regReq);
                config.CompanyName = companyName;
                if (regRes.IsSuccessStatusCode)
                {
                    config.HasLicense = true;
                    File.WriteAllText(AppContext.ConfigPath, JsonSerializer.Serialize(config, AppJsonContext.Default.AppConfig), Encoding.UTF8);
                    res.ContentType = "application/json; charset=utf-8";
                    await res.OutputStream.WriteAsync(Encoding.UTF8.GetBytes("{\"status\":\"ok\"}"));
                }
                else throw new Exception("BOSS 服务器拒绝注册");
            }
            catch
            {
                config.HasLicense = false;
                File.WriteAllText(AppContext.ConfigPath, JsonSerializer.Serialize(config, AppJsonContext.Default.AppConfig), Encoding.UTF8);
                res.StatusCode = 500;
            }
            return true;
        }

        // POST /api/boss/upload
        if (path == "/api/boss/upload" && req.HttpMethod == "POST")
        {
            string username = Uri.UnescapeDataString(req.Headers["X-Username"] ?? "");
            string teamId = string.IsNullOrWhiteSpace(config.CompanyName) ? "无执照公司" : config.CompanyName;

            if (config.PeerNodes.TryGetValue(username, out var nodeInfo) && !string.IsNullOrEmpty(nodeInfo.Url))
            {
                try
                {
                    var exportUrl = nodeInfo.Url.TrimEnd('/') + "/api/export";
                    using var exportReq = new HttpRequestMessage(HttpMethod.Get, exportUrl);
                    exportReq.Headers.Add("X-Username", Uri.EscapeDataString(username));
                    using var exportRes = await httpClient.SendAsync(exportReq, HttpCompletionOption.ResponseHeadersRead);
                    exportRes.EnsureSuccessStatusCode();

                    var uploadReq = new HttpRequestMessage(HttpMethod.Post, AppContext.BossMarketUrl + "/api/upload");
                    uploadReq.Headers.Add("X-Agent-Profile", exportRes.Headers.GetValues("X-Agent-Profile").First());
                    uploadReq.Headers.Add("X-Team-Id", Uri.EscapeDataString(teamId));
                    uploadReq.Content = new StreamContent(await exportRes.Content.ReadAsStreamAsync());
                    uploadReq.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/zip");
                    using var uploadRes = await httpClient.SendAsync(uploadReq);
                    uploadRes.EnsureSuccessStatusCode();
                    res.StatusCode = 200;
                }
                catch (Exception ex) { Console.WriteLine(ex.Message); res.StatusCode = 500; }
            }
            return true;
        }

        // POST /api/boss/hire
        if (path == "/api/boss/hire" && req.HttpMethod == "POST")
        {
            using var reader = new StreamReader(req.InputStream);
            var bodyJson = JsonDocument.Parse(await reader.ReadToEndAsync()).RootElement;
            string targetId = bodyJson.GetProperty("id").GetString() ?? "";
            string targetName = bodyJson.GetProperty("name").GetString() ?? "";
            string targetNodeUrl = bodyJson.GetProperty("nodeUrl").GetString() ?? "";

            try
            {
                var dlReq = new HttpRequestMessage(HttpMethod.Get, $"{AppContext.BossMarketUrl}/api/download?id={Uri.EscapeDataString(targetId)}");
                using var dlRes = await httpClient.SendAsync(dlReq, HttpCompletionOption.ResponseHeadersRead);
                dlRes.EnsureSuccessStatusCode();

                var importReq = new HttpRequestMessage(HttpMethod.Post, targetNodeUrl.TrimEnd('/') + "/api/import");
                importReq.Headers.Add("X-Username", Uri.EscapeDataString(targetName));
                importReq.Content = new StreamContent(await dlRes.Content.ReadAsStreamAsync());
                importReq.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/zip");
                using var importRes = await httpClient.SendAsync(importReq);
                importRes.EnsureSuccessStatusCode();

                var listRes = await httpClient.GetStringAsync(AppContext.BossMarketUrl + "/api/list");
                var allAgents = JsonSerializer.Deserialize(listRes, AppJsonContext.Default.ListJsonElement);
                var agent = allAgents.FirstOrDefault(a => a.GetProperty("Id").GetString() == targetId);

                config.PeerNodes[targetName] = new NodeInfo
                {
                    Name = targetName,
                    Url = targetNodeUrl,
                    Role = agent.GetProperty("Role").GetString() ?? "新员工",
                    Description = agent.GetProperty("Description").GetString() ?? "",
                    Resume = agent.GetProperty("Resume").GetString() ?? "",
                    ModelIndex = agent.GetProperty("ModelIndex").GetInt32()
                };
                File.WriteAllText(AppContext.ConfigPath, JsonSerializer.Serialize(config, AppJsonContext.Default.AppConfig), Encoding.UTF8);
                await CompanySetupService.SyncPeerNodesToMasterAsync();
                res.StatusCode = 200;
            }
            catch { res.StatusCode = 500; }
            return true;
        }

        // POST /api/boss/delete
        if (path == "/api/boss/delete" && req.HttpMethod == "POST")
        {
            using var reader = new StreamReader(req.InputStream);
            var bodyJson = JsonDocument.Parse(await reader.ReadToEndAsync()).RootElement;
            string targetId = bodyJson.GetProperty("id").GetString() ?? "";
            string teamId = string.IsNullOrWhiteSpace(config.CompanyName) ? "无执照公司" : config.CompanyName;

            try
            {
                var delReq = new HttpRequestMessage(HttpMethod.Post, AppContext.BossMarketUrl + "/api/delete");
                delReq.Headers.Add("X-Team-Id", Uri.EscapeDataString(teamId));
                delReq.Content = new StringContent($"{{\"id\":\"{targetId}\"}}", Encoding.UTF8, "application/json");
                using var delRes = await httpClient.SendAsync(delReq);

                if (delRes.IsSuccessStatusCode)
                {
                    res.StatusCode = 200;
                    await res.OutputStream.WriteAsync(Encoding.UTF8.GetBytes("{\"status\":\"ok\"}"));
                }
                else if (delRes.StatusCode == HttpStatusCode.Forbidden)
                {
                    res.StatusCode = 403;
                }
                else { res.StatusCode = 500; }
            }
            catch { res.StatusCode = 500; }
            return true;
        }

        return false;
    }
}

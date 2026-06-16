using System.Text;
using System.Text.Json;

namespace PiPiClaw.Team;

/// <summary>
/// Company setup helpers for synchronizing peer nodes and parsing LLM setup output.
/// </summary>
public static class CompanySetupService
{
    public static async Task SyncPeerNodesToMasterAsync()
    {
        var config = AppContext.Config;
        var httpClient = AppContext.HttpClient;
        string targetUrl = string.IsNullOrEmpty(config.MasterNodeUrl) ? "http://127.0.0.1:5050" : config.MasterNodeUrl;
        try
        {
            var getReq = new HttpRequestMessage(HttpMethod.Get, targetUrl.TrimEnd('/') + "/api/config");
            using var getRes = await httpClient.SendAsync(getReq);
            if (!getRes.IsSuccessStatusCode) return;

            var masterCfgStr = await getRes.Content.ReadAsStringAsync();
            var masterCfgDict = JsonSerializer.Deserialize(masterCfgStr, AppJsonContext.Default.DictionaryStringJsonElement) ?? new();

            masterCfgDict["PeerNodes"] = JsonSerializer.SerializeToElement(config.PeerNodes, AppJsonContext.Default.DictionaryStringNodeInfo);

            var postReq = new HttpRequestMessage(HttpMethod.Post, targetUrl.TrimEnd('/') + "/api/config");
            postReq.Content = new StringContent(JsonSerializer.Serialize(masterCfgDict, AppJsonContext.Default.DictionaryStringJsonElement), Encoding.UTF8, "application/json");
            using var postRes = await httpClient.SendAsync(postReq);

            Console.WriteLine($"[强绑定] PeerNodes synchronized to master node {targetUrl}; peers={config.PeerNodes.Count}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[强绑定失败] Master node is offline or misconfigured: {ex.Message}");
        }
    }

    public static CompanySetupResult? ParseCompanySetupResult(string modelOutput)
    {
        var cleaned = modelOutput
            .Replace("```json", "", StringComparison.OrdinalIgnoreCase)
            .Replace("```", "")
            .Trim();

        if (cleaned.StartsWith('"'))
        {
            try
            {
                cleaned = JsonSerializer.Deserialize(cleaned, AppJsonContext.Default.String) ?? cleaned;
            }
            catch { }
        }

        int startIndex = cleaned.IndexOf('{');
        int endIndex = cleaned.LastIndexOf('}');
        if (startIndex >= 0 && endIndex > startIndex)
        {
            cleaned = cleaned.Substring(startIndex, endIndex - startIndex + 1);
        }

        if (cleaned.Contains("\\\"") && !cleaned.Contains("\"Profile\""))
        {
            cleaned = cleaned.Replace("\\\"", "\"");
        }

        return JsonSerializer.Deserialize(cleaned, typeof(CompanySetupResult), AppJsonContext.Default) as CompanySetupResult;
    }
}

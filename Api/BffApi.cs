using System.Net;

namespace PiPiClaw.Team;

/// <summary>
/// UI-oriented aggregate APIs for the World Cup company frontend.
/// </summary>
public static class BffApi
{
    public static async Task<bool> TryHandleAsync(HttpListenerContext ctx)
    {
        var req = ctx.Request;
        var res = ctx.Response;
        string path = req.Url?.AbsolutePath.ToLower() ?? "/";
        var store = AppContext.WorldCupStore;

        if (path == "/api/worldcup/company-dashboard" && req.HttpMethod == "GET")
        {
            var limit = int.TryParse(req.QueryString["activity_limit"], out var parsedLimit) ? parsedLimit : 20;
            var result = store.GetCompanyDashboard(limit, includeTestData: ApiHelpers.IncludeTestData(req));
            await ApiHelpers.WriteJsonAsync(res, result, AppJsonContext.Default.WorldCupCompanyDashboardResult);
            return true;
        }

        if (path == "/api/worldcup/operations/summary" && req.HttpMethod == "GET")
        {
            var result = store.GetOperationsSummary(includeTestData: ApiHelpers.IncludeTestData(req));
            await ApiHelpers.WriteJsonAsync(res, result, AppJsonContext.Default.WorldCupOperationsSummary);
            return true;
        }

        if (path == "/api/worldcup/employees/status-summary" && req.HttpMethod == "GET")
        {
            var result = store.GetEmployeeStatusSummary(includeTestData: ApiHelpers.IncludeTestData(req));
            await ApiHelpers.WriteJsonAsync(res, result, AppJsonContext.Default.WorldCupEmployeeStatusSummaryResult);
            return true;
        }

        if (path == "/api/worldcup/predictions/accuracy" && req.HttpMethod == "GET")
        {
            var result = store.GetPredictionAccuracy(includeTestData: ApiHelpers.IncludeTestData(req));
            await ApiHelpers.WriteJsonAsync(res, result, AppJsonContext.Default.WorldCupPredictionAccuracyResult);
            return true;
        }

        if (path == "/api/worldcup/match-board" && req.HttpMethod == "GET")
        {
            var limit = int.TryParse(req.QueryString["limit"], out var parsedLimit) ? parsedLimit : 300;
            var result = store.GetMatchBoard(
                req.QueryString["stage"],
                req.QueryString["group"],
                req.QueryString["status"],
                limit,
                includeTestData: ApiHelpers.IncludeTestData(req));
            await ApiHelpers.WriteJsonAsync(res, result, AppJsonContext.Default.WorldCupMatchBoardResult);
            return true;
        }

        if (path == "/api/worldcup/activity-feed" && req.HttpMethod == "GET")
        {
            var limit = int.TryParse(req.QueryString["limit"], out var parsedLimit) ? parsedLimit : 50;
            var result = store.GetActivityFeed(limit, includeTestData: ApiHelpers.IncludeTestData(req));
            await ApiHelpers.WriteJsonAsync(res, result, AppJsonContext.Default.ListWorldCupActivityFeedItem);
            return true;
        }

        if (path == "/api/worldcup/llm-usage" && req.HttpMethod == "GET")
        {
            var result = store.GetLlmUsageSummary();
            await ApiHelpers.WriteJsonAsync(res, result, AppJsonContext.Default.WorldCupLlmUsageSummary);
            return true;
        }

        if (path == "/api/worldcup/model-gateway-health" && req.HttpMethod == "GET")
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var targetUrl = string.IsNullOrWhiteSpace(AppContext.Config.MasterNodeUrl)
                ? "http://127.0.0.1:5050"
                : AppContext.Config.MasterNodeUrl;
            var result = await AppContext.LlmGateway.CheckHealthAsync(targetUrl, timeout.Token);
            await ApiHelpers.WriteJsonAsync(res, result, AppJsonContext.Default.WorldCupModelGatewayHealthResult);
            return true;
        }

        if (path == "/api/memories/summary" && req.HttpMethod == "GET")
        {
            var result = store.GetMemorySummary(req.QueryString["object_id"], req.QueryString["owner_id"]);
            await ApiHelpers.WriteJsonAsync(res, result, AppJsonContext.Default.MemorySummaryResult);
            return true;
        }

        return false;
    }
}

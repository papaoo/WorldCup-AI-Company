using System.Net;

namespace PiPiClaw.Team;

/// <summary>
/// Intelligence APIs: signal triage, employee report triggers, and system event logs.
/// </summary>
public static class IntelligenceApi
{
    public static async Task<bool> TryHandleAsync(HttpListenerContext ctx)
    {
        var req = ctx.Request;
        var res = ctx.Response;
        string path = req.Url?.AbsolutePath.ToLower() ?? "/";
        var store = AppContext.WorldCupStore;

        if (path == "/api/worldcup/intelligence-signals" && req.HttpMethod == "GET")
        {
            var limit = int.TryParse(req.QueryString["limit"], out var parsedLimit) ? parsedLimit : 200;
            var items = ApiHelpers.IncludeTestData(req)
                ? store.GetIntelligenceSignals(
                    req.QueryString["status"],
                    req.QueryString["object_id"],
                    req.QueryString["match_id"],
                    req.QueryString["signal_type"],
                    limit)
                : store.GetProductionIntelligenceSignals(
                    req.QueryString["status"],
                    req.QueryString["object_id"],
                    req.QueryString["match_id"],
                    req.QueryString["signal_type"],
                    limit);
            await ApiHelpers.WriteJsonAsync(res, items, AppJsonContext.Default.ListIntelligenceSignalRecord);
            return true;
        }
        if (path == "/api/worldcup/intelligence-triage" && req.HttpMethod == "POST")
        {
            var limit = int.TryParse(req.QueryString["snapshot_limit"], out var parsedLimit) ? parsedLimit : 500;
            var result = store.RunIntelligenceTriage(limit, includeTestData: ApiHelpers.IncludeTestData(req));
            await ApiHelpers.WriteJsonAsync(res, result, AppJsonContext.Default.IntelligenceTriageResult);
            return true;
        }
        if (path == "/api/worldcup/employee-report-trigger" && req.HttpMethod == "POST")
        {
            var maxTeams = int.TryParse(req.QueryString["max_teams"], out var parsedMaxTeams) ? parsedMaxTeams : 8;
            var result = store.TriggerEmployeeReportsFromSignals(maxTeams, includeTestData: ApiHelpers.IncludeTestData(req));
            await ApiHelpers.WriteJsonAsync(res, result, AppJsonContext.Default.EmployeeReportTriggerResult);
            return true;
        }
        if (path == "/api/worldcup/employee-report-budget" && req.HttpMethod == "GET")
        {
            var maxTeams = int.TryParse(req.QueryString["max_teams"], out var parsedMaxTeams) ? parsedMaxTeams : 8;
            var result = store.EstimateEmployeeReportBudget(maxTeams);
            await ApiHelpers.WriteJsonAsync(res, result, AppJsonContext.Default.EmployeeReportBudgetEstimate);
            return true;
        }
        if (path == "/api/worldcup/team-intelligence-llm-report" && req.HttpMethod == "POST")
        {
            var maxSignals = int.TryParse(req.QueryString["max_signals"], out var parsedMaxSignals) ? parsedMaxSignals : 8;
            var result = await TeamIntelligenceLlmReportService.RunAsync(req.QueryString["object_id"], maxSignals);
            await ApiHelpers.WriteJsonAsync(res, result, AppJsonContext.Default.TeamIntelligenceLlmReportResult);
            return true;
        }
        if (path == "/api/worldcup/auto-llm-report-run" && req.HttpMethod == "POST")
        {
            var maxTeams = int.TryParse(req.QueryString["max_teams"], out var parsedMaxTeams) ? parsedMaxTeams : 2;
            var maxSignals = int.TryParse(req.QueryString["max_signals"], out var parsedMaxSignals) ? parsedMaxSignals : 8;
            var result = await TeamIntelligenceLlmReportService.RunBatchAsync(maxTeams, maxSignals);
            AppContext.LastAutoLlmReportRun = result;
            await ApiHelpers.WriteJsonAsync(res, result, AppJsonContext.Default.AutoLlmReportRunResult);
            return true;
        }
        if (path == "/api/worldcup/team-workbench" && req.HttpMethod == "GET")
        {
            var objectId = req.QueryString["object_id"];
            if (string.IsNullOrWhiteSpace(objectId)) { res.StatusCode = 400; await ApiHelpers.WriteErrorAsync(res, "object_id is required"); return true; }
            var limit = int.TryParse(req.QueryString["limit"], out var parsedLimit) ? parsedLimit : 20;
            var result = store.GetTeamWorkbench(objectId, limit, includeTestData: ApiHelpers.IncludeTestData(req));
            await ApiHelpers.WriteJsonAsync(res, result, AppJsonContext.Default.TeamWorkbenchResult);
            return true;
        }
        if (path == "/api/worldcup/team-context-pack" && req.HttpMethod == "GET")
        {
            var objectId = req.QueryString["object_id"];
            if (string.IsNullOrWhiteSpace(objectId)) { res.StatusCode = 400; await ApiHelpers.WriteErrorAsync(res, "object_id is required"); return true; }
            var maxEvidence = int.TryParse(req.QueryString["max_evidence"], out var parsedMaxEvidence) ? parsedMaxEvidence : 12;
            var result = store.BuildTeamIntelligenceContextPack(objectId, maxEvidence, includeTestData: ApiHelpers.IncludeTestData(req));
            await ApiHelpers.WriteJsonAsync(res, result, AppJsonContext.Default.TeamIntelligenceContextPack);
            return true;
        }
        if (path == "/api/worldcup/team-context-llm-review" && req.HttpMethod == "POST")
        {
            var objectId = req.QueryString["object_id"];
            if (string.IsNullOrWhiteSpace(objectId)) { res.StatusCode = 400; await ApiHelpers.WriteErrorAsync(res, "object_id is required"); return true; }
            var maxEvidence = int.TryParse(req.QueryString["max_evidence"], out var parsedMaxEvidence) ? parsedMaxEvidence : 12;
            var result = await TeamContextLlmReviewService.RunAsync(objectId, maxEvidence);
            await ApiHelpers.WriteJsonAsync(res, result, AppJsonContext.Default.TeamContextLlmReviewResult);
            return true;
        }
        if (path == "/api/worldcup/intelligence-content-quality" && req.HttpMethod == "GET")
        {
            var artifactId = req.QueryString["artifact_id"];
            if (string.IsNullOrWhiteSpace(artifactId)) { res.StatusCode = 400; await ApiHelpers.WriteErrorAsync(res, "artifact_id is required"); return true; }
            var result = store.AuditIntelligenceReportContent(artifactId);
            await ApiHelpers.WriteJsonAsync(res, result, AppJsonContext.Default.IntelligenceContentQualityResult);
            return true;
        }

        if (path == "/api/worldcup/system-logs" && req.HttpMethod == "GET")
        {
            var limit = int.TryParse(req.QueryString["limit"], out var parsedLimit) ? parsedLimit : 200;
            var items = ApiHelpers.IncludeTestData(req)
                ? store.GetSystemEventLogs(
                    req.QueryString["category"],
                    req.QueryString["event_type"],
                    req.QueryString["match_id"],
                    req.QueryString["object_id"],
                    req.QueryString["employee_id"],
                    limit)
                : store.GetProductionSystemEventLogs(
                    req.QueryString["category"],
                    req.QueryString["event_type"],
                    req.QueryString["match_id"],
                    req.QueryString["object_id"],
                    req.QueryString["employee_id"],
                    limit);
            await ApiHelpers.WriteJsonAsync(res, items, AppJsonContext.Default.ListWorldCupSystemEventLog);
            return true;
        }

        return false;
    }
}

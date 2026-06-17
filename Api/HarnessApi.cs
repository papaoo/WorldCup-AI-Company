using System.Net;

namespace PiPiClaw.Team;

/// <summary>
/// Harness endpoints used by development and regression tests.
/// </summary>
public static class HarnessApi
{
    public static async Task<bool> TryHandleAsync(HttpListenerContext ctx)
    {
        var req = ctx.Request;
        var res = ctx.Response;
        string path = req.Url?.AbsolutePath.ToLower() ?? "/";
        var store = AppContext.WorldCupStore;

        if (req.HttpMethod != "POST") return false;
        if (!ApiHelpers.AllowDevelopmentEndpoints())
        {
            res.StatusCode = 403;
            await ApiHelpers.WriteErrorAsync(res, "development endpoints are disabled", 403);
            return true;
        }

        switch (path)
        {
            case "/api/worldcup/baseline-harness":
                await ApiHelpers.WriteJsonAsync(res, store.RunBaselineHarness(), AppJsonContext.Default.BaselineBacktestResult);
                return true;
            case "/api/worldcup/match-review-harness":
                await ApiHelpers.WriteJsonAsync(res, store.RunMatchReviewHarness(), AppJsonContext.Default.MatchReviewHarnessResult);
                return true;
            case "/api/worldcup/strategy-evaluation-harness":
                await ApiHelpers.WriteJsonAsync(res, store.RunStrategyEvaluationHarness(), AppJsonContext.Default.StrategyEvaluationHarnessResult);
                return true;
            case "/api/worldcup/demo-results-harness":
                await ApiHelpers.WriteJsonAsync(res, store.RunDemoResultsHarness(), AppJsonContext.Default.DemoResultsHarnessResult);
                return true;
            case "/api/worldcup/lifecycle-harness":
                await ApiHelpers.WriteJsonAsync(res, store.RunLifecycleHarness(), AppJsonContext.Default.WorldCupLifecycleHarnessResult);
                return true;
            case "/api/worldcup/workflow-harness":
                var matchId = req.QueryString["match_id"];
                await ApiHelpers.WriteJsonAsync(res, store.RunWorkflowHarness(matchId), AppJsonContext.Default.WorkflowHarnessResult);
                return true;
            case "/api/worldcup/report-quality-harness":
                await ApiHelpers.WriteJsonAsync(res, store.RunReportQualityHarness(), AppJsonContext.Default.ReportQualityHarnessResult);
                return true;
            case "/api/worldcup/engineering-guardrail-harness":
                await ApiHelpers.WriteJsonAsync(res, store.RunEngineeringGuardrailHarness(), AppJsonContext.Default.EngineeringGuardrailHarnessResult);
                return true;
            case "/api/worldcup/memory-harness":
                await ApiHelpers.WriteJsonAsync(res, store.RunMemoryHarness(), AppJsonContext.Default.MemoryHarnessResult);
                return true;
            case "/api/worldcup/data-snapshot-harness":
                await ApiHelpers.WriteJsonAsync(res, store.RunDataSnapshotHarness(), AppJsonContext.Default.DataSnapshotHarnessResult);
                return true;
            case "/api/worldcup/data-snapshot-import-harness":
                await ApiHelpers.WriteJsonAsync(res, store.RunDataSnapshotImportHarness(), AppJsonContext.Default.DataSnapshotBatchImportResult);
                return true;
            case "/api/worldcup/data-snapshot-maintenance-harness":
                await ApiHelpers.WriteJsonAsync(res, store.RunDataSnapshotMaintenanceHarness(), AppJsonContext.Default.DataSnapshotMaintenanceResult);
                return true;
            case "/api/worldcup/data-snapshot-quality-harness":
                await ApiHelpers.WriteJsonAsync(res, store.RunDataSnapshotQualityHarness(), AppJsonContext.Default.DataSnapshotQualityHarnessResult);
                return true;
            case "/api/worldcup/data-source-import-harness":
                await ApiHelpers.WriteJsonAsync(res, await store.RunDataSourceImportHarnessAsync(), AppJsonContext.Default.DataSourceImportResult);
                return true;
            case "/api/worldcup/data-source-provider-harness":
                await ApiHelpers.WriteJsonAsync(res, await store.RunDataSourceProviderHarnessAsync(), AppJsonContext.Default.DataSourceProviderHarnessResult);
                return true;
            case "/api/worldcup/system-logs-harness":
                await ApiHelpers.WriteJsonAsync(res, store.RunSystemEventLogHarness(), AppJsonContext.Default.WorldCupSystemEventLogHarnessResult);
                return true;
            case "/api/worldcup/intelligence-workflow-harness":
                await ApiHelpers.WriteJsonAsync(res, store.RunIntelligenceWorkflowHarness(), AppJsonContext.Default.IntelligenceWorkflowHarnessResult);
                return true;
            case "/api/worldcup/intelligence-quality-harness":
                await ApiHelpers.WriteJsonAsync(res, store.RunIntelligenceTriageQualityHarness(), AppJsonContext.Default.IntelligenceWorkflowHarnessResult);
                return true;
            case "/api/worldcup/team-workbench-harness":
                await ApiHelpers.WriteJsonAsync(res, store.RunTeamWorkbenchHarness(), AppJsonContext.Default.TeamWorkbenchHarnessResult);
                return true;
            case "/api/worldcup/intelligence-content-quality-harness":
                await ApiHelpers.WriteJsonAsync(res, store.RunIntelligenceContentQualityHarness(), AppJsonContext.Default.IntelligenceContentQualityHarnessResult);
                return true;
            case "/api/worldcup/bff-harness":
                await ApiHelpers.WriteJsonAsync(res, store.RunWorldCupBffHarness(), AppJsonContext.Default.WorldCupBffHarnessResult);
                return true;
        }

        return false;
    }
}

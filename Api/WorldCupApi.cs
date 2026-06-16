using System.Net;
using System.Text;
using System.Text.Json;

namespace PiPiClaw.Team;

/// <summary>
/// 世界杯核心业务 API：比赛、预测、工作流、产物、生命周期、策略评估。
/// </summary>
public static class WorldCupApi
{
    public static async Task<bool> TryHandleAsync(HttpListenerContext ctx)
    {
        var req = ctx.Request;
        var res = ctx.Response;
        string path = req.Url?.AbsolutePath.ToLower() ?? "/";
        var store = AppContext.WorldCupStore;

        // Health & seed
        if (path == "/api/worldcup/health" && req.HttpMethod == "GET")
        {
            res.ContentType = "application/json; charset=utf-8";
            var status = store.GetStatus();
            await ApiHelpers.WriteJsonAsync(res, status, AppJsonContext.Default.WorldCupStoreStatus);
            return true;
        }
        if (path == "/api/worldcup/seed" && req.HttpMethod == "POST")
        {
            store.SeedDemoWorldCupCompany();
            res.ContentType = "application/json; charset=utf-8";
            var status = store.GetStatus();
            await ApiHelpers.WriteJsonAsync(res, status, AppJsonContext.Default.WorldCupStoreStatus);
            return true;
        }
        if (path == "/api/worldcup/bootstrap" && req.HttpMethod == "POST")
        {
            var result = await store.BootstrapPublicWorldCupDataAsync();
            res.ContentType = "application/json; charset=utf-8";
            await ApiHelpers.WriteJsonAsync(res, result, AppJsonContext.Default.WorldCupPublicDataBootstrapResult);
            return true;
        }

        // Matches
        if (path == "/api/worldcup/matches" && req.HttpMethod == "GET")
        {
            var items = ApiHelpers.IncludeTestData(req) ? store.GetMatches() : store.GetProductionMatches();
            await ApiHelpers.WriteJsonAsync(res, items, AppJsonContext.Default.ListWorldCupMatch);
            return true;
        }

        // Baseline predictions
        if (path == "/api/worldcup/baseline-predictions" && req.HttpMethod == "GET")
        {
            var matchId = req.QueryString["match_id"];
            var items = ApiHelpers.IncludeTestData(req)
                ? store.GetBaselinePredictions(matchId)
                : store.GetProductionBaselinePredictions(matchId);
            await ApiHelpers.WriteJsonAsync(res, items, AppJsonContext.Default.ListBaselinePredictionRecord);
            return true;
        }
        if (path == "/api/worldcup/baseline-predict" && req.HttpMethod == "POST")
        {
            var matchId = req.QueryString["match_id"];
            if (string.IsNullOrWhiteSpace(matchId)) { res.StatusCode = 400; await ApiHelpers.WriteErrorAsync(res, "match_id is required"); return true; }
            var prediction = store.CreateBaselinePrediction(matchId);
            await ApiHelpers.WriteJsonAsync(res, prediction, AppJsonContext.Default.BaselinePredictionRecord);
            return true;
        }
        if (path == "/api/worldcup/production-baseline-refresh" && req.HttpMethod == "POST")
        {
            var result = store.RefreshProductionBaselinePredictions();
            await ApiHelpers.WriteJsonAsync(res, result, AppJsonContext.Default.BaselineBacktestResult);
            return true;
        }

        // Match result & review
        if (path == "/api/worldcup/match-result" && req.HttpMethod == "POST")
        {
            var body = await ApiHelpers.ReadBodyAsync(req);
            var request = JsonSerializer.Deserialize(body, AppJsonContext.Default.MatchResultRequest);
            if (request == null || string.IsNullOrWhiteSpace(request.MatchId)) { res.StatusCode = 400; await ApiHelpers.WriteErrorAsync(res, "match_id is required"); return true; }
            var match = store.RecordMatchResult(request);
            await ApiHelpers.WriteJsonAsync(res, match, AppJsonContext.Default.WorldCupMatch);
            return true;
        }
        if (path == "/api/worldcup/match-review" && req.HttpMethod == "POST")
        {
            var matchId = req.QueryString["match_id"];
            if (string.IsNullOrWhiteSpace(matchId)) { res.StatusCode = 400; await ApiHelpers.WriteErrorAsync(res, "match_id is required"); return true; }
            var review = store.CreateMatchReview(matchId);
            await ApiHelpers.WriteJsonAsync(res, review, AppJsonContext.Default.MatchReviewRecord);
            return true;
        }

        // Lifecycle
        if (path == "/api/worldcup/lifecycle" && req.HttpMethod == "POST")
        {
            var matchId = req.QueryString["match_id"];
            if (string.IsNullOrWhiteSpace(matchId)) { res.StatusCode = 400; await ApiHelpers.WriteErrorAsync(res, "match_id required"); return true; }
            var result = store.ApplyMatchLifecycle(matchId);
            await ApiHelpers.WriteJsonAsync(res, result, AppJsonContext.Default.WorldCupLifecycleResult);
            return true;
        }

        // Workflows
        if (path == "/api/worldcup/workflows" && req.HttpMethod == "GET")
        {
            var items = ApiHelpers.IncludeTestData(req) ? store.GetWorkflowRuns() : store.GetProductionWorkflowRuns();
            await ApiHelpers.WriteJsonAsync(res, items, AppJsonContext.Default.ListWorkflowRunRecord);
            return true;
        }
        if (path == "/api/worldcup/workflow-steps" && req.HttpMethod == "GET")
        {
            var workflowId = req.QueryString["workflow_id"];
            if (string.IsNullOrWhiteSpace(workflowId)) { res.StatusCode = 400; await ApiHelpers.WriteErrorAsync(res, "workflow_id is required"); return true; }
            var items = ApiHelpers.IncludeTestData(req) ? store.GetWorkflowSteps(workflowId) : store.GetProductionWorkflowSteps(workflowId);
            await ApiHelpers.WriteJsonAsync(res, items, AppJsonContext.Default.ListWorkflowStepRecord);
            return true;
        }

        // Artifacts
        if (path == "/api/worldcup/artifacts" && req.HttpMethod == "GET")
        {
            var workflowId = req.QueryString["workflow_id"];
            var items = ApiHelpers.IncludeTestData(req) ? store.GetArtifacts(workflowId) : store.GetProductionArtifacts(workflowId);
            await ApiHelpers.WriteJsonAsync(res, items, AppJsonContext.Default.ListArtifactRecord);
            return true;
        }
        if (path == "/api/worldcup/artifact-content" && req.HttpMethod == "GET")
        {
            var artifactId = req.QueryString["artifact_id"];
            if (string.IsNullOrWhiteSpace(artifactId)) { res.StatusCode = 400; await ApiHelpers.WriteErrorAsync(res, "artifact_id is required"); return true; }
            var content = store.GetArtifactContent(artifactId);
            if (content == null) { res.StatusCode = 404; await ApiHelpers.WriteErrorAsync(res, "artifact not found"); return true; }
            await ApiHelpers.WriteJsonAsync(res, content, AppJsonContext.Default.ArtifactContent);
            return true;
        }

        // Prediction workflows (mock & real)
        if (path == "/api/worldcup/mock-prediction-workflow" && req.HttpMethod == "POST")
        {
            var matchId = req.QueryString["match_id"];
            if (string.IsNullOrWhiteSpace(matchId)) { res.StatusCode = 400; await ApiHelpers.WriteErrorAsync(res, "match_id is required"); return true; }
            var result = store.RunMockMatchPredictionWorkflow(matchId);
            await ApiHelpers.WriteJsonAsync(res, result, AppJsonContext.Default.MatchWorkflowResult);
            return true;
        }
        if (path == "/api/worldcup/real-prediction-workflow" && req.HttpMethod == "POST")
        {
            var matchId = req.QueryString["match_id"];
            if (string.IsNullOrWhiteSpace(matchId)) { res.StatusCode = 400; await ApiHelpers.WriteErrorAsync(res, "match_id is required"); return true; }
            var result = await WorldCupWorkflowService.RunRealWorldCupWorkflowAsync(matchId);
            await ApiHelpers.WriteJsonAsync(res, result, AppJsonContext.Default.MatchWorkflowResult);
            return true;
        }

        // Strategy evaluation
        if (path == "/api/worldcup/strategy-evaluation" && req.HttpMethod == "GET")
        {
            var summary = store.GetStrategyEvaluation();
            await ApiHelpers.WriteJsonAsync(res, summary, AppJsonContext.Default.StrategyEvaluationSummary);
            return true;
        }
        if (path == "/api/worldcup/model-backtest" && req.HttpMethod == "POST")
        {
            var days = int.TryParse(req.QueryString["days"], out var parsedDays) ? parsedDays : 1095;
            var limit = int.TryParse(req.QueryString["limit"], out var parsedLimit) ? parsedLimit : 300;
            var result = await store.RunAndCacheModelBacktestAsync(days, limit);
            await ApiHelpers.WriteJsonAsync(res, result, AppJsonContext.Default.ModelBacktestResult);
            return true;
        }

        return false;
    }

}

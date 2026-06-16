using System.Net;
using System.Text.Json;

namespace PiPiClaw.Team;

/// <summary>
/// Data source APIs: snapshots, imports, public-data bootstrap, and auto collection.
/// </summary>
public static class DataSourceApi
{
    public static async Task<bool> TryHandleAsync(HttpListenerContext ctx)
    {
        var req = ctx.Request;
        var res = ctx.Response;
        string path = req.Url?.AbsolutePath.ToLower() ?? "/";
        var store = AppContext.WorldCupStore;

        if (path == "/api/worldcup/data-snapshots" && req.HttpMethod == "GET")
        {
            var matchId = req.QueryString["match_id"];
            var objectId = req.QueryString["object_id"];
            var snapshotType = req.QueryString["snapshot_type"];
            var items = ApiHelpers.IncludeTestData(req)
                ? store.GetDataSnapshots(matchId, objectId, snapshotType)
                : store.GetProductionDataSnapshots(matchId, objectId, snapshotType);
            await ApiHelpers.WriteJsonAsync(res, items, AppJsonContext.Default.ListDataSnapshotRecord);
            return true;
        }
        if (path == "/api/worldcup/data-snapshots" && req.HttpMethod == "POST")
        {
            var body = await ApiHelpers.ReadBodyAsync(req);
            var request = JsonSerializer.Deserialize(body, AppJsonContext.Default.DataSnapshotCreateRequest);
            if (request == null || string.IsNullOrWhiteSpace(request.ContentJson)) { await ApiHelpers.WriteErrorAsync(res, "content_json is required"); return true; }
            var item = store.AddDataSnapshot(request);
            await ApiHelpers.WriteJsonAsync(res, item, AppJsonContext.Default.DataSnapshotRecord);
            return true;
        }

        if (path == "/api/worldcup/data-snapshot-import" && req.HttpMethod == "POST")
        {
            var body = await ApiHelpers.ReadBodyAsync(req);
            var request = JsonSerializer.Deserialize(body, AppJsonContext.Default.DataSnapshotBatchImportRequest);
            if (request == null || request.Items.Count == 0) { await ApiHelpers.WriteErrorAsync(res, "items are required"); return true; }
            var result = store.ImportDataSnapshots(request);
            await ApiHelpers.WriteJsonAsync(res, result, AppJsonContext.Default.DataSnapshotBatchImportResult);
            return true;
        }
        if (path == "/api/worldcup/data-snapshot-maintenance" && req.HttpMethod == "POST")
        {
            var result = store.PruneDuplicateDataSnapshots();
            await ApiHelpers.WriteJsonAsync(res, result, AppJsonContext.Default.DataSnapshotMaintenanceResult);
            return true;
        }
        if (path == "/api/worldcup/data-snapshot-quality" && req.HttpMethod == "GET")
        {
            var limit = int.TryParse(req.QueryString["limit"], out var parsedLimit) ? parsedLimit : 200;
            var result = store.AuditDataSnapshotQuality(
                req.QueryString["source"],
                req.QueryString["match_id"],
                req.QueryString["object_id"],
                req.QueryString["snapshot_type"],
                limit);
            await ApiHelpers.WriteJsonAsync(res, result, AppJsonContext.Default.DataSnapshotQualityResult);
            return true;
        }
        if (path == "/api/worldcup/data-readiness-audit" && req.HttpMethod == "GET")
        {
            var result = store.AuditWorldCupDataReadiness(includeTestData: ApiHelpers.IncludeTestData(req));
            await ApiHelpers.WriteJsonAsync(res, result, AppJsonContext.Default.WorldCupDataReadinessAuditResult);
            return true;
        }
        if (path == "/api/worldcup/model-review" && req.HttpMethod == "POST")
        {
            var result = await WorldCupModelReviewService.RunAsync();
            await ApiHelpers.WriteJsonAsync(res, result, AppJsonContext.Default.WorldCupModelReviewResult);
            return true;
        }

        if (path == "/api/worldcup/data-source-import" && req.HttpMethod == "POST")
        {
            var body = await ApiHelpers.ReadBodyAsync(req);
            var request = JsonSerializer.Deserialize(body, AppJsonContext.Default.DataSourceImportRequest);
            if (request == null) { await ApiHelpers.WriteErrorAsync(res, "request body is required"); return true; }
            var result = await store.ImportDataSourceAsync(request);
            await ApiHelpers.WriteJsonAsync(res, result, AppJsonContext.Default.DataSourceImportResult);
            return true;
        }

        if (path == "/api/worldcup/public-data-bootstrap" && req.HttpMethod == "POST")
        {
            var result = await store.BootstrapPublicWorldCupDataAsync();
            await ApiHelpers.WriteJsonAsync(res, result, AppJsonContext.Default.WorldCupPublicDataBootstrapResult);
            return true;
        }

        if (path == "/api/worldcup/auto-collection-config" && req.HttpMethod == "GET")
        {
            var config = AutoCollectionService.LoadAutoCollectionConfig();
            await ApiHelpers.WriteJsonAsync(res, config, AppJsonContext.Default.DataSourceAutoCollectionConfig);
            return true;
        }
        if (path == "/api/worldcup/auto-collection-config" && req.HttpMethod == "PUT")
        {
            var body = await ApiHelpers.ReadBodyAsync(req);
            var request = JsonSerializer.Deserialize(body, AppJsonContext.Default.DataSourceAutoCollectionConfig);
            if (request == null) { await ApiHelpers.WriteErrorAsync(res, "request body is required"); return true; }
            var config = AutoCollectionService.SaveAutoCollectionConfig(request);
            await ApiHelpers.WriteJsonAsync(res, config, AppJsonContext.Default.DataSourceAutoCollectionConfig);
            return true;
        }
        if (path == "/api/worldcup/auto-collection-run" && req.HttpMethod == "POST")
        {
            var config = AutoCollectionService.LoadAutoCollectionConfig();
            var result = await AutoCollectionService.RunAutoCollectionWithLockAsync(config, includeMaintenance: false);
            var delay = AutoCollectionService.ResolveNextDelay(config);
            result.NextIntervalMinutes = delay.Minutes;
            result.NextIntervalReason = delay.Reason;
            await ApiHelpers.WriteJsonAsync(res, result, AppJsonContext.Default.DataSourceAutoCollectionRunResult);
            return true;
        }
        if (path == "/api/worldcup/auto-collection-last-run" && req.HttpMethod == "GET")
        {
            var result = AppContext.CurrentAutoCollectionRun ?? AppContext.LastAutoCollectionRun ?? new DataSourceAutoCollectionRunResult
            {
                Passed = true,
                Notes = ["Auto collection has not run in this process yet."]
            };
            if (result.Running && DateTime.TryParse(result.StartedAt, out var startedAt))
            {
                result.ElapsedSeconds = Math.Max(0, (int)Math.Round((DateTime.Now - startedAt).TotalSeconds));
            }
            await ApiHelpers.WriteJsonAsync(res, result, AppJsonContext.Default.DataSourceAutoCollectionRunResult);
            return true;
        }

        return false;
    }
}

using System.Text.Json;

namespace PiPiClaw.Team;

public sealed partial class WorldCupStore
{
    public List<WorldCupWatchObject> GetProductionWatchObjects()
    {
        return GetWatchObjects()
            .Where(item => !IsTestWatchObject(item))
            .ToList();
    }

    public List<EmployeeAssignment> GetProductionAssignments()
    {
        var productionObjectIds = GetProductionWatchObjects()
            .Select(item => item.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return GetAssignments()
            .Where(assignment => productionObjectIds.Contains(assignment.ObjectId))
            .ToList();
    }

    public List<WorldCupEmployee> GetProductionEmployees()
    {
        var productionEmployeeIds = GetProductionAssignments()
            .Select(assignment => assignment.EmployeeId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return GetEmployees()
            .Where(employee => IsCoreEmployee(employee) || productionEmployeeIds.Contains(employee.Id))
            .ToList();
    }

    public List<WorldCupMatch> GetProductionMatches()
    {
        var productionObjectIds = GetProductionWatchObjects()
            .Select(item => item.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return GetMatches()
            .Where(match => IsProductionMatch(match, productionObjectIds))
            .ToList();
    }

    public List<BaselinePredictionRecord> GetProductionBaselinePredictions(string? matchId = null)
    {
        var productionMatchIds = GetProductionMatches()
            .Select(match => match.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return GetBaselinePredictions(matchId)
            .Where(prediction => productionMatchIds.Contains(prediction.MatchId))
            .ToList();
    }

    public List<DataSnapshotRecord> GetProductionDataSnapshots(string? matchId = null, string? objectId = null, string? snapshotType = null)
    {
        return GetDataSnapshots(matchId, objectId, snapshotType)
            .Where(IsProductionDataSnapshot)
            .ToList();
    }

    public List<IntelligenceSignalRecord> GetProductionIntelligenceSignals(
        string? status = null,
        string? objectId = null,
        string? matchId = null,
        string? signalType = null,
        int limit = 200)
    {
        var productionObjectIds = GetProductionWatchObjects()
            .Select(item => item.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var productionMatchIds = GetProductionMatches()
            .Select(match => match.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return GetIntelligenceSignals(status, objectId, matchId, signalType, limit)
            .Where(signal => IsProductionIntelligenceSignal(signal, productionObjectIds, productionMatchIds))
            .ToList();
    }

    public List<WorkflowRunRecord> GetProductionWorkflowRuns()
    {
        var productionObjectIds = GetProductionWatchObjects()
            .Select(item => item.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var productionMatchIds = GetProductionMatches()
            .Select(match => match.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return GetWorkflowRuns()
            .Where(workflow => IsProductionWorkflowRun(workflow, productionObjectIds, productionMatchIds))
            .ToList();
    }

    public List<ArtifactRecord> GetProductionArtifacts(string? workflowRunId = null)
    {
        var productionWorkflowIds = GetProductionWorkflowRuns()
            .Select(workflow => workflow.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var productionObjectIds = GetProductionWatchObjects()
            .Select(item => item.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return GetArtifacts(workflowRunId)
            .Where(artifact => IsProductionArtifact(artifact, productionWorkflowIds, productionObjectIds))
            .ToList();
    }

    public List<WorkflowStepRecord> GetProductionWorkflowSteps(string workflowRunId)
    {
        var productionWorkflowIds = GetProductionWorkflowRuns()
            .Select(workflow => workflow.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return productionWorkflowIds.Contains(workflowRunId)
            ? GetWorkflowSteps(workflowRunId)
            : [];
    }

    public List<WorldCupSystemEventLog> GetProductionSystemEventLogs(
        string? category = null,
        string? eventType = null,
        string? matchId = null,
        string? objectId = null,
        string? employeeId = null,
        int limit = 200)
    {
        var productionObjectIds = GetProductionWatchObjects()
            .Select(item => item.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var productionMatchIds = GetProductionMatches()
            .Select(match => match.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var productionWorkflowIds = GetProductionWorkflowRuns()
            .Select(workflow => workflow.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return GetSystemEventLogs(category, eventType, matchId, objectId, employeeId, limit)
            .Where(log => IsProductionSystemEventLog(log, productionObjectIds, productionMatchIds, productionWorkflowIds))
            .ToList();
    }

    private static bool IsProductionMatch(WorldCupMatch match, IReadOnlySet<string> productionObjectIds)
    {
        if (IsDemoOrHarnessMatch(match)) return false;
        if (!match.Id.StartsWith("match_wc26_", StringComparison.OrdinalIgnoreCase)) return false;
        if (!productionObjectIds.Contains(match.HomeObjectId)) return false;
        if (!productionObjectIds.Contains(match.AwayObjectId)) return false;
        if (match.HomeObjectId.StartsWith("slot_", StringComparison.OrdinalIgnoreCase)) return false;
        if (match.AwayObjectId.StartsWith("slot_", StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    private static bool IsProductionDataSnapshot(DataSnapshotRecord snapshot)
    {
        return IsTrustedProductionSnapshotSource(snapshot.Source)
            && !IsTestIdentifier(snapshot.Source)
            && !IsTestIdentifier(snapshot.Id)
            && !IsTestIdentifier(snapshot.ObjectId)
            && !IsTestIdentifier(snapshot.MatchId);
    }

    private static bool IsProductionIntelligenceSignal(
        IntelligenceSignalRecord signal,
        IReadOnlySet<string> productionObjectIds,
        IReadOnlySet<string> productionMatchIds)
    {
        if (IsTestIdentifier(signal.Id)
            || IsTestIdentifier(signal.SourceSnapshotId)
            || IsTestSourceInEvidence(signal.EvidenceJson))
        {
            return false;
        }
        if (!string.IsNullOrWhiteSpace(signal.ObjectId) && !productionObjectIds.Contains(signal.ObjectId)) return false;
        if (!string.IsNullOrWhiteSpace(signal.MatchId) && !productionMatchIds.Contains(signal.MatchId)) return false;
        return true;
    }

    private static bool IsProductionWorkflowRun(
        WorkflowRunRecord workflow,
        IReadOnlySet<string> productionObjectIds,
        IReadOnlySet<string> productionMatchIds)
    {
        if (IsTestIdentifier(workflow.Id)
            || IsTestIdentifier(workflow.WorkflowType)
            || IsTestIdentifier(workflow.StartedBy)
            || IsTestIdentifier(workflow.MetadataJson))
        {
            return false;
        }
        if (!string.IsNullOrWhiteSpace(workflow.ObjectId) && !productionObjectIds.Contains(workflow.ObjectId)) return false;
        if (!string.IsNullOrWhiteSpace(workflow.MatchId) && !productionMatchIds.Contains(workflow.MatchId)) return false;
        return true;
    }

    private static bool IsProductionArtifact(
        ArtifactRecord artifact,
        IReadOnlySet<string> productionWorkflowIds,
        IReadOnlySet<string> productionObjectIds)
    {
        if (IsTestIdentifier(artifact.Id)
            || IsTestIdentifier(artifact.FilePath)
            || IsTestIdentifier(artifact.MetadataJson))
        {
            return false;
        }
        if (!string.IsNullOrWhiteSpace(artifact.WorkflowRunId)
            && !productionWorkflowIds.Contains(artifact.WorkflowRunId))
        {
            return false;
        }
        if (!string.IsNullOrWhiteSpace(artifact.ObjectId)
            && !productionObjectIds.Contains(artifact.ObjectId))
        {
            return false;
        }
        return true;
    }

    private static bool IsProductionSystemEventLog(
        WorldCupSystemEventLog log,
        IReadOnlySet<string> productionObjectIds,
        IReadOnlySet<string> productionMatchIds,
        IReadOnlySet<string> productionWorkflowIds)
    {
        if (IsTestIdentifier(log.Id)
            || IsTestIdentifier(log.Source)
            || IsTestIdentifier(log.Category)
            || IsTestIdentifier(log.EventType)
            || IsTestIdentifier(log.SnapshotId)
            || IsTestIdentifier(log.ArtifactId))
        {
            return false;
        }
        if (!string.IsNullOrWhiteSpace(log.ObjectId) && !productionObjectIds.Contains(log.ObjectId)) return false;
        if (!string.IsNullOrWhiteSpace(log.MatchId) && !productionMatchIds.Contains(log.MatchId)) return false;
        if (!string.IsNullOrWhiteSpace(log.WorkflowRunId) && !productionWorkflowIds.Contains(log.WorkflowRunId)) return false;
        return true;
    }

    private static bool IsCoreEmployee(WorldCupEmployee employee)
    {
        var role = NormalizeEmployeeRole(employee.Role);
        return role is "ceo" or "hr" or "data_analyst" or "risk_officer";
    }

    private static bool IsTestWatchObject(WorldCupWatchObject item)
    {
        return item.Type.Equals("tournament_slot", StringComparison.OrdinalIgnoreCase)
            || item.Id.StartsWith("slot_", StringComparison.OrdinalIgnoreCase)
            || IsTestIdentifier(item.Id)
            || HasMetadataBoolean(item.MetadataJson, "demo")
            || HasMetadataBoolean(item.MetadataJson, "harness");
    }

    private static bool IsTestIdentifier(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        return value.Contains("demo", StringComparison.OrdinalIgnoreCase)
            || value.Contains("harness", StringComparison.OrdinalIgnoreCase)
            || value.Contains("mock", StringComparison.OrdinalIgnoreCase)
            || value.Contains("synthetic", StringComparison.OrdinalIgnoreCase)
            || value.Contains("manual_demo", StringComparison.OrdinalIgnoreCase)
            || value.Contains("snapshot_quality", StringComparison.OrdinalIgnoreCase)
            || value.Contains("content_quality", StringComparison.OrdinalIgnoreCase)
            || value.Contains("adapter_harness", StringComparison.OrdinalIgnoreCase)
            || value.Contains("dedupe_harness", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTrustedProductionSnapshotSource(string source)
    {
        return source.Equals("worldcup26_bootstrap", StringComparison.OrdinalIgnoreCase)
            || source.Equals("fixturedownload_bootstrap", StringComparison.OrdinalIgnoreCase)
            || source.Equals("worldcup26_games", StringComparison.OrdinalIgnoreCase)
            || source.Equals("worldcup26_teams", StringComparison.OrdinalIgnoreCase)
            || source.Equals("worldcup26_groups", StringComparison.OrdinalIgnoreCase)
            || source.Equals("worldcup26_stadiums", StringComparison.OrdinalIgnoreCase)
            || source.Equals("fifa_official_mens_ranking", StringComparison.OrdinalIgnoreCase)
            || source.Equals("world_football_elo", StringComparison.OrdinalIgnoreCase)
            || source.Equals("international_results_recent_form", StringComparison.OrdinalIgnoreCase)
            || source.Equals("openfootball_schedule", StringComparison.OrdinalIgnoreCase)
            || source.Equals("fixturedownload_schedule", StringComparison.OrdinalIgnoreCase)
            || source.Equals("espn_scoreboard", StringComparison.OrdinalIgnoreCase)
            || source.Equals("espn_summary", StringComparison.OrdinalIgnoreCase)
            || source.Equals("rss_soccer_news", StringComparison.OrdinalIgnoreCase)
            || source.StartsWith("rss_match_watch_", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTestSourceInEvidence(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("source", out var source)
                && IsTestIdentifier(source.ToString());
        }
        catch
        {
            return false;
        }
    }

    private static bool HasMetadataBoolean(string json, string propertyName)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty(propertyName, out var value)
                && value.ValueKind == JsonValueKind.True;
        }
        catch
        {
            return false;
        }
    }
}

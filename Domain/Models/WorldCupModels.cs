using System.Text.Json.Serialization;

namespace PiPiClaw.Team;

public class WorldCupWatchObject
{
    [JsonPropertyName("id")] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [JsonPropertyName("type")] public string Type { get; set; } = "football_team";
    [JsonPropertyName("symbol")] public string Symbol { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("display_name")] public string DisplayName { get; set; } = "";
    [JsonPropertyName("status")] public string Status { get; set; } = "active";
    [JsonPropertyName("metadata_json")] public string MetadataJson { get; set; } = "{}";
    [JsonPropertyName("created_at")] public string CreatedAt { get; set; } = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
    [JsonPropertyName("updated_at")] public string UpdatedAt { get; set; } = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
}

public class WorldCupEmployee
{
    [JsonPropertyName("id")] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("role")] public string Role { get; set; } = "";
    [JsonPropertyName("specialty")] public string Specialty { get; set; } = "";
    [JsonPropertyName("status")] public string Status { get; set; } = "active";
    [JsonPropertyName("prompt_profile")] public string PromptProfile { get; set; } = "";
    [JsonPropertyName("model_index")] public int ModelIndex { get; set; }
    [JsonPropertyName("contacts_json")] public string ContactsJson { get; set; } = "[]";
    [JsonPropertyName("created_at")] public string CreatedAt { get; set; } = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
    [JsonPropertyName("updated_at")] public string UpdatedAt { get; set; } = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
}

public class EmployeeAssignment
{
    [JsonPropertyName("id")] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [JsonPropertyName("employee_id")] public string EmployeeId { get; set; } = "";
    [JsonPropertyName("object_id")] public string ObjectId { get; set; } = "";
    [JsonPropertyName("assignment_role")] public string AssignmentRole { get; set; } = "primary_researcher";
    [JsonPropertyName("status")] public string Status { get; set; } = "active";
    [JsonPropertyName("started_at")] public string StartedAt { get; set; } = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
    [JsonPropertyName("ended_at")] public string? EndedAt { get; set; }
    [JsonPropertyName("metadata_json")] public string MetadataJson { get; set; } = "{}";
}

public class WorkflowRunRecord
{
    [JsonPropertyName("id")] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [JsonPropertyName("workflow_type")] public string WorkflowType { get; set; } = "";
    [JsonPropertyName("status")] public string Status { get; set; } = "pending";
    [JsonPropertyName("object_id")] public string? ObjectId { get; set; }
    [JsonPropertyName("match_id")] public string? MatchId { get; set; }
    [JsonPropertyName("started_by")] public string StartedBy { get; set; } = "system";
    [JsonPropertyName("started_at")] public string StartedAt { get; set; } = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
    [JsonPropertyName("completed_at")] public string? CompletedAt { get; set; }
    [JsonPropertyName("error_message")] public string? ErrorMessage { get; set; }
    [JsonPropertyName("metadata_json")] public string MetadataJson { get; set; } = "{}";
}

public class WorkflowStepRecord
{
    [JsonPropertyName("id")] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [JsonPropertyName("workflow_run_id")] public string WorkflowRunId { get; set; } = "";
    [JsonPropertyName("step_type")] public string StepType { get; set; } = "";
    [JsonPropertyName("status")] public string Status { get; set; } = "pending";
    [JsonPropertyName("assignee_employee_id")] public string? AssigneeEmployeeId { get; set; }
    [JsonPropertyName("started_at")] public string? StartedAt { get; set; }
    [JsonPropertyName("completed_at")] public string? CompletedAt { get; set; }
    [JsonPropertyName("input_json")] public string InputJson { get; set; } = "{}";
    [JsonPropertyName("output_json")] public string OutputJson { get; set; } = "{}";
    [JsonPropertyName("artifact_id")] public string? ArtifactId { get; set; }
    [JsonPropertyName("error_message")] public string? ErrorMessage { get; set; }
}

public class ArtifactRecord
{
    [JsonPropertyName("id")] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [JsonPropertyName("type")] public string Type { get; set; } = "markdown";
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("owner_employee_id")] public string? OwnerEmployeeId { get; set; }
    [JsonPropertyName("object_id")] public string? ObjectId { get; set; }
    [JsonPropertyName("workflow_run_id")] public string? WorkflowRunId { get; set; }
    [JsonPropertyName("file_path")] public string FilePath { get; set; } = "";
    [JsonPropertyName("summary")] public string Summary { get; set; } = "";
    [JsonPropertyName("mime_type")] public string MimeType { get; set; } = "text/markdown";
    [JsonPropertyName("content_hash")] public string ContentHash { get; set; } = "";
    [JsonPropertyName("version")] public int Version { get; set; } = 1;
    [JsonPropertyName("metadata_json")] public string MetadataJson { get; set; } = "{}";
    [JsonPropertyName("parent_artifact_id")] public string? ParentArtifactId { get; set; }
    [JsonPropertyName("created_at")] public string CreatedAt { get; set; } = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
}

public class ArtifactContent
{
    [JsonPropertyName("artifact")] public ArtifactRecord Artifact { get; set; } = new();
    [JsonPropertyName("content")] public string Content { get; set; } = "";
}

public class MemoryRecord
{
    [JsonPropertyName("id")] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [JsonPropertyName("scope")] public string Scope { get; set; } = "object";
    [JsonPropertyName("owner_id")] public string? OwnerId { get; set; }
    [JsonPropertyName("object_id")] public string? ObjectId { get; set; }
    [JsonPropertyName("memory_type")] public string MemoryType { get; set; } = "episode";
    [JsonPropertyName("content")] public string Content { get; set; } = "";
    [JsonPropertyName("summary")] public string Summary { get; set; } = "";
    [JsonPropertyName("tags_json")] public string TagsJson { get; set; } = "[]";
    [JsonPropertyName("importance")] public double Importance { get; set; } = 0.5;
    [JsonPropertyName("confidence")] public double Confidence { get; set; } = 0.5;
    [JsonPropertyName("source_type")] public string SourceType { get; set; } = "system_event";
    [JsonPropertyName("source_id")] public string? SourceId { get; set; }
    [JsonPropertyName("valid_from")] public string ValidFrom { get; set; } = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
    [JsonPropertyName("expires_at")] public string? ExpiresAt { get; set; }
    [JsonPropertyName("contradicted_by_memory_id")] public string? ContradictedByMemoryId { get; set; }
    [JsonPropertyName("review_status")] public string ReviewStatus { get; set; } = "approved";
    [JsonPropertyName("created_at")] public string CreatedAt { get; set; } = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
    [JsonPropertyName("updated_at")] public string UpdatedAt { get; set; } = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
    [JsonPropertyName("last_used_at")] public string? LastUsedAt { get; set; }
}

public class MemoryCreateRequest
{
    [JsonPropertyName("scope")] public string Scope { get; set; } = "object";
    [JsonPropertyName("owner_id")] public string? OwnerId { get; set; }
    [JsonPropertyName("object_id")] public string? ObjectId { get; set; }
    [JsonPropertyName("memory_type")] public string MemoryType { get; set; } = "episode";
    [JsonPropertyName("content")] public string Content { get; set; } = "";
    [JsonPropertyName("summary")] public string Summary { get; set; } = "";
    [JsonPropertyName("tags_json")] public string TagsJson { get; set; } = "[]";
    [JsonPropertyName("importance")] public double Importance { get; set; } = 0.5;
    [JsonPropertyName("confidence")] public double Confidence { get; set; } = 0.5;
    [JsonPropertyName("source_type")] public string SourceType { get; set; } = "user_message";
    [JsonPropertyName("source_id")] public string? SourceId { get; set; }
    [JsonPropertyName("expires_at")] public string? ExpiresAt { get; set; }
}

public class MemoryHarnessResult
{
    [JsonPropertyName("memory_created")] public bool MemoryCreated { get; set; }
    [JsonPropertyName("memory_recalled")] public bool MemoryRecalled { get; set; }
    [JsonPropertyName("expired_memory_filtered")] public bool ExpiredMemoryFiltered { get; set; }
    [JsonPropertyName("context_contains_memory")] public bool ContextContainsMemory { get; set; }
    [JsonPropertyName("passed")] public bool Passed { get; set; }
    [JsonPropertyName("notes")] public List<string> Notes { get; set; } = [];
}

public class DataSnapshotRecord
{
    [JsonPropertyName("id")] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [JsonPropertyName("source")] public string Source { get; set; } = "manual_demo";
    [JsonPropertyName("snapshot_type")] public string SnapshotType { get; set; } = "team_intel";
    [JsonPropertyName("object_id")] public string? ObjectId { get; set; }
    [JsonPropertyName("match_id")] public string? MatchId { get; set; }
    [JsonPropertyName("content_json")] public string ContentJson { get; set; } = "{}";
    [JsonPropertyName("content_hash")] public string ContentHash { get; set; } = "";
    [JsonPropertyName("captured_at")] public string CapturedAt { get; set; } = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
}

public class DataSnapshotCreateRequest
{
    [JsonPropertyName("source")] public string Source { get; set; } = "";
    [JsonPropertyName("snapshot_type")] public string SnapshotType { get; set; } = "team_intel";
    [JsonPropertyName("object_id")] public string? ObjectId { get; set; }
    [JsonPropertyName("match_id")] public string? MatchId { get; set; }
    [JsonPropertyName("content_json")] public string ContentJson { get; set; } = "{}";
}

public class DataSnapshotBatchImportRequest
{
    [JsonPropertyName("source")] public string Source { get; set; } = "manual_import";
    [JsonPropertyName("items")] public List<DataSnapshotCreateRequest> Items { get; set; } = [];
    [JsonPropertyName("notes")] public List<string> Notes { get; set; } = [];
    [JsonIgnore] public bool AllowEmptySuccess { get; set; }
}

public class DataSnapshotBatchImportResult
{
    [JsonPropertyName("requested")] public int Requested { get; set; }
    [JsonPropertyName("imported")] public int Imported { get; set; }
    [JsonPropertyName("skipped_duplicates")] public int SkippedDuplicates { get; set; }
    [JsonPropertyName("recalled")] public int Recalled { get; set; }
    [JsonPropertyName("context_contains_imported_evidence")] public bool ContextContainsImportedEvidence { get; set; }
    [JsonPropertyName("hashes_populated")] public bool HashesPopulated { get; set; }
    [JsonPropertyName("passed")] public bool Passed { get; set; }
    [JsonPropertyName("imported_items")] public List<DataSnapshotRecord> ImportedItems { get; set; } = [];
    [JsonPropertyName("duplicate_items")] public List<DataSnapshotRecord> DuplicateItems { get; set; } = [];
    [JsonPropertyName("notes")] public List<string> Notes { get; set; } = [];
}

public class DataSnapshotMaintenanceResult
{
    [JsonPropertyName("before_count")] public int BeforeCount { get; set; }
    [JsonPropertyName("after_count")] public int AfterCount { get; set; }
    [JsonPropertyName("duplicates_removed")] public int DuplicatesRemoved { get; set; }
    [JsonPropertyName("duplicate_import_skipped")] public bool DuplicateImportSkipped { get; set; }
    [JsonPropertyName("passed")] public bool Passed { get; set; }
    [JsonPropertyName("notes")] public List<string> Notes { get; set; } = [];
}

public class DataSnapshotQualityResult
{
    [JsonPropertyName("snapshots_checked")] public int SnapshotsChecked { get; set; }
    [JsonPropertyName("valid_json_count")] public int ValidJsonCount { get; set; }
    [JsonPropertyName("invalid_json_count")] public int InvalidJsonCount { get; set; }
    [JsonPropertyName("hash_mismatch_count")] public int HashMismatchCount { get; set; }
    [JsonPropertyName("missing_source_count")] public int MissingSourceCount { get; set; }
    [JsonPropertyName("missing_target_count")] public int MissingTargetCount { get; set; }
    [JsonPropertyName("news_shape_errors")] public int NewsShapeErrors { get; set; }
    [JsonPropertyName("passed")] public bool Passed { get; set; }
    [JsonPropertyName("notes")] public List<string> Notes { get; set; } = [];
}

public class DataSnapshotQualityHarnessResult
{
    [JsonPropertyName("valid_source_quality")] public DataSnapshotQualityResult ValidSourceQuality { get; set; } = new();
    [JsonPropertyName("invalid_json_detected")] public bool InvalidJsonDetected { get; set; }
    [JsonPropertyName("news_shape_validated")] public bool NewsShapeValidated { get; set; }
    [JsonPropertyName("hashes_validated")] public bool HashesValidated { get; set; }
    [JsonPropertyName("passed")] public bool Passed { get; set; }
    [JsonPropertyName("notes")] public List<string> Notes { get; set; } = [];
}

public class DataSourceImportRequest
{
    [JsonPropertyName("source_name")] public string SourceName { get; set; } = "external_json";
    [JsonPropertyName("provider")] public string? Provider { get; set; }
    [JsonPropertyName("file_path")] public string? FilePath { get; set; }
    [JsonPropertyName("url")] public string? Url { get; set; }
    [JsonPropertyName("match_id")] public string? MatchId { get; set; }
    [JsonPropertyName("object_id")] public string? ObjectId { get; set; }
    [JsonPropertyName("snapshot_type")] public string? SnapshotType { get; set; }
    [JsonPropertyName("query")] public string? Query { get; set; }
    [JsonPropertyName("sport_key")] public string? SportKey { get; set; }
    [JsonPropertyName("competition_code")] public string? CompetitionCode { get; set; }
    [JsonPropertyName("date_from")] public string? DateFrom { get; set; }
    [JsonPropertyName("date_to")] public string? DateTo { get; set; }
}

public class DataSourceImportResult
{
    [JsonPropertyName("source_name")] public string SourceName { get; set; } = "";
    [JsonPropertyName("source_kind")] public string SourceKind { get; set; } = "";
    [JsonPropertyName("raw_items")] public int RawItems { get; set; }
    [JsonPropertyName("affected_match_ids")] public List<string> AffectedMatchIds { get; set; } = [];
    [JsonPropertyName("baseline_predictions_refreshed")] public int BaselinePredictionsRefreshed { get; set; }
    [JsonPropertyName("import_result")] public DataSnapshotBatchImportResult ImportResult { get; set; } = new();
    [JsonPropertyName("passed")] public bool Passed { get; set; }
    [JsonPropertyName("notes")] public List<string> Notes { get; set; } = [];
}

public class DataSourceAutoCollectionSource
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;
    [JsonPropertyName("source_name")] public string SourceName { get; set; } = "auto_source";
    [JsonPropertyName("provider")] public string? Provider { get; set; }
    [JsonPropertyName("file_path")] public string? FilePath { get; set; }
    [JsonPropertyName("url")] public string? Url { get; set; }
    [JsonPropertyName("match_id")] public string? MatchId { get; set; }
    [JsonPropertyName("object_id")] public string? ObjectId { get; set; }
    [JsonPropertyName("snapshot_type")] public string? SnapshotType { get; set; }
    [JsonPropertyName("query")] public string? Query { get; set; }
    [JsonPropertyName("sport_key")] public string? SportKey { get; set; }
    [JsonPropertyName("competition_code")] public string? CompetitionCode { get; set; }
    [JsonPropertyName("date_from")] public string? DateFrom { get; set; }
    [JsonPropertyName("date_to")] public string? DateTo { get; set; }
    [JsonPropertyName("timeout_seconds")] public int TimeoutSeconds { get; set; } = 90;
}

public class DataSourceAutoCollectionConfig
{
    [JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;
    [JsonPropertyName("interval_minutes")] public int IntervalMinutes { get; set; } = 30;
    [JsonPropertyName("adaptive_schedule_enabled")] public bool AdaptiveScheduleEnabled { get; set; } = true;
    [JsonPropertyName("run_intelligence_triage")] public bool RunIntelligenceTriage { get; set; } = true;
    [JsonPropertyName("trigger_employee_reports")] public bool TriggerEmployeeReports { get; set; } = true;
    [JsonPropertyName("trigger_reports_only_when_new_data")] public bool TriggerReportsOnlyWhenNewData { get; set; } = true;
    [JsonPropertyName("max_report_teams")] public int MaxReportTeams { get; set; } = 8;
    [JsonPropertyName("triage_snapshot_limit")] public int TriageSnapshotLimit { get; set; } = 800;
    [JsonPropertyName("auto_llm_reports_enabled")] public bool AutoLlmReportsEnabled { get; set; }
    [JsonPropertyName("llm_report_interval_minutes")] public int LlmReportIntervalMinutes { get; set; } = 360;
    [JsonPropertyName("max_llm_report_teams")] public int MaxLlmReportTeams { get; set; } = 2;
    [JsonPropertyName("sources")] public List<DataSourceAutoCollectionSource> Sources { get; set; } = [];
}

public class DataSourceAutoCollectionRunResult
{
    [JsonPropertyName("started_at")] public string StartedAt { get; set; } = "";
    [JsonPropertyName("completed_at")] public string CompletedAt { get; set; } = "";
    [JsonPropertyName("running")] public bool Running { get; set; }
    [JsonPropertyName("current_source_id")] public string CurrentSourceId { get; set; } = "";
    [JsonPropertyName("current_source_name")] public string CurrentSourceName { get; set; } = "";
    [JsonPropertyName("elapsed_seconds")] public int ElapsedSeconds { get; set; }
    [JsonPropertyName("next_interval_minutes")] public int NextIntervalMinutes { get; set; }
    [JsonPropertyName("next_interval_reason")] public string NextIntervalReason { get; set; } = "";
    [JsonPropertyName("sources_checked")] public int SourcesChecked { get; set; }
    [JsonPropertyName("sources_succeeded")] public int SourcesSucceeded { get; set; }
    [JsonPropertyName("imported")] public int Imported { get; set; }
    [JsonPropertyName("skipped_duplicates")] public int SkippedDuplicates { get; set; }
    [JsonPropertyName("baseline_predictions_refreshed")] public int BaselinePredictionsRefreshed { get; set; }
    [JsonPropertyName("snapshot_quality")] public DataSnapshotQualityResult SnapshotQuality { get; set; } = new();
    [JsonPropertyName("intelligence_queue_quality")] public IntelligenceQueueQualityResult IntelligenceQueueQuality { get; set; } = new();
    [JsonPropertyName("intelligence_triage")] public IntelligenceTriageResult? IntelligenceTriage { get; set; }
    [JsonPropertyName("employee_report_trigger")] public EmployeeReportTriggerResult? EmployeeReportTrigger { get; set; }
    [JsonPropertyName("source_runs")] public List<DataSourceAutoCollectionSourceRun> SourceRuns { get; set; } = [];
    [JsonPropertyName("results")] public List<DataSourceImportResult> Results { get; set; } = [];
    [JsonPropertyName("passed")] public bool Passed { get; set; }
    [JsonPropertyName("notes")] public List<string> Notes { get; set; } = [];
}

public class DataSourceAutoCollectionSourceRun
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("source_name")] public string SourceName { get; set; } = "";
    [JsonPropertyName("provider")] public string Provider { get; set; } = "";
    [JsonPropertyName("started_at")] public string StartedAt { get; set; } = "";
    [JsonPropertyName("completed_at")] public string CompletedAt { get; set; } = "";
    [JsonPropertyName("elapsed_ms")] public long ElapsedMs { get; set; }
    [JsonPropertyName("timeout_seconds")] public int TimeoutSeconds { get; set; }
    [JsonPropertyName("raw_items")] public int RawItems { get; set; }
    [JsonPropertyName("imported")] public int Imported { get; set; }
    [JsonPropertyName("skipped_duplicates")] public int SkippedDuplicates { get; set; }
    [JsonPropertyName("baseline_predictions_refreshed")] public int BaselinePredictionsRefreshed { get; set; }
    [JsonPropertyName("passed")] public bool Passed { get; set; }
    [JsonPropertyName("error_message")] public string ErrorMessage { get; set; } = "";
    [JsonPropertyName("notes")] public List<string> Notes { get; set; } = [];
}

public class IntelligenceQueueQualityResult
{
    [JsonPropertyName("signals_checked")] public int SignalsChecked { get; set; }
    [JsonPropertyName("actionable_signals")] public int ActionableSignals { get; set; }
    [JsonPropertyName("pending_actionable_signals")] public int PendingActionableSignals { get; set; }
    [JsonPropertyName("non_actionable_pending_review")] public int NonActionablePendingReview { get; set; }
    [JsonPropertyName("missing_object_count")] public int MissingObjectCount { get; set; }
    [JsonPropertyName("missing_evidence_count")] public int MissingEvidenceCount { get; set; }
    [JsonPropertyName("passed")] public bool Passed { get; set; }
    [JsonPropertyName("notes")] public List<string> Notes { get; set; } = [];
}

public class DataSourceProviderHarnessResult
{
    [JsonPropertyName("gnews_guarded")] public bool GnewsGuarded { get; set; }
    [JsonPropertyName("odds_guarded")] public bool OddsGuarded { get; set; }
    [JsonPropertyName("football_data_guarded")] public bool FootballDataGuarded { get; set; }
    [JsonPropertyName("worldcup26_loaded")] public bool Worldcup26Loaded { get; set; }
    [JsonPropertyName("fifa_ranking_loaded")] public bool FifaRankingLoaded { get; set; }
    [JsonPropertyName("world_football_elo_loaded")] public bool WorldFootballEloLoaded { get; set; }
    [JsonPropertyName("international_results_loaded")] public bool InternationalResultsLoaded { get; set; }
    [JsonPropertyName("openfootball_loaded")] public bool OpenfootballLoaded { get; set; }
    [JsonPropertyName("fixturedownload_loaded")] public bool FixtureDownloadLoaded { get; set; }
    [JsonPropertyName("espn_scoreboard_loaded")] public bool EspnScoreboardLoaded { get; set; }
    [JsonPropertyName("espn_summary_loaded")] public bool EspnSummaryLoaded { get; set; }
    [JsonPropertyName("passed")] public bool Passed { get; set; }
    [JsonPropertyName("notes")] public List<string> Notes { get; set; } = [];
}

public class DataSourceQualityProfile
{
    [JsonPropertyName("source_name")] public string SourceName { get; set; } = "";
    [JsonPropertyName("provider")] public string Provider { get; set; } = "";
    [JsonPropertyName("authority_tier")] public string AuthorityTier { get; set; } = "unknown";
    [JsonPropertyName("stability_tier")] public string StabilityTier { get; set; } = "unknown";
    [JsonPropertyName("license_note")] public string LicenseNote { get; set; } = "";
    [JsonPropertyName("best_for")] public List<string> BestFor { get; set; } = [];
    [JsonPropertyName("not_for")] public List<string> NotFor { get; set; } = [];
    [JsonPropertyName("requires_api_key")] public bool RequiresApiKey { get; set; }
    [JsonPropertyName("enabled")] public bool Enabled { get; set; }
    [JsonPropertyName("snapshots")] public int Snapshots { get; set; }
    [JsonPropertyName("unique_hashes")] public int UniqueHashes { get; set; }
    [JsonPropertyName("missing_target_count")] public int MissingTargetCount { get; set; }
    [JsonPropertyName("latest_captured_at")] public string? LatestCapturedAt { get; set; }
    [JsonPropertyName("reliability_score")] public double ReliabilityScore { get; set; }
    [JsonPropertyName("notes")] public List<string> Notes { get; set; } = [];
}

public class MatchPredictionEligibility
{
    [JsonPropertyName("match_id")] public string MatchId { get; set; } = "";
    [JsonPropertyName("home_object_id")] public string HomeObjectId { get; set; } = "";
    [JsonPropertyName("away_object_id")] public string AwayObjectId { get; set; } = "";
    [JsonPropertyName("home_display_name")] public string HomeDisplayName { get; set; } = "";
    [JsonPropertyName("away_display_name")] public string AwayDisplayName { get; set; } = "";
    [JsonPropertyName("eligible")] public bool Eligible { get; set; }
    [JsonPropertyName("reason")] public string Reason { get; set; } = "";
}

public class WorldCupDataReadinessAuditResult
{
    [JsonPropertyName("generated_at")] public string GeneratedAt { get; set; } = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
    [JsonPropertyName("registered_sources")] public List<DataSourceQualityProfile> RegisteredSources { get; set; } = [];
    [JsonPropertyName("match_eligibility")] public List<MatchPredictionEligibility> MatchEligibility { get; set; } = [];
    [JsonPropertyName("total_matches")] public int TotalMatches { get; set; }
    [JsonPropertyName("eligible_matches")] public int EligibleMatches { get; set; }
    [JsonPropertyName("blocked_matches")] public int BlockedMatches { get; set; }
    [JsonPropertyName("demo_or_harness_matches")] public int DemoOrHarnessMatches { get; set; }
    [JsonPropertyName("source_health_passed")] public bool SourceHealthPassed { get; set; }
    [JsonPropertyName("prediction_readiness_passed")] public bool PredictionReadinessPassed { get; set; }
    [JsonPropertyName("passed")] public bool Passed { get; set; }
    [JsonPropertyName("notes")] public List<string> Notes { get; set; } = [];
}

public class WorldCupModelReviewResult
{
    [JsonPropertyName("workflow_run")] public WorkflowRunRecord? WorkflowRun { get; set; }
    [JsonPropertyName("artifact")] public ArtifactRecord? Artifact { get; set; }
    [JsonPropertyName("llm_call")] public LlmCallRecord? LlmCall { get; set; }
    [JsonPropertyName("audit")] public WorldCupDataReadinessAuditResult Audit { get; set; } = new();
    [JsonPropertyName("content")] public string Content { get; set; } = "";
    [JsonPropertyName("passed")] public bool Passed { get; set; }
    [JsonPropertyName("notes")] public List<string> Notes { get; set; } = [];
}

public class WorldCupModelGatewayHealthResult
{
    [JsonPropertyName("generated_at")] public string GeneratedAt { get; set; } = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
    [JsonPropertyName("target_url")] public string TargetUrl { get; set; } = "";
    [JsonPropertyName("checked_endpoint")] public string CheckedEndpoint { get; set; } = "";
    [JsonPropertyName("online")] public bool Online { get; set; }
    [JsonPropertyName("status_code")] public int? StatusCode { get; set; }
    [JsonPropertyName("latency_ms")] public long LatencyMs { get; set; }
    [JsonPropertyName("token_cost")] public int TokenCost { get; set; }
    [JsonPropertyName("message")] public string Message { get; set; } = "";
    [JsonPropertyName("notes")] public List<string> Notes { get; set; } = [];
}

public class CompactEvidenceItem
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("source")] public string Source { get; set; } = "";
    [JsonPropertyName("kind")] public string Kind { get; set; } = "";
    [JsonPropertyName("confidence")] public double Confidence { get; set; }
    [JsonPropertyName("captured_at")] public string CapturedAt { get; set; } = "";
    [JsonPropertyName("summary")] public string Summary { get; set; } = "";
    [JsonPropertyName("url")] public string? Url { get; set; }
}

public class TeamIntelligenceContextPack
{
    [JsonPropertyName("object_id")] public string ObjectId { get; set; } = "";
    [JsonPropertyName("team_name")] public string TeamName { get; set; } = "";
    [JsonPropertyName("symbol")] public string Symbol { get; set; } = "";
    [JsonPropertyName("fifa_rank")] public int? FifaRank { get; set; }
    [JsonPropertyName("group")] public string? Group { get; set; }
    [JsonPropertyName("employee_id")] public string? EmployeeId { get; set; }
    [JsonPropertyName("employee_name")] public string? EmployeeName { get; set; }
    [JsonPropertyName("upcoming_matches")] public List<MatchPredictionEligibility> UpcomingMatches { get; set; } = [];
    [JsonPropertyName("evidence")] public List<CompactEvidenceItem> Evidence { get; set; } = [];
    [JsonPropertyName("source_notes")] public List<string> SourceNotes { get; set; } = [];
    [JsonPropertyName("risks")] public List<string> Risks { get; set; } = [];
    [JsonPropertyName("estimated_tokens")] public int EstimatedTokens { get; set; }
    [JsonPropertyName("passed")] public bool Passed { get; set; }
    [JsonPropertyName("notes")] public List<string> Notes { get; set; } = [];
}

public class TeamContextLlmReviewResult
{
    [JsonPropertyName("context_pack")] public TeamIntelligenceContextPack ContextPack { get; set; } = new();
    [JsonPropertyName("workflow_run")] public WorkflowRunRecord? WorkflowRun { get; set; }
    [JsonPropertyName("artifact")] public ArtifactRecord? Artifact { get; set; }
    [JsonPropertyName("llm_call")] public LlmCallRecord? LlmCall { get; set; }
    [JsonPropertyName("content")] public string Content { get; set; } = "";
    [JsonPropertyName("passed")] public bool Passed { get; set; }
    [JsonPropertyName("notes")] public List<string> Notes { get; set; } = [];
}

public class WorldCupCompanyDashboardResult
{
    [JsonPropertyName("generated_at")] public string GeneratedAt { get; set; } = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
    [JsonPropertyName("company")] public WorldCupCompanySummary Company { get; set; } = new();
    [JsonPropertyName("operations")] public WorldCupOperationsSummary Operations { get; set; } = new();
    [JsonPropertyName("auto_collection")] public WorldCupAutoCollectionSummary AutoCollection { get; set; } = new();
    [JsonPropertyName("recent_activity")] public List<WorldCupActivityFeedItem> RecentActivity { get; set; } = [];
    [JsonPropertyName("active_workflows")] public List<WorkflowRunRecord> ActiveWorkflows { get; set; } = [];
    [JsonPropertyName("passed")] public bool Passed { get; set; }
    [JsonPropertyName("notes")] public List<string> Notes { get; set; } = [];
}

public class WorldCupCompanySummary
{
    [JsonPropertyName("stage")] public string Stage { get; set; } = "pre_tournament";
    [JsonPropertyName("total_teams")] public int TotalTeams { get; set; }
    [JsonPropertyName("active_teams")] public int ActiveTeams { get; set; }
    [JsonPropertyName("eliminated_teams")] public int EliminatedTeams { get; set; }
    [JsonPropertyName("target_employees")] public int TargetEmployees { get; set; } = 52;
    [JsonPropertyName("implemented_employees")] public int ImplementedEmployees { get; set; }
    [JsonPropertyName("active_employees")] public int ActiveEmployees { get; set; }
    [JsonPropertyName("inactive_employees")] public int InactiveEmployees { get; set; }
    [JsonPropertyName("missing_roles")] public List<string> MissingRoles { get; set; } = [];
}

public class WorldCupOperationsSummary
{
    [JsonPropertyName("matches")] public int Matches { get; set; }
    [JsonPropertyName("scheduled_matches")] public int ScheduledMatches { get; set; }
    [JsonPropertyName("finished_matches")] public int FinishedMatches { get; set; }
    [JsonPropertyName("baseline_predictions")] public int BaselinePredictions { get; set; }
    [JsonPropertyName("data_snapshots")] public int DataSnapshots { get; set; }
    [JsonPropertyName("workflow_runs")] public int WorkflowRuns { get; set; }
    [JsonPropertyName("completed_workflows")] public int CompletedWorkflows { get; set; }
    [JsonPropertyName("failed_workflows")] public int FailedWorkflows { get; set; }
    [JsonPropertyName("needs_review_workflows")] public int NeedsReviewWorkflows { get; set; }
    [JsonPropertyName("artifacts")] public int Artifacts { get; set; }
    [JsonPropertyName("llm")] public WorldCupLlmUsageSummary Llm { get; set; } = new();
    [JsonPropertyName("snapshot_quality")] public DataSnapshotQualityResult SnapshotQuality { get; set; } = new();
    [JsonPropertyName("intelligence_queue_quality")] public IntelligenceQueueQualityResult IntelligenceQueueQuality { get; set; } = new();
}

public class WorldCupAutoCollectionSummary
{
    [JsonPropertyName("enabled")] public bool Enabled { get; set; }
    [JsonPropertyName("interval_minutes")] public int IntervalMinutes { get; set; }
    [JsonPropertyName("enabled_sources")] public int EnabledSources { get; set; }
    [JsonPropertyName("last_run_passed")] public bool? LastRunPassed { get; set; }
    [JsonPropertyName("last_run_at")] public string? LastRunAt { get; set; }
    [JsonPropertyName("last_imported")] public int LastImported { get; set; }
    [JsonPropertyName("last_duplicates")] public int LastDuplicates { get; set; }
}

public class WorldCupLlmUsageSummary
{
    [JsonPropertyName("calls")] public int Calls { get; set; }
    [JsonPropertyName("prompt_tokens")] public int PromptTokens { get; set; }
    [JsonPropertyName("completion_tokens")] public int CompletionTokens { get; set; }
    [JsonPropertyName("estimated_cost_usd")] public double EstimatedCostUsd { get; set; }
    [JsonPropertyName("successful_calls")] public int SuccessfulCalls { get; set; }
    [JsonPropertyName("failed_calls")] public int FailedCalls { get; set; }
    [JsonPropertyName("by_employee")] public List<WorldCupLlmUsageEmployeeGroup> ByEmployee { get; set; } = [];
    [JsonPropertyName("by_day")] public List<WorldCupLlmUsageDayGroup> ByDay { get; set; } = [];
    [JsonPropertyName("recent_calls")] public List<LlmCallRecord> RecentCalls { get; set; } = [];
}

public class WorldCupLlmUsageEmployeeGroup
{
    [JsonPropertyName("employee_id")] public string EmployeeId { get; set; } = "unassigned";
    [JsonPropertyName("employee_name")] public string EmployeeName { get; set; } = "unassigned";
    [JsonPropertyName("role")] public string Role { get; set; } = "";
    [JsonPropertyName("calls")] public int Calls { get; set; }
    [JsonPropertyName("prompt_tokens")] public int PromptTokens { get; set; }
    [JsonPropertyName("completion_tokens")] public int CompletionTokens { get; set; }
    [JsonPropertyName("estimated_cost_usd")] public double EstimatedCostUsd { get; set; }
}

public class WorldCupLlmUsageDayGroup
{
    [JsonPropertyName("day")] public string Day { get; set; } = "";
    [JsonPropertyName("calls")] public int Calls { get; set; }
    [JsonPropertyName("prompt_tokens")] public int PromptTokens { get; set; }
    [JsonPropertyName("completion_tokens")] public int CompletionTokens { get; set; }
    [JsonPropertyName("estimated_cost_usd")] public double EstimatedCostUsd { get; set; }
}

public class WorldCupActivityFeedItem
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("time")] public string Time { get; set; } = "";
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("category")] public string Category { get; set; } = "";
    [JsonPropertyName("severity")] public string Severity { get; set; } = "info";
    [JsonPropertyName("from_employee_id")] public string? FromEmployeeId { get; set; }
    [JsonPropertyName("to_employee_id")] public string? ToEmployeeId { get; set; }
    [JsonPropertyName("object_id")] public string? ObjectId { get; set; }
    [JsonPropertyName("match_id")] public string? MatchId { get; set; }
    [JsonPropertyName("workflow_run_id")] public string? WorkflowRunId { get; set; }
    [JsonPropertyName("artifact_id")] public string? ArtifactId { get; set; }
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("message")] public string Message { get; set; } = "";
    [JsonPropertyName("animation_hint")] public string AnimationHint { get; set; } = "log";
}

public class WorldCupBffHarnessResult
{
    [JsonPropertyName("dashboard_passed")] public bool DashboardPassed { get; set; }
    [JsonPropertyName("operations_passed")] public bool OperationsPassed { get; set; }
    [JsonPropertyName("employee_summary_passed")] public bool EmployeeSummaryPassed { get; set; }
    [JsonPropertyName("prediction_accuracy_passed")] public bool PredictionAccuracyPassed { get; set; }
    [JsonPropertyName("memory_summary_passed")] public bool MemorySummaryPassed { get; set; }
    [JsonPropertyName("match_board_passed")] public bool MatchBoardPassed { get; set; }
    [JsonPropertyName("football_teams")] public int FootballTeams { get; set; }
    [JsonPropertyName("matches")] public int Matches { get; set; }
    [JsonPropertyName("employees")] public int Employees { get; set; }
    [JsonPropertyName("employee_fields_enriched")] public bool EmployeeFieldsEnriched { get; set; }
    [JsonPropertyName("match_board_items")] public int MatchBoardItems { get; set; }
    [JsonPropertyName("recent_activity")] public int RecentActivity { get; set; }
    [JsonPropertyName("llm_calls")] public int LlmCalls { get; set; }
    [JsonPropertyName("passed")] public bool Passed { get; set; }
    [JsonPropertyName("notes")] public List<string> Notes { get; set; } = [];
}

public class WorldCupEmployeeStatusSummaryResult
{
    [JsonPropertyName("total")] public int Total { get; set; }
    [JsonPropertyName("target_total")] public int TargetTotal { get; set; } = 52;
    [JsonPropertyName("active")] public int Active { get; set; }
    [JsonPropertyName("inactive")] public int Inactive { get; set; }
    [JsonPropertyName("standby")] public int Standby { get; set; }
    [JsonPropertyName("hibernated")] public int Hibernated { get; set; }
    [JsonPropertyName("missing_roles")] public List<string> MissingRoles { get; set; } = [];
    [JsonPropertyName("employees")] public List<WorldCupEmployeeStatusView> Employees { get; set; } = [];
    [JsonPropertyName("passed")] public bool Passed { get; set; }
    [JsonPropertyName("notes")] public List<string> Notes { get; set; } = [];
}

public class WorldCupEmployeeStatusView
{
    [JsonPropertyName("employee_id")] public string EmployeeId { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("role")] public string Role { get; set; } = "";
    [JsonPropertyName("display_status")] public string DisplayStatus { get; set; } = "active";
    [JsonPropertyName("team_id")] public string? TeamId { get; set; }
    [JsonPropertyName("team_name")] public string? TeamName { get; set; }
    [JsonPropertyName("team_status")] public string? TeamStatus { get; set; }
    [JsonPropertyName("current_task_count")] public int CurrentTaskCount { get; set; }
    [JsonPropertyName("completed_task_count")] public int CompletedTaskCount { get; set; }
    [JsonPropertyName("latest_report_artifact_id")] public string? LatestReportArtifactId { get; set; }
    [JsonPropertyName("pending_actionable_signals")] public int PendingActionableSignals { get; set; }
    [JsonPropertyName("llm_reports_created")] public int LlmReportsCreated { get; set; }
    [JsonPropertyName("accuracy")] public double? Accuracy { get; set; }
    [JsonPropertyName("token_consumed")] public int TokenConsumed { get; set; }
    [JsonPropertyName("estimated_cost_usd")] public double EstimatedCostUsd { get; set; }
    [JsonPropertyName("eliminated_at")] public string? EliminatedAt { get; set; }
    [JsonPropertyName("elimination_reason")] public string? EliminationReason { get; set; }
}

public class WorldCupPredictionAccuracyResult
{
    [JsonPropertyName("overall")] public StrategyEvaluationSummary Overall { get; set; } = new();
    [JsonPropertyName("by_stage")] public List<WorldCupPredictionAccuracyGroup> ByStage { get; set; } = [];
    [JsonPropertyName("by_strategy")] public List<WorldCupPredictionAccuracyGroup> ByStrategy { get; set; } = [];
    [JsonPropertyName("by_team")] public List<WorldCupPredictionAccuracyGroup> ByTeam { get; set; } = [];
    [JsonPropertyName("trend")] public List<WorldCupPredictionAccuracyTrendItem> Trend { get; set; } = [];
    [JsonPropertyName("sample_status")] public string SampleStatus { get; set; } = "no_reviewed_matches";
    [JsonPropertyName("passed")] public bool Passed { get; set; }
    [JsonPropertyName("notes")] public List<string> Notes { get; set; } = [];
}

public class WorldCupPredictionAccuracyGroup
{
    [JsonPropertyName("key")] public string Key { get; set; } = "";
    [JsonPropertyName("label")] public string Label { get; set; } = "";
    [JsonPropertyName("total")] public int Total { get; set; }
    [JsonPropertyName("correct")] public int Correct { get; set; }
    [JsonPropertyName("accuracy")] public double Accuracy { get; set; }
    [JsonPropertyName("average_brier_score")] public double AverageBrierScore { get; set; }
}

public class WorldCupPredictionAccuracyTrendItem
{
    [JsonPropertyName("match_id")] public string MatchId { get; set; } = "";
    [JsonPropertyName("reviewed_at")] public string ReviewedAt { get; set; } = "";
    [JsonPropertyName("cumulative_accuracy")] public double CumulativeAccuracy { get; set; }
    [JsonPropertyName("cumulative_brier_score")] public double CumulativeBrierScore { get; set; }
}

public class MemorySummaryResult
{
    [JsonPropertyName("total")] public int Total { get; set; }
    [JsonPropertyName("by_type")] public Dictionary<string, int> ByType { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    [JsonPropertyName("recent")] public List<MemoryRecord> Recent { get; set; } = [];
    [JsonPropertyName("important")] public List<MemoryRecord> Important { get; set; } = [];
    [JsonPropertyName("passed")] public bool Passed { get; set; }
    [JsonPropertyName("notes")] public List<string> Notes { get; set; } = [];
}

public class WorldCupMatchBoardResult
{
    [JsonPropertyName("total")] public int Total { get; set; }
    [JsonPropertyName("filtered")] public int Filtered { get; set; }
    [JsonPropertyName("stages")] public List<string> Stages { get; set; } = [];
    [JsonPropertyName("groups")] public List<string> Groups { get; set; } = [];
    [JsonPropertyName("items")] public List<WorldCupMatchBoardItem> Items { get; set; } = [];
    [JsonPropertyName("passed")] public bool Passed { get; set; }
    [JsonPropertyName("notes")] public List<string> Notes { get; set; } = [];
}

public class WorldCupMatchBoardItem
{
    [JsonPropertyName("match")] public WorldCupMatch Match { get; set; } = new();
    [JsonPropertyName("home_team")] public WorldCupWatchObject? HomeTeam { get; set; }
    [JsonPropertyName("away_team")] public WorldCupWatchObject? AwayTeam { get; set; }
    [JsonPropertyName("latest_prediction")] public BaselinePredictionRecord? LatestPrediction { get; set; }
    [JsonPropertyName("workflow_count")] public int WorkflowCount { get; set; }
    [JsonPropertyName("artifact_count")] public int ArtifactCount { get; set; }
    [JsonPropertyName("snapshot_count")] public int SnapshotCount { get; set; }
    [JsonPropertyName("has_result")] public bool HasResult { get; set; }
    [JsonPropertyName("display_title")] public string DisplayTitle { get; set; } = "";
}

public class WorldCupPublicDataBootstrapResult
{
    [JsonPropertyName("started_at")] public string StartedAt { get; set; } = "";
    [JsonPropertyName("completed_at")] public string CompletedAt { get; set; } = "";
    [JsonPropertyName("teams_upserted")] public int TeamsUpserted { get; set; }
    [JsonPropertyName("employees_upserted")] public int EmployeesUpserted { get; set; }
    [JsonPropertyName("assignments_upserted")] public int AssignmentsUpserted { get; set; }
    [JsonPropertyName("matches_upserted")] public int MatchesUpserted { get; set; }
    [JsonPropertyName("snapshots_imported")] public int SnapshotsImported { get; set; }
    [JsonPropertyName("snapshot_duplicates")] public int SnapshotDuplicates { get; set; }
    [JsonPropertyName("sources_checked")] public int SourcesChecked { get; set; }
    [JsonPropertyName("passed")] public bool Passed { get; set; }
    [JsonPropertyName("notes")] public List<string> Notes { get; set; } = [];
}

public class DataSnapshotHarnessResult
{
    [JsonPropertyName("snapshots_created")] public int SnapshotsCreated { get; set; }
    [JsonPropertyName("snapshots_recalled")] public int SnapshotsRecalled { get; set; }
    [JsonPropertyName("context_contains_team_intel")] public bool ContextContainsTeamIntel { get; set; }
    [JsonPropertyName("hashes_populated")] public bool HashesPopulated { get; set; }
    [JsonPropertyName("passed")] public bool Passed { get; set; }
    [JsonPropertyName("notes")] public List<string> Notes { get; set; } = [];
}

public class LlmCallRecord
{
    [JsonPropertyName("id")] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [JsonPropertyName("agent_task_id")] public string? AgentTaskId { get; set; }
    [JsonPropertyName("employee_id")] public string? EmployeeId { get; set; }
    [JsonPropertyName("model_name")] public string ModelName { get; set; } = "";
    [JsonPropertyName("provider")] public string Provider { get; set; } = "PiPiClaw";
    [JsonPropertyName("prompt_version")] public string PromptVersion { get; set; } = "worldcup_v0";
    [JsonPropertyName("prompt_tokens")] public int PromptTokens { get; set; }
    [JsonPropertyName("completion_tokens")] public int CompletionTokens { get; set; }
    [JsonPropertyName("cost_estimate")] public double CostEstimate { get; set; }
    [JsonPropertyName("request_hash")] public string RequestHash { get; set; } = "";
    [JsonPropertyName("response_hash")] public string ResponseHash { get; set; } = "";
    [JsonPropertyName("status")] public string Status { get; set; } = "success";
    [JsonPropertyName("error_message")] public string? ErrorMessage { get; set; }
    [JsonPropertyName("created_at")] public string CreatedAt { get; set; } = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
}

public class StepOutputDraft
{
    [JsonPropertyName("step_type")] public string StepType { get; set; } = "";
    [JsonPropertyName("employee_id")] public string? EmployeeId { get; set; }
    [JsonPropertyName("content")] public string Content { get; set; } = "";
    [JsonPropertyName("source")] public string Source { get; set; } = "mock";
    [JsonPropertyName("llm_call_id")] public string? LlmCallId { get; set; }
}

public class MatchWorkflowResult
{
    [JsonPropertyName("workflow_run")] public WorkflowRunRecord WorkflowRun { get; set; } = new();
    [JsonPropertyName("steps")] public List<WorkflowStepRecord> Steps { get; set; } = [];
    [JsonPropertyName("artifact")] public ArtifactRecord Artifact { get; set; } = new();
    [JsonPropertyName("baseline_prediction")] public BaselinePredictionRecord BaselinePrediction { get; set; } = new();
}

public class WorkflowHarnessResult
{
    [JsonPropertyName("workflow_id")] public string WorkflowId { get; set; } = "";
    [JsonPropertyName("match_id")] public string MatchId { get; set; } = "";
    [JsonPropertyName("step_count")] public int StepCount { get; set; }
    [JsonPropertyName("artifact_created")] public bool ArtifactCreated { get; set; }
    [JsonPropertyName("baseline_prediction_found")] public bool BaselinePredictionFound { get; set; }
    [JsonPropertyName("workflow_completed")] public bool WorkflowCompleted { get; set; }
    [JsonPropertyName("passed")] public bool Passed { get; set; }
    [JsonPropertyName("notes")] public List<string> Notes { get; set; } = [];
}

public class ReportQualityHarnessResult
{
    [JsonPropertyName("workflow_id")] public string WorkflowId { get; set; } = "";
    [JsonPropertyName("artifact_id")] public string ArtifactId { get; set; } = "";
    [JsonPropertyName("contains_executive_summary")] public bool ContainsExecutiveSummary { get; set; }
    [JsonPropertyName("contains_probability_section")] public bool ContainsProbabilitySection { get; set; }
    [JsonPropertyName("contains_evidence_section")] public bool ContainsEvidenceSection { get; set; }
    [JsonPropertyName("contains_evidence_trace")] public bool ContainsEvidenceTrace { get; set; }
    [JsonPropertyName("contains_risk_section")] public bool ContainsRiskSection { get; set; }
    [JsonPropertyName("contains_employee_sections")] public bool ContainsEmployeeSections { get; set; }
    [JsonPropertyName("contains_ceo_conclusion")] public bool ContainsCeoConclusion { get; set; }
    [JsonPropertyName("contains_disclaimer")] public bool ContainsDisclaimer { get; set; }
    [JsonPropertyName("passed")] public bool Passed { get; set; }
    [JsonPropertyName("notes")] public List<string> Notes { get; set; } = [];
}

public class IntelligenceContentQualityResult
{
    [JsonPropertyName("artifact_id")] public string ArtifactId { get; set; } = "";
    [JsonPropertyName("object_id")] public string ObjectId { get; set; } = "";
    [JsonPropertyName("content_chars")] public int ContentChars { get; set; }
    [JsonPropertyName("contains_core_judgment")] public bool ContainsCoreJudgment { get; set; }
    [JsonPropertyName("contains_evidence")] public bool ContainsEvidence { get; set; }
    [JsonPropertyName("contains_uncertainty_or_risk")] public bool ContainsUncertaintyOrRisk { get; set; }
    [JsonPropertyName("contains_action")] public bool ContainsAction { get; set; }
    [JsonPropertyName("contains_signal_trace")] public bool ContainsSignalTrace { get; set; }
    [JsonPropertyName("contains_no_betting_guardrail")] public bool ContainsNoBettingGuardrail { get; set; }
    [JsonPropertyName("avoids_forbidden_claims")] public bool AvoidsForbiddenClaims { get; set; }
    [JsonPropertyName("passed")] public bool Passed { get; set; }
    [JsonPropertyName("notes")] public List<string> Notes { get; set; } = [];
}

public class IntelligenceContentQualityHarnessResult
{
    [JsonPropertyName("structured_report")] public IntelligenceContentQualityResult StructuredReport { get; set; } = new();
    [JsonPropertyName("llm_report")] public IntelligenceContentQualityResult LlmReport { get; set; } = new();
    [JsonPropertyName("passed")] public bool Passed { get; set; }
    [JsonPropertyName("notes")] public List<string> Notes { get; set; } = [];
}

public class EngineeringGuardrailHarnessResult
{
    [JsonPropertyName("foreign_keys_enabled")] public bool ForeignKeysEnabled { get; set; }
    [JsonPropertyName("required_indexes_present")] public bool RequiredIndexesPresent { get; set; }
    [JsonPropertyName("invalid_score_rejected")] public bool InvalidScoreRejected { get; set; }
    [JsonPropertyName("fallback_workflow_needs_review")] public bool FallbackWorkflowNeedsReview { get; set; }
    [JsonPropertyName("fallback_step_needs_review")] public bool FallbackStepNeedsReview { get; set; }
    [JsonPropertyName("team_report_memory_expires")] public bool TeamReportMemoryExpires { get; set; }
    [JsonPropertyName("risk_memory_persistent")] public bool RiskMemoryPersistent { get; set; }
    [JsonPropertyName("llm_calls_saved_with_workflow")] public bool LlmCallsSavedWithWorkflow { get; set; }
    [JsonPropertyName("step_llm_call_refs_valid")] public bool StepLlmCallRefsValid { get; set; }
    [JsonPropertyName("token_estimate_valid")] public bool TokenEstimateValid { get; set; }
    [JsonPropertyName("cost_estimate_valid")] public bool CostEstimateValid { get; set; }
    [JsonPropertyName("passed")] public bool Passed { get; set; }
    [JsonPropertyName("notes")] public List<string> Notes { get; set; } = [];
}

public class BaselinePredictionRecord
{
    [JsonPropertyName("id")] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [JsonPropertyName("match_id")] public string MatchId { get; set; } = "";
    [JsonPropertyName("strategy_version")] public string StrategyVersion { get; set; } = "baseline_rank_v0";
    [JsonPropertyName("home_win_probability")] public double HomeWinProbability { get; set; }
    [JsonPropertyName("draw_probability")] public double DrawProbability { get; set; }
    [JsonPropertyName("away_win_probability")] public double AwayWinProbability { get; set; }
    [JsonPropertyName("method")] public string Method { get; set; } = "baseline_rank";
    [JsonPropertyName("input_snapshot_ids_json")] public string InputSnapshotIdsJson { get; set; } = "[]";
    [JsonPropertyName("explanation")] public string Explanation { get; set; } = "";
    [JsonPropertyName("created_at")] public string CreatedAt { get; set; } = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
}

public class WorldCupMatch
{
    [JsonPropertyName("id")] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [JsonPropertyName("stage")] public string Stage { get; set; } = "group";
    [JsonPropertyName("group_name")] public string GroupName { get; set; } = "";
    [JsonPropertyName("home_object_id")] public string HomeObjectId { get; set; } = "";
    [JsonPropertyName("away_object_id")] public string AwayObjectId { get; set; } = "";
    [JsonPropertyName("kickoff_time")] public string KickoffTime { get; set; } = "";
    [JsonPropertyName("venue")] public string Venue { get; set; } = "";
    [JsonPropertyName("status")] public string Status { get; set; } = "scheduled";
    [JsonPropertyName("home_score")] public int? HomeScore { get; set; }
    [JsonPropertyName("away_score")] public int? AwayScore { get; set; }
}

public class BaselineBacktestResult
{
    [JsonPropertyName("matches_checked")] public int MatchesChecked { get; set; }
    [JsonPropertyName("predictions_created")] public int PredictionsCreated { get; set; }
    [JsonPropertyName("invalid_probability_count")] public int InvalidProbabilityCount { get; set; }
    [JsonPropertyName("max_probability_sum_error")] public double MaxProbabilitySumError { get; set; }
    [JsonPropertyName("factor_predictions")] public int FactorPredictions { get; set; }
    [JsonPropertyName("factor_payloads_valid")] public bool FactorPayloadsValid { get; set; }
    [JsonPropertyName("snapshot_aware_predictions")] public int SnapshotAwarePredictions { get; set; }
    [JsonPropertyName("snapshot_payloads_valid")] public bool SnapshotPayloadsValid { get; set; }
    [JsonPropertyName("passed")] public bool Passed { get; set; }
    [JsonPropertyName("notes")] public List<string> Notes { get; set; } = [];
}

public class MatchResultRequest
{
    [JsonPropertyName("match_id")] public string MatchId { get; set; } = "";
    [JsonPropertyName("home_score")] public int HomeScore { get; set; }
    [JsonPropertyName("away_score")] public int AwayScore { get; set; }
}

public class MatchReviewRecord
{
    [JsonPropertyName("match")] public WorldCupMatch Match { get; set; } = new();
    [JsonPropertyName("prediction")] public BaselinePredictionRecord Prediction { get; set; } = new();
    [JsonPropertyName("actual_outcome")] public string ActualOutcome { get; set; } = "";
    [JsonPropertyName("predicted_outcome")] public string PredictedOutcome { get; set; } = "";
    [JsonPropertyName("hit")] public bool Hit { get; set; }
    [JsonPropertyName("brier_score")] public double BrierScore { get; set; }
    [JsonPropertyName("artifact")] public ArtifactRecord Artifact { get; set; } = new();
    [JsonPropertyName("memory")] public MemoryRecord Memory { get; set; } = new();
}

public class MatchReviewHarnessResult
{
    [JsonPropertyName("match_id")] public string MatchId { get; set; } = "";
    [JsonPropertyName("result_recorded")] public bool ResultRecorded { get; set; }
    [JsonPropertyName("review_created")] public bool ReviewCreated { get; set; }
    [JsonPropertyName("artifact_created")] public bool ArtifactCreated { get; set; }
    [JsonPropertyName("memory_written")] public bool MemoryWritten { get; set; }
    [JsonPropertyName("brier_score_valid")] public bool BrierScoreValid { get; set; }
    [JsonPropertyName("passed")] public bool Passed { get; set; }
    [JsonPropertyName("notes")] public List<string> Notes { get; set; } = [];
}

public class StrategyEvaluationItem
{
    [JsonPropertyName("match_id")] public string MatchId { get; set; } = "";
    [JsonPropertyName("home_team")] public string HomeTeam { get; set; } = "";
    [JsonPropertyName("away_team")] public string AwayTeam { get; set; } = "";
    [JsonPropertyName("score")] public string Score { get; set; } = "";
    [JsonPropertyName("actual_outcome")] public string ActualOutcome { get; set; } = "";
    [JsonPropertyName("predicted_outcome")] public string PredictedOutcome { get; set; } = "";
    [JsonPropertyName("hit")] public bool Hit { get; set; }
    [JsonPropertyName("brier_score")] public double BrierScore { get; set; }
    [JsonPropertyName("reviewed_at")] public string ReviewedAt { get; set; } = "";
}

public class StrategyEvaluationSummary
{
    [JsonPropertyName("strategy_version")] public string StrategyVersion { get; set; } = "baseline_rank_v0";
    [JsonPropertyName("reviewed_matches")] public int ReviewedMatches { get; set; }
    [JsonPropertyName("hit_count")] public int HitCount { get; set; }
    [JsonPropertyName("hit_rate")] public double HitRate { get; set; }
    [JsonPropertyName("average_brier_score")] public double AverageBrierScore { get; set; }
    [JsonPropertyName("latest_reviewed_at")] public string? LatestReviewedAt { get; set; }
    [JsonPropertyName("items")] public List<StrategyEvaluationItem> Items { get; set; } = [];
}

public class StrategyEvaluationHarnessResult
{
    [JsonPropertyName("reviewed_matches")] public int ReviewedMatches { get; set; }
    [JsonPropertyName("hit_rate_valid")] public bool HitRateValid { get; set; }
    [JsonPropertyName("average_brier_valid")] public bool AverageBrierValid { get; set; }
    [JsonPropertyName("contains_demo_match")] public bool ContainsDemoMatch { get; set; }
    [JsonPropertyName("passed")] public bool Passed { get; set; }
    [JsonPropertyName("notes")] public List<string> Notes { get; set; } = [];
}

public class ModelBacktestResult
{
    [JsonPropertyName("strategy_version")] public string StrategyVersion { get; set; } = "";
    [JsonPropertyName("source")] public string Source { get; set; } = "";
    [JsonPropertyName("date_from")] public string DateFrom { get; set; } = "";
    [JsonPropertyName("date_to")] public string DateTo { get; set; } = "";
    [JsonPropertyName("samples_checked")] public int SamplesChecked { get; set; }
    [JsonPropertyName("samples_used")] public int SamplesUsed { get; set; }
    [JsonPropertyName("skipped_unmapped")] public int SkippedUnmapped { get; set; }
    [JsonPropertyName("top1_hit_count")] public int Top1HitCount { get; set; }
    [JsonPropertyName("top1_hit_rate")] public double Top1HitRate { get; set; }
    [JsonPropertyName("average_brier_score")] public double AverageBrierScore { get; set; }
    [JsonPropertyName("average_log_loss")] public double AverageLogLoss { get; set; }
    [JsonPropertyName("draw_samples")] public int DrawSamples { get; set; }
    [JsonPropertyName("draw_hit_count")] public int DrawHitCount { get; set; }
    [JsonPropertyName("draw_recall")] public double DrawRecall { get; set; }
    [JsonPropertyName("favorite_samples")] public int FavoriteSamples { get; set; }
    [JsonPropertyName("favorite_hit_rate")] public double FavoriteHitRate { get; set; }
    [JsonPropertyName("buckets")] public List<ModelBacktestBucket> Buckets { get; set; } = [];
    [JsonPropertyName("sample_items")] public List<ModelBacktestItem> SampleItems { get; set; } = [];
    [JsonPropertyName("passed")] public bool Passed { get; set; }
    [JsonPropertyName("notes")] public List<string> Notes { get; set; } = [];
}

public class ModelBacktestBucket
{
    [JsonPropertyName("label")] public string Label { get; set; } = "";
    [JsonPropertyName("samples")] public int Samples { get; set; }
    [JsonPropertyName("hit_rate")] public double HitRate { get; set; }
    [JsonPropertyName("average_brier_score")] public double AverageBrierScore { get; set; }
}

public class ModelBacktestItem
{
    [JsonPropertyName("date")] public string Date { get; set; } = "";
    [JsonPropertyName("home_team")] public string HomeTeam { get; set; } = "";
    [JsonPropertyName("away_team")] public string AwayTeam { get; set; } = "";
    [JsonPropertyName("score")] public string Score { get; set; } = "";
    [JsonPropertyName("tournament")] public string Tournament { get; set; } = "";
    [JsonPropertyName("actual_outcome")] public string ActualOutcome { get; set; } = "";
    [JsonPropertyName("predicted_outcome")] public string PredictedOutcome { get; set; } = "";
    [JsonPropertyName("hit")] public bool Hit { get; set; }
    [JsonPropertyName("home_win_probability")] public double HomeWinProbability { get; set; }
    [JsonPropertyName("draw_probability")] public double DrawProbability { get; set; }
    [JsonPropertyName("away_win_probability")] public double AwayWinProbability { get; set; }
    [JsonPropertyName("brier_score")] public double BrierScore { get; set; }
    [JsonPropertyName("log_loss")] public double LogLoss { get; set; }
}

public class DemoResultsHarnessResult
{
    [JsonPropertyName("results_recorded")] public int ResultsRecorded { get; set; }
    [JsonPropertyName("reviews_created")] public int ReviewsCreated { get; set; }
    [JsonPropertyName("evaluation")] public StrategyEvaluationSummary Evaluation { get; set; } = new();
    [JsonPropertyName("passed")] public bool Passed { get; set; }
    [JsonPropertyName("notes")] public List<string> Notes { get; set; } = [];
}

public class WorldCupLifecycleResult
{
    [JsonPropertyName("match_id")] public string MatchId { get; set; } = "";
    [JsonPropertyName("stage")] public string Stage { get; set; } = "";
    [JsonPropertyName("applied")] public bool Applied { get; set; }
    [JsonPropertyName("winner_object_id")] public string? WinnerObjectId { get; set; }
    [JsonPropertyName("loser_object_id")] public string? LoserObjectId { get; set; }
    [JsonPropertyName("offboarded_employee_id")] public string? OffboardedEmployeeId { get; set; }
    [JsonPropertyName("memory_id")] public string? MemoryId { get; set; }
    [JsonPropertyName("notes")] public List<string> Notes { get; set; } = [];
}

public class WorldCupLifecycleHarnessResult
{
    [JsonPropertyName("lifecycle")] public WorldCupLifecycleResult Lifecycle { get; set; } = new();
    [JsonPropertyName("loser_team_eliminated")] public bool LoserTeamEliminated { get; set; }
    [JsonPropertyName("winner_team_active")] public bool WinnerTeamActive { get; set; }
    [JsonPropertyName("employee_offboarded")] public bool EmployeeOffboarded { get; set; }
    [JsonPropertyName("assignment_ended")] public bool AssignmentEnded { get; set; }
    [JsonPropertyName("memory_written")] public bool MemoryWritten { get; set; }
    [JsonPropertyName("passed")] public bool Passed { get; set; }
    [JsonPropertyName("notes")] public List<string> Notes { get; set; } = [];
}

public class WorldCupStoreStatus
{
    [JsonPropertyName("database_path")] public string DatabasePath { get; set; } = "";
    [JsonPropertyName("watch_objects")] public int WatchObjects { get; set; }
    [JsonPropertyName("employees")] public int Employees { get; set; }
    [JsonPropertyName("assignments")] public int Assignments { get; set; }
    [JsonPropertyName("workflow_runs")] public int WorkflowRuns { get; set; }
    [JsonPropertyName("llm_calls")] public int LlmCalls { get; set; }
    [JsonPropertyName("data_snapshots")] public int DataSnapshots { get; set; }
    [JsonPropertyName("matches")] public int Matches { get; set; }
    [JsonPropertyName("baseline_predictions")] public int BaselinePredictions { get; set; }
}

public class WorldCupSystemEventLog
{
    [JsonPropertyName("id")] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [JsonPropertyName("event_type")] public string EventType { get; set; } = "";
    [JsonPropertyName("category")] public string Category { get; set; } = "system";
    [JsonPropertyName("severity")] public string Severity { get; set; } = "info";
    [JsonPropertyName("source")] public string Source { get; set; } = "";
    [JsonPropertyName("employee_id")] public string? EmployeeId { get; set; }
    [JsonPropertyName("object_id")] public string? ObjectId { get; set; }
    [JsonPropertyName("match_id")] public string? MatchId { get; set; }
    [JsonPropertyName("workflow_run_id")] public string? WorkflowRunId { get; set; }
    [JsonPropertyName("llm_call_id")] public string? LlmCallId { get; set; }
    [JsonPropertyName("snapshot_id")] public string? SnapshotId { get; set; }
    [JsonPropertyName("artifact_id")] public string? ArtifactId { get; set; }
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("message")] public string Message { get; set; } = "";
    [JsonPropertyName("payload_json")] public string PayloadJson { get; set; } = "{}";
    [JsonPropertyName("content_hash")] public string ContentHash { get; set; } = "";
    [JsonPropertyName("created_at")] public string CreatedAt { get; set; } = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
}

public class WorldCupSystemEventLogHarnessResult
{
    [JsonPropertyName("event_written")] public bool EventWritten { get; set; }
    [JsonPropertyName("event_recalled")] public bool EventRecalled { get; set; }
    [JsonPropertyName("category_filter_works")] public bool CategoryFilterWorks { get; set; }
    [JsonPropertyName("entity_filter_works")] public bool EntityFilterWorks { get; set; }
    [JsonPropertyName("workflow_events_written")] public bool WorkflowEventsWritten { get; set; }
    [JsonPropertyName("employee_events_written")] public bool EmployeeEventsWritten { get; set; }
    [JsonPropertyName("passed")] public bool Passed { get; set; }
    [JsonPropertyName("notes")] public List<string> Notes { get; set; } = [];
}

public class IntelligenceSignalRecord
{
    [JsonPropertyName("id")] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [JsonPropertyName("source_snapshot_id")] public string SourceSnapshotId { get; set; } = "";
    [JsonPropertyName("signal_type")] public string SignalType { get; set; } = "general_intel";
    [JsonPropertyName("severity")] public string Severity { get; set; } = "low";
    [JsonPropertyName("confidence")] public double Confidence { get; set; } = 0.5;
    [JsonPropertyName("object_id")] public string? ObjectId { get; set; }
    [JsonPropertyName("match_id")] public string? MatchId { get; set; }
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("summary")] public string Summary { get; set; } = "";
    [JsonPropertyName("evidence_json")] public string EvidenceJson { get; set; } = "{}";
    [JsonPropertyName("status")] public string Status { get; set; } = "needs_ai_review";
    [JsonPropertyName("content_hash")] public string ContentHash { get; set; } = "";
    [JsonPropertyName("created_at")] public string CreatedAt { get; set; } = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
    [JsonPropertyName("updated_at")] public string UpdatedAt { get; set; } = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
}

public class IntelligenceTriageResult
{
    [JsonPropertyName("snapshots_checked")] public int SnapshotsChecked { get; set; }
    [JsonPropertyName("signals_created")] public int SignalsCreated { get; set; }
    [JsonPropertyName("duplicates_skipped")] public int DuplicatesSkipped { get; set; }
    [JsonPropertyName("needs_ai_review")] public int NeedsAiReview { get; set; }
    [JsonPropertyName("signals")] public List<IntelligenceSignalRecord> Signals { get; set; } = [];
    [JsonPropertyName("passed")] public bool Passed { get; set; }
    [JsonPropertyName("notes")] public List<string> Notes { get; set; } = [];
}

public class EmployeeReportTriggerResult
{
    [JsonPropertyName("signals_considered")] public int SignalsConsidered { get; set; }
    [JsonPropertyName("teams_triggered")] public int TeamsTriggered { get; set; }
    [JsonPropertyName("reports_created")] public int ReportsCreated { get; set; }
    [JsonPropertyName("artifacts")] public List<ArtifactRecord> Artifacts { get; set; } = [];
    [JsonPropertyName("passed")] public bool Passed { get; set; }
    [JsonPropertyName("notes")] public List<string> Notes { get; set; } = [];
}

public class EmployeeReportBudgetEstimate
{
    [JsonPropertyName("signals_considered")] public int SignalsConsidered { get; set; }
    [JsonPropertyName("teams_considered")] public int TeamsConsidered { get; set; }
    [JsonPropertyName("max_teams")] public int MaxTeams { get; set; }
    [JsonPropertyName("estimated_prompt_tokens")] public int EstimatedPromptTokens { get; set; }
    [JsonPropertyName("estimated_completion_tokens")] public int EstimatedCompletionTokens { get; set; }
    [JsonPropertyName("estimated_cost_usd")] public double EstimatedCostUsd { get; set; }
    [JsonPropertyName("passed")] public bool Passed { get; set; }
    [JsonPropertyName("notes")] public List<string> Notes { get; set; } = [];
}

public class TeamIntelligenceLlmReportResult
{
    [JsonPropertyName("object_id")] public string ObjectId { get; set; } = "";
    [JsonPropertyName("employee_id")] public string? EmployeeId { get; set; }
    [JsonPropertyName("signals_used")] public int SignalsUsed { get; set; }
    [JsonPropertyName("workflow_run")] public WorkflowRunRecord? WorkflowRun { get; set; }
    [JsonPropertyName("artifact")] public ArtifactRecord? Artifact { get; set; }
    [JsonPropertyName("llm_call")] public LlmCallRecord? LlmCall { get; set; }
    [JsonPropertyName("passed")] public bool Passed { get; set; }
    [JsonPropertyName("notes")] public List<string> Notes { get; set; } = [];
}

public class AutoLlmReportRunResult
{
    [JsonPropertyName("started_at")] public string StartedAt { get; set; } = "";
    [JsonPropertyName("completed_at")] public string CompletedAt { get; set; } = "";
    [JsonPropertyName("enabled")] public bool Enabled { get; set; }
    [JsonPropertyName("teams_checked")] public int TeamsChecked { get; set; }
    [JsonPropertyName("reports_created")] public int ReportsCreated { get; set; }
    [JsonPropertyName("failed_reports")] public int FailedReports { get; set; }
    [JsonPropertyName("estimated_cost_usd")] public double EstimatedCostUsd { get; set; }
    [JsonPropertyName("results")] public List<TeamIntelligenceLlmReportResult> Results { get; set; } = [];
    [JsonPropertyName("passed")] public bool Passed { get; set; }
    [JsonPropertyName("notes")] public List<string> Notes { get; set; } = [];
}

public class TeamWorkbenchResult
{
    [JsonPropertyName("object_id")] public string ObjectId { get; set; } = "";
    [JsonPropertyName("team")] public WorldCupWatchObject? Team { get; set; }
    [JsonPropertyName("employee")] public WorldCupEmployee? Employee { get; set; }
    [JsonPropertyName("signals")] public List<IntelligenceSignalRecord> Signals { get; set; } = [];
    [JsonPropertyName("snapshots")] public List<DataSnapshotRecord> Snapshots { get; set; } = [];
    [JsonPropertyName("logs")] public List<WorldCupSystemEventLog> Logs { get; set; } = [];
    [JsonPropertyName("workflows")] public List<WorkflowRunRecord> Workflows { get; set; } = [];
    [JsonPropertyName("artifacts")] public List<ArtifactRecord> Artifacts { get; set; } = [];
    [JsonPropertyName("latest_report")] public ArtifactContent? LatestReport { get; set; }
    [JsonPropertyName("llm_budget")] public EmployeeReportBudgetEstimate LlmBudget { get; set; } = new();
    [JsonPropertyName("pending_actionable_signals")] public int PendingActionableSignals { get; set; }
    [JsonPropertyName("llm_reports_created")] public int LlmReportsCreated { get; set; }
    [JsonPropertyName("passed")] public bool Passed { get; set; }
    [JsonPropertyName("notes")] public List<string> Notes { get; set; } = [];
}

public class TeamWorkbenchHarnessResult
{
    [JsonPropertyName("team_found")] public bool TeamFound { get; set; }
    [JsonPropertyName("employee_found")] public bool EmployeeFound { get; set; }
    [JsonPropertyName("signals_loaded")] public bool SignalsLoaded { get; set; }
    [JsonPropertyName("snapshots_loaded")] public bool SnapshotsLoaded { get; set; }
    [JsonPropertyName("logs_loaded")] public bool LogsLoaded { get; set; }
    [JsonPropertyName("artifacts_loaded")] public bool ArtifactsLoaded { get; set; }
    [JsonPropertyName("latest_report_loaded")] public bool LatestReportLoaded { get; set; }
    [JsonPropertyName("budget_loaded")] public bool BudgetLoaded { get; set; }
    [JsonPropertyName("passed")] public bool Passed { get; set; }
    [JsonPropertyName("notes")] public List<string> Notes { get; set; } = [];
}

public class IntelligenceWorkflowHarnessResult
{
    [JsonPropertyName("signal_created")] public bool SignalCreated { get; set; }
    [JsonPropertyName("signal_recalled")] public bool SignalRecalled { get; set; }
    [JsonPropertyName("report_triggered")] public bool ReportTriggered { get; set; }
    [JsonPropertyName("artifact_created")] public bool ArtifactCreated { get; set; }
    [JsonPropertyName("events_logged")] public bool EventsLogged { get; set; }
    [JsonPropertyName("non_actionable_report_skipped")] public bool NonActionableReportSkipped { get; set; } = true;
    [JsonPropertyName("broad_article_skipped")] public bool BroadArticleSkipped { get; set; } = true;
    [JsonPropertyName("targeted_feed_noise_skipped")] public bool TargetedFeedNoiseSkipped { get; set; } = true;
    [JsonPropertyName("passed")] public bool Passed { get; set; }
    [JsonPropertyName("notes")] public List<string> Notes { get; set; } = [];
}

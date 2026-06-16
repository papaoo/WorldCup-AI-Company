using System.Text.Json;
using System.Text.Json.Serialization;

namespace PiPiClaw.Team;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(AppConfig))]
[JsonSerializable(typeof(ChatRequest))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(ChatResponse))]
[JsonSerializable(typeof(NodeInfo))]
[JsonSerializable(typeof(CreateCompanyReq))]
[JsonSerializable(typeof(NodeInfoTemplate))]
[JsonSerializable(typeof(List<NodeInfoTemplate>))]
[JsonSerializable(typeof(Dictionary<string, NodeInfo>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(Dictionary<string, JsonElement>))]
[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(CompanySetupResult))]
[JsonSerializable(typeof(List<JsonElement>))]
[JsonSerializable(typeof(ProjectBoard))]
[JsonSerializable(typeof(ProjectTask))]
[JsonSerializable(typeof(List<ProjectTask>))]
[JsonSerializable(typeof(List<ProjectBoard>))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(WorldCupStoreStatus))]
[JsonSerializable(typeof(WorldCupWatchObject))]
[JsonSerializable(typeof(List<WorldCupWatchObject>))]
[JsonSerializable(typeof(WorldCupEmployee))]
[JsonSerializable(typeof(List<WorldCupEmployee>))]
[JsonSerializable(typeof(EmployeeAssignment))]
[JsonSerializable(typeof(List<EmployeeAssignment>))]
[JsonSerializable(typeof(WorldCupMatch))]
[JsonSerializable(typeof(List<WorldCupMatch>))]
[JsonSerializable(typeof(BaselinePredictionRecord))]
[JsonSerializable(typeof(List<BaselinePredictionRecord>))]
[JsonSerializable(typeof(BaselineBacktestResult))]
[JsonSerializable(typeof(WorkflowRunRecord))]
[JsonSerializable(typeof(List<WorkflowRunRecord>))]
[JsonSerializable(typeof(WorkflowStepRecord))]
[JsonSerializable(typeof(List<WorkflowStepRecord>))]
[JsonSerializable(typeof(ArtifactRecord))]
[JsonSerializable(typeof(List<ArtifactRecord>))]
[JsonSerializable(typeof(MatchWorkflowResult))]
[JsonSerializable(typeof(WorkflowHarnessResult))]
[JsonSerializable(typeof(ReportQualityHarnessResult))]
[JsonSerializable(typeof(IntelligenceContentQualityResult))]
[JsonSerializable(typeof(IntelligenceContentQualityHarnessResult))]
[JsonSerializable(typeof(EngineeringGuardrailHarnessResult))]
[JsonSerializable(typeof(LlmCallRecord))]
[JsonSerializable(typeof(List<LlmCallRecord>))]
[JsonSerializable(typeof(StepOutputDraft))]
[JsonSerializable(typeof(List<StepOutputDraft>))]
[JsonSerializable(typeof(ArtifactContent))]
[JsonSerializable(typeof(MemoryRecord))]
[JsonSerializable(typeof(List<MemoryRecord>))]
[JsonSerializable(typeof(MemoryCreateRequest))]
[JsonSerializable(typeof(MemoryHarnessResult))]
[JsonSerializable(typeof(DataSnapshotRecord))]
[JsonSerializable(typeof(List<DataSnapshotRecord>))]
[JsonSerializable(typeof(DataSnapshotCreateRequest))]
[JsonSerializable(typeof(DataSnapshotBatchImportRequest))]
[JsonSerializable(typeof(DataSnapshotBatchImportResult))]
[JsonSerializable(typeof(DataSnapshotMaintenanceResult))]
[JsonSerializable(typeof(DataSnapshotQualityResult))]
[JsonSerializable(typeof(DataSnapshotQualityHarnessResult))]
[JsonSerializable(typeof(DataSourceImportRequest))]
[JsonSerializable(typeof(DataSourceImportResult))]
[JsonSerializable(typeof(DataSourceAutoCollectionSource))]
[JsonSerializable(typeof(List<DataSourceAutoCollectionSource>))]
[JsonSerializable(typeof(DataSourceAutoCollectionConfig))]
[JsonSerializable(typeof(DataSourceAutoCollectionRunResult))]
[JsonSerializable(typeof(DataSourceAutoCollectionSourceRun))]
[JsonSerializable(typeof(List<DataSourceAutoCollectionSourceRun>))]
[JsonSerializable(typeof(IntelligenceQueueQualityResult))]
[JsonSerializable(typeof(DataSourceProviderHarnessResult))]
[JsonSerializable(typeof(DataSourceQualityProfile))]
[JsonSerializable(typeof(List<DataSourceQualityProfile>))]
[JsonSerializable(typeof(MatchPredictionEligibility))]
[JsonSerializable(typeof(List<MatchPredictionEligibility>))]
[JsonSerializable(typeof(WorldCupDataReadinessAuditResult))]
[JsonSerializable(typeof(WorldCupModelReviewResult))]
[JsonSerializable(typeof(WorldCupModelGatewayHealthResult))]
[JsonSerializable(typeof(CompactEvidenceItem))]
[JsonSerializable(typeof(List<CompactEvidenceItem>))]
[JsonSerializable(typeof(TeamIntelligenceContextPack))]
[JsonSerializable(typeof(TeamContextLlmReviewResult))]
[JsonSerializable(typeof(WorldCupCompanyDashboardResult))]
[JsonSerializable(typeof(WorldCupCompanySummary))]
[JsonSerializable(typeof(WorldCupOperationsSummary))]
[JsonSerializable(typeof(WorldCupAutoCollectionSummary))]
[JsonSerializable(typeof(WorldCupLlmUsageSummary))]
[JsonSerializable(typeof(WorldCupLlmUsageEmployeeGroup))]
[JsonSerializable(typeof(List<WorldCupLlmUsageEmployeeGroup>))]
[JsonSerializable(typeof(WorldCupLlmUsageDayGroup))]
[JsonSerializable(typeof(List<WorldCupLlmUsageDayGroup>))]
[JsonSerializable(typeof(WorldCupActivityFeedItem))]
[JsonSerializable(typeof(List<WorldCupActivityFeedItem>))]
[JsonSerializable(typeof(WorldCupBffHarnessResult))]
[JsonSerializable(typeof(WorldCupEmployeeStatusSummaryResult))]
[JsonSerializable(typeof(WorldCupEmployeeStatusView))]
[JsonSerializable(typeof(List<WorldCupEmployeeStatusView>))]
[JsonSerializable(typeof(WorldCupPredictionAccuracyResult))]
[JsonSerializable(typeof(WorldCupPredictionAccuracyGroup))]
[JsonSerializable(typeof(List<WorldCupPredictionAccuracyGroup>))]
[JsonSerializable(typeof(WorldCupPredictionAccuracyTrendItem))]
[JsonSerializable(typeof(List<WorldCupPredictionAccuracyTrendItem>))]
[JsonSerializable(typeof(MemorySummaryResult))]
[JsonSerializable(typeof(WorldCupMatchBoardResult))]
[JsonSerializable(typeof(WorldCupMatchBoardItem))]
[JsonSerializable(typeof(List<WorldCupMatchBoardItem>))]
[JsonSerializable(typeof(WorldCupPublicDataBootstrapResult))]
[JsonSerializable(typeof(DataSnapshotHarnessResult))]
[JsonSerializable(typeof(MatchResultRequest))]
[JsonSerializable(typeof(MatchReviewRecord))]
[JsonSerializable(typeof(MatchReviewHarnessResult))]
[JsonSerializable(typeof(StrategyEvaluationItem))]
[JsonSerializable(typeof(List<StrategyEvaluationItem>))]
[JsonSerializable(typeof(StrategyEvaluationSummary))]
[JsonSerializable(typeof(StrategyEvaluationHarnessResult))]
[JsonSerializable(typeof(ModelBacktestResult))]
[JsonSerializable(typeof(ModelBacktestBucket))]
[JsonSerializable(typeof(List<ModelBacktestBucket>))]
[JsonSerializable(typeof(ModelBacktestItem))]
[JsonSerializable(typeof(List<ModelBacktestItem>))]
[JsonSerializable(typeof(DemoResultsHarnessResult))]
[JsonSerializable(typeof(WorldCupLifecycleResult))]
[JsonSerializable(typeof(WorldCupLifecycleHarnessResult))]
[JsonSerializable(typeof(WorldCupSystemEventLog))]
[JsonSerializable(typeof(List<WorldCupSystemEventLog>))]
[JsonSerializable(typeof(WorldCupSystemEventLogHarnessResult))]
[JsonSerializable(typeof(IntelligenceSignalRecord))]
[JsonSerializable(typeof(List<IntelligenceSignalRecord>))]
[JsonSerializable(typeof(IntelligenceTriageResult))]
[JsonSerializable(typeof(EmployeeReportTriggerResult))]
[JsonSerializable(typeof(EmployeeReportBudgetEstimate))]
[JsonSerializable(typeof(TeamIntelligenceLlmReportResult))]
[JsonSerializable(typeof(List<TeamIntelligenceLlmReportResult>))]
[JsonSerializable(typeof(AutoLlmReportRunResult))]
[JsonSerializable(typeof(TeamWorkbenchResult))]
[JsonSerializable(typeof(TeamWorkbenchHarnessResult))]
[JsonSerializable(typeof(IntelligenceWorkflowHarnessResult))]
[JsonSerializable(typeof(ProductTeamView))]
[JsonSerializable(typeof(List<ProductTeamView>))]
[JsonSerializable(typeof(ProductTeamProfileView))]
[JsonSerializable(typeof(ProductTeamResearchItem))]
[JsonSerializable(typeof(List<ProductTeamResearchItem>))]
[JsonSerializable(typeof(ProductTeamResearchResult))]
[JsonSerializable(typeof(ProductRadarMetricView))]
[JsonSerializable(typeof(List<ProductRadarMetricView>))]
[JsonSerializable(typeof(ProductEmployeeView))]
[JsonSerializable(typeof(ProductProbabilityView))]
[JsonSerializable(typeof(ProductPredictionPhaseView))]
[JsonSerializable(typeof(ProductBettingAdviceView))]
[JsonSerializable(typeof(ProductPredictionQualityGateView))]
[JsonSerializable(typeof(ProductPredictionSourceCheckView))]
[JsonSerializable(typeof(ProductProbabilityFactorView))]
[JsonSerializable(typeof(List<ProductProbabilityFactorView>))]
[JsonSerializable(typeof(ProductMarketSignalView))]
[JsonSerializable(typeof(List<ProductMarketSignalView>))]
[JsonSerializable(typeof(ProductPredictionRuleView))]
[JsonSerializable(typeof(ProductMetricView))]
[JsonSerializable(typeof(List<ProductMetricView>))]
[JsonSerializable(typeof(ProductEvidenceView))]
[JsonSerializable(typeof(List<ProductEvidenceView>))]
[JsonSerializable(typeof(ProductMemoryView))]
[JsonSerializable(typeof(List<ProductMemoryView>))]
[JsonSerializable(typeof(ProductActivityView))]
[JsonSerializable(typeof(List<ProductActivityView>))]
[JsonSerializable(typeof(ProductMatchQueueItem))]
[JsonSerializable(typeof(List<ProductMatchQueueItem>))]
[JsonSerializable(typeof(ProductMatchDetail))]
[JsonSerializable(typeof(ProductCollectionPriorityView))]
[JsonSerializable(typeof(List<ProductCollectionPriorityView>))]
[JsonSerializable(typeof(ProductPrematchWatchPlanItem))]
[JsonSerializable(typeof(List<ProductPrematchWatchPlanItem>))]
[JsonSerializable(typeof(ProductDataCoverageItem))]
[JsonSerializable(typeof(List<ProductDataCoverageItem>))]
[JsonSerializable(typeof(ProductDataTrustItem))]
[JsonSerializable(typeof(List<ProductDataTrustItem>))]
[JsonSerializable(typeof(ProductAutoCollectionStatus))]
[JsonSerializable(typeof(ProductAutoCollectionSourceRun))]
[JsonSerializable(typeof(List<ProductAutoCollectionSourceRun>))]
[JsonSerializable(typeof(ProductModelHealthResult))]
[JsonSerializable(typeof(ProductOverviewResult))]
[JsonSerializable(typeof(ProductAuditResult))]
internal partial class AppJsonContext : JsonSerializerContext { }

public class CreateCompanyReq
{
    public string? Description { get; set; }
    public string? MasterNodeUrl { get; set; }
}

public class CompanySetupResult
{
    public string? Profile { get; set; }
    public List<NodeInfoTemplate>? Employees { get; set; }
}

public class NodeInfoTemplate
{
    public string? name { get; set; }
    public string? Role { get; set; }
    public string? Url { get; set; }
    public string? Description { get; set; }
    public string? Resume { get; set; }
    public int ModelIndex { get; set; } = 0;
    public List<string> Contacts { get; set; } = [];
}

public class ChatRequest
{
    public string? message { get; set; }
    public int modelIndex { get; set; }
    public string? sop { get; set; }
    public string? caller { get; set; }
    public string? taskId { get; set; }
}

public class ChatResponse
{
    public string? type { get; set; }
    public string? content { get; set; }
}

public class NodeInfo
{
    [JsonPropertyName("Name")] public string? Name { get; set; }
    [JsonPropertyName("Url")] public string? Url { get; set; }
    [JsonPropertyName("Role")] public string? Role { get; set; }
    [JsonPropertyName("Description")] public string? Description { get; set; }
    [JsonPropertyName("Resume")] public string? Resume { get; set; }
    [JsonPropertyName("ModelIndex")] public int ModelIndex { get; set; } = 0;
    [JsonPropertyName("Contacts")] public List<string> Contacts { get; set; } = [];
}

public class ProjectTask
{
    [JsonPropertyName("id")] public string Id { get; set; } = Guid.NewGuid().ToString("N").Substring(0, 8);
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("assignee")] public string Assignee { get; set; } = "";
    [JsonPropertyName("status")] public string Status { get; set; } = "todo";
    [JsonPropertyName("update_time")] public string UpdateTime { get; set; } = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
    [JsonPropertyName("result")] public string Result { get; set; } = "";
}

public class ProjectBoard
{
    [JsonPropertyName("project_name")] public string? ProjectName { get; set; }
    [JsonPropertyName("tasks")] public List<ProjectTask> Tasks { get; set; } = [];
}

public class AppConfig
{
    public string? CompanyProfile { get; set; }
    public string CompanyName { get; set; } = "未命名皮皮虾公司";
    public bool HasLicense { get; set; } = false;
    public string MasterNodeUrl { get; set; } = "http://127.0.0.1:5050";
    public Dictionary<string, NodeInfo> PeerNodes { get; set; } = new();
    public string? CompanySOP { get; set; }
    public List<ProjectBoard> Projects { get; set; } = new();
}

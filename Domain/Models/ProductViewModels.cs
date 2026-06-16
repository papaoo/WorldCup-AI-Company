using System.Text.Json.Serialization;

namespace PiPiClaw.Team;

public class ProductTeamView
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("name_cn")] public string NameCn { get; set; } = "";
    [JsonPropertyName("code")] public string Code { get; set; } = "";
    [JsonPropertyName("group")] public string Group { get; set; } = "";
    [JsonPropertyName("fifa_rank")] public int? FifaRank { get; set; }
    [JsonPropertyName("status")] public string Status { get; set; } = "";
    [JsonPropertyName("flag_asset")] public string FlagAsset { get; set; } = "";
}

public class ProductTeamProfileView
{
    [JsonPropertyName("team_id")] public string TeamId { get; set; } = "";
    [JsonPropertyName("name_cn")] public string NameCn { get; set; } = "";
    [JsonPropertyName("code")] public string Code { get; set; } = "";
    [JsonPropertyName("group")] public string Group { get; set; } = "";
    [JsonPropertyName("fifa_rank")] public int? FifaRank { get; set; }
    [JsonPropertyName("status")] public string Status { get; set; } = "";
    [JsonPropertyName("headline")] public string Headline { get; set; } = "";
    [JsonPropertyName("stars")] public List<string> Stars { get; set; } = [];
    [JsonPropertyName("formation")] public string Formation { get; set; } = "";
    [JsonPropertyName("style_tags")] public List<string> StyleTags { get; set; } = [];
    [JsonPropertyName("strengths")] public List<string> Strengths { get; set; } = [];
    [JsonPropertyName("weaknesses")] public List<string> Weaknesses { get; set; } = [];
    [JsonPropertyName("intel_metrics")] public List<ProductMetricView> IntelMetrics { get; set; } = [];
    [JsonPropertyName("injury_watch")] public List<string> InjuryWatch { get; set; } = [];
    [JsonPropertyName("lineup_watch")] public List<string> LineupWatch { get; set; } = [];
    [JsonPropertyName("key_variables")] public List<string> KeyVariables { get; set; } = [];
    [JsonPropertyName("recent_form_notes")] public List<string> RecentFormNotes { get; set; } = [];
    [JsonPropertyName("radar")] public List<ProductRadarMetricView> Radar { get; set; } = [];
    [JsonPropertyName("notes")] public List<string> Notes { get; set; } = [];
}

public class ProductTeamResearchItem
{
    [JsonPropertyName("team")] public ProductTeamView Team { get; set; } = new();
    [JsonPropertyName("employee")] public ProductEmployeeView? Employee { get; set; }
    [JsonPropertyName("profile")] public ProductTeamProfileView? Profile { get; set; }
    [JsonPropertyName("matches")] public List<ProductMatchQueueItem> Matches { get; set; } = [];
    [JsonPropertyName("evidence_count")] public int EvidenceCount { get; set; }
    [JsonPropertyName("memory_count")] public int MemoryCount { get; set; }
    [JsonPropertyName("report_count")] public int ReportCount { get; set; }
    [JsonPropertyName("peak_probability")] public double PeakProbability { get; set; }
    [JsonPropertyName("evidence")] public List<ProductEvidenceView> Evidence { get; set; } = [];
    [JsonPropertyName("memories")] public List<ProductMemoryView> Memories { get; set; } = [];
    [JsonPropertyName("recent_activity")] public List<ProductActivityView> RecentActivity { get; set; } = [];
    [JsonPropertyName("data_notes")] public List<string> DataNotes { get; set; } = [];
}

public class ProductTeamResearchResult
{
    [JsonPropertyName("generated_at")] public string GeneratedAt { get; set; } = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
    [JsonPropertyName("teams")] public List<ProductTeamResearchItem> Teams { get; set; } = [];
    [JsonPropertyName("passed")] public bool Passed { get; set; }
    [JsonPropertyName("notes")] public List<string> Notes { get; set; } = [];
}

public class ProductRadarMetricView
{
    [JsonPropertyName("key")] public string Key { get; set; } = "";
    [JsonPropertyName("label")] public string Label { get; set; } = "";
    [JsonPropertyName("value")] public double Value { get; set; }
}

public class ProductEmployeeView
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("role")] public string Role { get; set; } = "";
    [JsonPropertyName("specialty")] public string Specialty { get; set; } = "";
    [JsonPropertyName("status")] public string Status { get; set; } = "";
    [JsonPropertyName("team_id")] public string? TeamId { get; set; }
}

public class ProductProbabilityView
{
    [JsonPropertyName("home_win")] public double HomeWin { get; set; }
    [JsonPropertyName("draw")] public double Draw { get; set; }
    [JsonPropertyName("away_win")] public double AwayWin { get; set; }
    [JsonPropertyName("phase")] public ProductPredictionPhaseView Phase { get; set; } = new();
    [JsonPropertyName("favorite_label")] public string FavoriteLabel { get; set; } = "";
    [JsonPropertyName("confidence_label")] public string ConfidenceLabel { get; set; } = "";
    [JsonPropertyName("risk_label")] public string RiskLabel { get; set; } = "";
    [JsonPropertyName("updated_at")] public string UpdatedAt { get; set; } = "";
    [JsonPropertyName("method")] public string Method { get; set; } = "";
    [JsonPropertyName("factors")] public List<ProductProbabilityFactorView> Factors { get; set; } = [];
    [JsonPropertyName("betting_advice")] public ProductBettingAdviceView BettingAdvice { get; set; } = new();
    [JsonPropertyName("quality_gate")] public ProductPredictionQualityGateView QualityGate { get; set; } = new();
}

public class ProductPredictionPhaseView
{
    [JsonPropertyName("phase")] public string Phase { get; set; } = "pre_match";
    [JsonPropertyName("phase_label")] public string PhaseLabel { get; set; } = "赛前预测";
    [JsonPropertyName("primary_label")] public string PrimaryLabel { get; set; } = "";
    [JsonPropertyName("summary")] public string Summary { get; set; } = "";
    [JsonPropertyName("score_label")] public string ScoreLabel { get; set; } = "";
    [JsonPropertyName("predicted_outcome")] public string PredictedOutcome { get; set; } = "";
    [JsonPropertyName("predicted_label")] public string PredictedLabel { get; set; } = "";
    [JsonPropertyName("actual_outcome")] public string ActualOutcome { get; set; } = "";
    [JsonPropertyName("actual_label")] public string ActualLabel { get; set; } = "";
    [JsonPropertyName("hit")] public bool? Hit { get; set; }
    [JsonPropertyName("brier_score")] public double? BrierScore { get; set; }
    [JsonPropertyName("is_post_match")] public bool IsPostMatch { get; set; }
    [JsonPropertyName("is_actionable_pre_match")] public bool IsActionablePreMatch { get; set; } = true;
}

public class ProductBettingAdviceView
{
    [JsonPropertyName("action")] public string Action { get; set; } = "no_bet";
    [JsonPropertyName("action_label")] public string ActionLabel { get; set; } = "暂不建议投注";
    [JsonPropertyName("suggested_play")] public string SuggestedPlay { get; set; } = "观察";
    [JsonPropertyName("confidence")] public string Confidence { get; set; } = "谨慎";
    [JsonPropertyName("threshold")] public string Threshold { get; set; } = "";
    [JsonPropertyName("stake_policy")] public string StakePolicy { get; set; } = "";
    [JsonPropertyName("risk_notes")] public List<string> RiskNotes { get; set; } = [];
    [JsonPropertyName("disclaimer")] public string Disclaimer { get; set; } = "仅作赛前研究和风险提示，不保证收益。请遵守当地法律法规，控制预算。";
}

public class ProductPredictionQualityGateView
{
    [JsonPropertyName("level")] public string Level { get; set; } = "blocked";
    [JsonPropertyName("level_label")] public string LevelLabel { get; set; } = "不可用";
    [JsonPropertyName("score")] public double Score { get; set; }
    [JsonPropertyName("passed")] public bool Passed { get; set; }
    [JsonPropertyName("betting_allowed")] public bool BettingAllowed { get; set; }
    [JsonPropertyName("strong_advice_allowed")] public bool StrongAdviceAllowed { get; set; }
    [JsonPropertyName("required_sources_ready")] public int RequiredSourcesReady { get; set; }
    [JsonPropertyName("required_sources_total")] public int RequiredSourcesTotal { get; set; }
    [JsonPropertyName("missing_sources")] public List<string> MissingSources { get; set; } = [];
    [JsonPropertyName("source_checks")] public List<ProductPredictionSourceCheckView> SourceChecks { get; set; } = [];
    [JsonPropertyName("warnings")] public List<string> Warnings { get; set; } = [];
    [JsonPropertyName("explanation")] public string Explanation { get; set; } = "";
    [JsonPropertyName("updated_at")] public string UpdatedAt { get; set; } = "";
}

public class ProductPredictionSourceCheckView
{
    [JsonPropertyName("key")] public string Key { get; set; } = "";
    [JsonPropertyName("label")] public string Label { get; set; } = "";
    [JsonPropertyName("ready")] public bool Ready { get; set; }
    [JsonPropertyName("status_label")] public string StatusLabel { get; set; } = "";
    [JsonPropertyName("weight")] public double Weight { get; set; }
    [JsonPropertyName("reason")] public string Reason { get; set; } = "";
}

public class ProductProbabilityFactorView
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("label")] public string Label { get; set; } = "";
    [JsonPropertyName("home_contribution")] public double HomeContribution { get; set; }
    [JsonPropertyName("weight")] public double Weight { get; set; }
    [JsonPropertyName("explanation")] public string Explanation { get; set; } = "";
}

public class ProductMarketSignalView
{
    [JsonPropertyName("label")] public string Label { get; set; } = "";
    [JsonPropertyName("side")] public string Side { get; set; } = "";
    [JsonPropertyName("moneyline")] public int? Moneyline { get; set; }
    [JsonPropertyName("market_probability")] public double MarketProbability { get; set; }
    [JsonPropertyName("model_probability")] public double ModelProbability { get; set; }
    [JsonPropertyName("edge")] public double Edge { get; set; }
    [JsonPropertyName("edge_label")] public string EdgeLabel { get; set; } = "";
    [JsonPropertyName("provider")] public string Provider { get; set; } = "";
    [JsonPropertyName("updated_at")] public string UpdatedAt { get; set; } = "";
}

public class ProductPredictionRuleView
{
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("summary")] public string Summary { get; set; } = "";
    [JsonPropertyName("steps")] public List<string> Steps { get; set; } = [];
    [JsonPropertyName("guardrails")] public List<string> Guardrails { get; set; } = [];
    [JsonPropertyName("limitations")] public List<string> Limitations { get; set; } = [];
}

public class ProductMetricView
{
    [JsonPropertyName("label")] public string Label { get; set; } = "";
    [JsonPropertyName("value")] public string Value { get; set; } = "";
    [JsonPropertyName("tone")] public string Tone { get; set; } = "neutral";
}

public class ProductEvidenceView
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("source")] public string Source { get; set; } = "";
    [JsonPropertyName("source_label")] public string SourceLabel { get; set; } = "";
    [JsonPropertyName("kind")] public string Kind { get; set; } = "";
    [JsonPropertyName("kind_label")] public string KindLabel { get; set; } = "";
    [JsonPropertyName("fact_level")] public string FactLevel { get; set; } = "";
    [JsonPropertyName("fact_label")] public string FactLabel { get; set; } = "";
    [JsonPropertyName("prediction_usage")] public string PredictionUsage { get; set; } = "";
    [JsonPropertyName("summary")] public string Summary { get; set; } = "";
    [JsonPropertyName("captured_at")] public string CapturedAt { get; set; } = "";
    [JsonPropertyName("confidence")] public double Confidence { get; set; }
    [JsonPropertyName("url")] public string? Url { get; set; }
}

public class ProductMemoryView
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("scope")] public string Scope { get; set; } = "";
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("summary")] public string Summary { get; set; } = "";
    [JsonPropertyName("importance")] public double Importance { get; set; }
    [JsonPropertyName("confidence")] public double Confidence { get; set; }
    [JsonPropertyName("created_at")] public string CreatedAt { get; set; } = "";
}

public class ProductActivityView
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("time")] public string Time { get; set; } = "";
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("message")] public string Message { get; set; } = "";
    [JsonPropertyName("tone")] public string Tone { get; set; } = "neutral";
}

public class ProductMatchQueueItem
{
    [JsonPropertyName("match_id")] public string MatchId { get; set; } = "";
    [JsonPropertyName("stage")] public string Stage { get; set; } = "";
    [JsonPropertyName("stage_label")] public string StageLabel { get; set; } = "";
    [JsonPropertyName("group_name")] public string GroupName { get; set; } = "";
    [JsonPropertyName("kickoff_time")] public string KickoffTime { get; set; } = "";
    [JsonPropertyName("kickoff_label")] public string KickoffLabel { get; set; } = "";
    [JsonPropertyName("venue")] public string Venue { get; set; } = "";
    [JsonPropertyName("status")] public string Status { get; set; } = "";
    [JsonPropertyName("status_label")] public string StatusLabel { get; set; } = "";
    [JsonPropertyName("home_score")] public int? HomeScore { get; set; }
    [JsonPropertyName("away_score")] public int? AwayScore { get; set; }
    [JsonPropertyName("home")] public ProductTeamView Home { get; set; } = new();
    [JsonPropertyName("away")] public ProductTeamView Away { get; set; } = new();
    [JsonPropertyName("prediction")] public ProductProbabilityView Prediction { get; set; } = new();
    [JsonPropertyName("evidence_count")] public int EvidenceCount { get; set; }
    [JsonPropertyName("memory_count")] public int MemoryCount { get; set; }
    [JsonPropertyName("report_count")] public int ReportCount { get; set; }
    [JsonPropertyName("summary")] public string Summary { get; set; } = "";
    [JsonPropertyName("is_ready")] public bool IsReady { get; set; }
}

public class ProductMatchDetail
{
    [JsonPropertyName("queue_item")] public ProductMatchQueueItem QueueItem { get; set; } = new();
    [JsonPropertyName("home_employee")] public ProductEmployeeView? HomeEmployee { get; set; }
    [JsonPropertyName("away_employee")] public ProductEmployeeView? AwayEmployee { get; set; }
    [JsonPropertyName("home_profile")] public ProductTeamProfileView? HomeProfile { get; set; }
    [JsonPropertyName("away_profile")] public ProductTeamProfileView? AwayProfile { get; set; }
    [JsonPropertyName("metrics")] public List<ProductMetricView> Metrics { get; set; } = [];
    [JsonPropertyName("evidence")] public List<ProductEvidenceView> Evidence { get; set; } = [];
    [JsonPropertyName("memories")] public List<ProductMemoryView> Memories { get; set; } = [];
    [JsonPropertyName("risks")] public List<string> Risks { get; set; } = [];
    [JsonPropertyName("model_review")] public string ModelReview { get; set; } = "";
    [JsonPropertyName("latest_report")] public string LatestReport { get; set; } = "";
    [JsonPropertyName("recent_activity")] public List<ProductActivityView> RecentActivity { get; set; } = [];
    [JsonPropertyName("data_notes")] public List<string> DataNotes { get; set; } = [];
    [JsonPropertyName("data_coverage")] public List<ProductDataCoverageItem> DataCoverage { get; set; } = [];
    [JsonPropertyName("prematch_watch_plan")] public List<ProductPrematchWatchPlanItem> PrematchWatchPlan { get; set; } = [];
    [JsonPropertyName("collection_priorities")] public List<ProductCollectionPriorityView> CollectionPriorities { get; set; } = [];
    [JsonPropertyName("market_signals")] public List<ProductMarketSignalView> MarketSignals { get; set; } = [];
    [JsonPropertyName("prediction_rule")] public ProductPredictionRuleView PredictionRule { get; set; } = new();
}

public class ProductCollectionPriorityView
{
    [JsonPropertyName("key")] public string Key { get; set; } = "";
    [JsonPropertyName("label")] public string Label { get; set; } = "";
    [JsonPropertyName("priority")] public string Priority { get; set; } = "normal";
    [JsonPropertyName("priority_label")] public string PriorityLabel { get; set; } = "常规";
    [JsonPropertyName("status")] public string Status { get; set; } = "pending";
    [JsonPropertyName("status_label")] public string StatusLabel { get; set; } = "待补充";
    [JsonPropertyName("reason")] public string Reason { get; set; } = "";
    [JsonPropertyName("suggested_source")] public string SuggestedSource { get; set; } = "";
    [JsonPropertyName("window_label")] public string WindowLabel { get; set; } = "";
}

public class ProductPrematchWatchPlanItem
{
    [JsonPropertyName("key")] public string Key { get; set; } = "";
    [JsonPropertyName("label")] public string Label { get; set; } = "";
    [JsonPropertyName("window_label")] public string WindowLabel { get; set; } = "";
    [JsonPropertyName("status")] public string Status { get; set; } = "pending";
    [JsonPropertyName("status_label")] public string StatusLabel { get; set; } = "";
    [JsonPropertyName("source_label")] public string SourceLabel { get; set; } = "";
    [JsonPropertyName("summary")] public string Summary { get; set; } = "";
}

public class ProductDataCoverageItem
{
    [JsonPropertyName("key")] public string Key { get; set; } = "";
    [JsonPropertyName("label")] public string Label { get; set; } = "";
    [JsonPropertyName("status")] public string Status { get; set; } = "missing";
    [JsonPropertyName("status_label")] public string StatusLabel { get; set; } = "缺失";
    [JsonPropertyName("source_label")] public string SourceLabel { get; set; } = "";
    [JsonPropertyName("updated_at")] public string UpdatedAt { get; set; } = "";
    [JsonPropertyName("summary")] public string Summary { get; set; } = "";
}

public class ProductDataTrustItem
{
    [JsonPropertyName("source_name")] public string SourceName { get; set; } = "";
    [JsonPropertyName("provider")] public string Provider { get; set; } = "";
    [JsonPropertyName("authority_label")] public string AuthorityLabel { get; set; } = "";
    [JsonPropertyName("stability_label")] public string StabilityLabel { get; set; } = "";
    [JsonPropertyName("enabled")] public bool Enabled { get; set; }
    [JsonPropertyName("latest_captured_at")] public string? LatestCapturedAt { get; set; }
    [JsonPropertyName("freshness_label")] public string FreshnessLabel { get; set; } = "";
    [JsonPropertyName("snapshot_count")] public int SnapshotCount { get; set; }
    [JsonPropertyName("reliability_score")] public double ReliabilityScore { get; set; }
    [JsonPropertyName("requires_api_key")] public bool RequiresApiKey { get; set; }
    [JsonPropertyName("best_for")] public List<string> BestFor { get; set; } = [];
    [JsonPropertyName("not_for")] public List<string> NotFor { get; set; } = [];
    [JsonPropertyName("notes")] public List<string> Notes { get; set; } = [];
}

public class ProductAutoCollectionStatus
{
    [JsonPropertyName("enabled")] public bool Enabled { get; set; }
    [JsonPropertyName("adaptive_schedule_enabled")] public bool AdaptiveScheduleEnabled { get; set; }
    [JsonPropertyName("running")] public bool Running { get; set; }
    [JsonPropertyName("current_source_id")] public string CurrentSourceId { get; set; } = "";
    [JsonPropertyName("current_source_name")] public string CurrentSourceName { get; set; } = "";
    [JsonPropertyName("elapsed_seconds")] public int ElapsedSeconds { get; set; }
    [JsonPropertyName("interval_minutes")] public int IntervalMinutes { get; set; }
    [JsonPropertyName("next_interval_minutes")] public int NextIntervalMinutes { get; set; }
    [JsonPropertyName("next_interval_reason")] public string NextIntervalReason { get; set; } = "";
    [JsonPropertyName("last_started_at")] public string LastStartedAt { get; set; } = "";
    [JsonPropertyName("last_completed_at")] public string LastCompletedAt { get; set; } = "";
    [JsonPropertyName("last_passed")] public bool LastPassed { get; set; }
    [JsonPropertyName("sources_checked")] public int SourcesChecked { get; set; }
    [JsonPropertyName("sources_succeeded")] public int SourcesSucceeded { get; set; }
    [JsonPropertyName("imported")] public int Imported { get; set; }
    [JsonPropertyName("skipped_duplicates")] public int SkippedDuplicates { get; set; }
    [JsonPropertyName("baseline_predictions_refreshed")] public int BaselinePredictionsRefreshed { get; set; }
    [JsonPropertyName("signals_created")] public int SignalsCreated { get; set; }
    [JsonPropertyName("reports_created")] public int ReportsCreated { get; set; }
    [JsonPropertyName("reports_skipped_reason")] public string ReportsSkippedReason { get; set; } = "";
    [JsonPropertyName("auto_llm_reports_enabled")] public bool AutoLlmReportsEnabled { get; set; }
    [JsonPropertyName("llm_report_interval_minutes")] public int LlmReportIntervalMinutes { get; set; }
    [JsonPropertyName("max_llm_report_teams")] public int MaxLlmReportTeams { get; set; }
    [JsonPropertyName("last_llm_report_run_at")] public string LastLlmReportRunAt { get; set; } = "";
    [JsonPropertyName("last_llm_reports_created")] public int LastLlmReportsCreated { get; set; }
    [JsonPropertyName("source_runs")] public List<ProductAutoCollectionSourceRun> SourceRuns { get; set; } = [];
    [JsonPropertyName("notes")] public List<string> Notes { get; set; } = [];
}

public class ProductAutoCollectionSourceRun
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("source_name")] public string SourceName { get; set; } = "";
    [JsonPropertyName("provider")] public string Provider { get; set; } = "";
    [JsonPropertyName("elapsed_ms")] public long ElapsedMs { get; set; }
    [JsonPropertyName("elapsed_label")] public string ElapsedLabel { get; set; } = "";
    [JsonPropertyName("timeout_seconds")] public int TimeoutSeconds { get; set; }
    [JsonPropertyName("raw_items")] public int RawItems { get; set; }
    [JsonPropertyName("imported")] public int Imported { get; set; }
    [JsonPropertyName("skipped_duplicates")] public int SkippedDuplicates { get; set; }
    [JsonPropertyName("baseline_predictions_refreshed")] public int BaselinePredictionsRefreshed { get; set; }
    [JsonPropertyName("status_label")] public string StatusLabel { get; set; } = "";
    [JsonPropertyName("tone")] public string Tone { get; set; } = "neutral";
    [JsonPropertyName("error_message")] public string ErrorMessage { get; set; } = "";
    [JsonPropertyName("notes")] public List<string> Notes { get; set; } = [];
}

public class ProductModelHealthResult
{
    [JsonPropertyName("generated_at")] public string GeneratedAt { get; set; } = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
    [JsonPropertyName("strategy_evaluation")] public StrategyEvaluationSummary StrategyEvaluation { get; set; } = new();
    [JsonPropertyName("backtest")] public ModelBacktestResult? Backtest { get; set; }
    [JsonPropertyName("backtest_cached_at")] public string? BacktestCachedAt { get; set; }
    [JsonPropertyName("backtest_cache_age_minutes")] public int? BacktestCacheAgeMinutes { get; set; }
    [JsonPropertyName("passed")] public bool Passed { get; set; }
    [JsonPropertyName("notes")] public List<string> Notes { get; set; } = [];
}

public class ProductOverviewResult
{
    [JsonPropertyName("generated_at")] public string GeneratedAt { get; set; } = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
    [JsonPropertyName("navigation")] public List<string> Navigation { get; set; } = [];
    [JsonPropertyName("selected_match_id")] public string SelectedMatchId { get; set; } = "";
    [JsonPropertyName("queue")] public List<ProductMatchQueueItem> Queue { get; set; } = [];
    [JsonPropertyName("featured_match")] public ProductMatchDetail? FeaturedMatch { get; set; }
    [JsonPropertyName("data_trust")] public List<ProductDataTrustItem> DataTrust { get; set; } = [];
    [JsonPropertyName("auto_collection")] public ProductAutoCollectionStatus AutoCollection { get; set; } = new();
    [JsonPropertyName("summary_metrics")] public List<ProductMetricView> SummaryMetrics { get; set; } = [];
    [JsonPropertyName("llm_usage")] public WorldCupLlmUsageSummary LlmUsage { get; set; } = new();
    [JsonPropertyName("passed")] public bool Passed { get; set; }
    [JsonPropertyName("notes")] public List<string> Notes { get; set; } = [];
}

public class ProductAuditResult
{
    [JsonPropertyName("match_id")] public string? MatchId { get; set; }
    [JsonPropertyName("team_id")] public string? TeamId { get; set; }
    [JsonPropertyName("events")] public List<ProductActivityView> Events { get; set; } = [];
    [JsonPropertyName("evidence")] public List<ProductEvidenceView> Evidence { get; set; } = [];
    [JsonPropertyName("memories")] public List<ProductMemoryView> Memories { get; set; } = [];
    [JsonPropertyName("passed")] public bool Passed { get; set; }
    [JsonPropertyName("notes")] public List<string> Notes { get; set; } = [];
}

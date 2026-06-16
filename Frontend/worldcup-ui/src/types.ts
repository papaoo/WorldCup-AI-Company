export type ProductTeamView = {
  id: string;
  name: string;
  name_cn: string;
  code: string;
  group: string;
  fifa_rank: number | null;
  status: string;
  flag_asset: string;
};

export type ProductEmployeeView = {
  id: string;
  name: string;
  role: string;
  specialty: string;
  status: string;
  team_id: string | null;
};

export type ProductProbabilityView = {
  home_win: number;
  draw: number;
  away_win: number;
  phase: ProductPredictionPhaseView;
  favorite_label: string;
  confidence_label: string;
  risk_label: string;
  updated_at: string;
  method: string;
  factors: ProductProbabilityFactorView[];
  betting_advice: ProductBettingAdviceView;
  quality_gate: ProductPredictionQualityGateView;
};

export type ProductPredictionPhaseView = {
  phase: string;
  phase_label: string;
  primary_label: string;
  summary: string;
  score_label: string;
  predicted_outcome: string;
  predicted_label: string;
  actual_outcome: string;
  actual_label: string;
  hit: boolean | null;
  brier_score: number | null;
  is_post_match: boolean;
  is_actionable_pre_match: boolean;
};

export type ProductBettingAdviceView = {
  action: string;
  action_label: string;
  suggested_play: string;
  confidence: string;
  threshold: string;
  stake_policy: string;
  risk_notes: string[];
  disclaimer: string;
};

export type ProductPredictionQualityGateView = {
  level: string;
  level_label: string;
  score: number;
  passed: boolean;
  betting_allowed: boolean;
  strong_advice_allowed: boolean;
  required_sources_ready: number;
  required_sources_total: number;
  missing_sources: string[];
  source_checks: ProductPredictionSourceCheckView[];
  warnings: string[];
  explanation: string;
  updated_at: string;
};

export type ProductPredictionSourceCheckView = {
  key: string;
  label: string;
  ready: boolean;
  status_label: string;
  weight: number;
  reason: string;
};

export type ProductProbabilityFactorView = {
  id: string;
  label: string;
  home_contribution: number;
  weight: number;
  explanation: string;
};

export type ProductMarketSignalView = {
  label: string;
  side: string;
  moneyline: number | null;
  market_probability: number;
  model_probability: number;
  edge: number;
  edge_label: string;
  provider: string;
  updated_at: string;
};

export type ProductRadarMetricView = {
  key: string;
  label: string;
  value: number;
};

export type ProductTeamProfileView = {
  team_id: string;
  name_cn: string;
  code: string;
  group: string;
  fifa_rank: number | null;
  status: string;
  headline: string;
  stars: string[];
  formation: string;
  style_tags: string[];
  strengths: string[];
  weaknesses: string[];
  intel_metrics: ProductMetricView[];
  injury_watch: string[];
  lineup_watch: string[];
  key_variables: string[];
  recent_form_notes: string[];
  radar: ProductRadarMetricView[];
  notes: string[];
};

export type ProductTeamResearchItem = {
  team: ProductTeamView;
  employee: ProductEmployeeView | null;
  profile: ProductTeamProfileView | null;
  matches: ProductMatchQueueItem[];
  evidence_count: number;
  memory_count: number;
  report_count: number;
  peak_probability: number;
  evidence: ProductEvidenceView[];
  memories: ProductMemoryView[];
  recent_activity: ProductActivityView[];
  data_notes: string[];
};

export type ProductTeamResearchResult = {
  generated_at: string;
  teams: ProductTeamResearchItem[];
  passed: boolean;
  notes: string[];
};

export type ProductPredictionRuleView = {
  title: string;
  summary: string;
  steps: string[];
  guardrails: string[];
  limitations: string[];
};

export type ProductMetricView = {
  label: string;
  value: string;
  tone: "good" | "warning" | "danger" | "neutral" | string;
};

export type ProductEvidenceView = {
  id: string;
  source: string;
  source_label: string;
  kind: string;
  kind_label: string;
  fact_level: string;
  fact_label: string;
  prediction_usage: string;
  summary: string;
  captured_at: string;
  confidence: number;
  url: string | null;
};

export type ProductMemoryView = {
  id: string;
  scope: string;
  type: string;
  summary: string;
  importance: number;
  confidence: number;
  created_at: string;
};

export type ProductActivityView = {
  id: string;
  time: string;
  title: string;
  message: string;
  tone: "good" | "warning" | "danger" | "neutral" | string;
};

export type ProductMatchQueueItem = {
  match_id: string;
  stage: string;
  stage_label: string;
  group_name: string;
  kickoff_time: string;
  kickoff_label: string;
  venue: string;
  status: string;
  status_label: string;
  home_score: number | null;
  away_score: number | null;
  home: ProductTeamView;
  away: ProductTeamView;
  prediction: ProductProbabilityView;
  evidence_count: number;
  memory_count: number;
  report_count: number;
  summary: string;
  is_ready: boolean;
};

export type ProductMatchDetail = {
  queue_item: ProductMatchQueueItem;
  home_employee: ProductEmployeeView | null;
  away_employee: ProductEmployeeView | null;
  home_profile: ProductTeamProfileView | null;
  away_profile: ProductTeamProfileView | null;
  metrics: ProductMetricView[];
  evidence: ProductEvidenceView[];
  memories: ProductMemoryView[];
  risks: string[];
  model_review: string;
  latest_report: string;
  recent_activity: ProductActivityView[];
  data_notes: string[];
  data_coverage: ProductDataCoverageItem[];
  prematch_watch_plan: ProductPrematchWatchPlanItem[];
  collection_priorities: ProductCollectionPriorityView[];
  market_signals: ProductMarketSignalView[];
  prediction_rule: ProductPredictionRuleView;
};

export type ProductCollectionPriorityView = {
  key: string;
  label: string;
  priority: "critical" | "high" | "medium" | "normal" | string;
  priority_label: string;
  status: "ready" | "pending" | string;
  status_label: string;
  reason: string;
  suggested_source: string;
  window_label: string;
};

export type ProductPrematchWatchPlanItem = {
  key: string;
  label: string;
  window_label: string;
  status: "ready" | "watching" | string;
  status_label: string;
  source_label: string;
  summary: string;
};

export type ProductDataCoverageItem = {
  key: string;
  label: string;
  status: "ready" | "missing" | string;
  status_label: string;
  source_label: string;
  updated_at: string;
  summary: string;
};

export type ProductDataTrustItem = {
  source_name: string;
  provider: string;
  authority_label: string;
  stability_label: string;
  enabled: boolean;
  latest_captured_at: string | null;
  freshness_label: string;
  snapshot_count: number;
  reliability_score: number;
  requires_api_key: boolean;
  best_for: string[];
  not_for: string[];
  notes: string[];
};

export type ProductAutoCollectionStatus = {
  enabled: boolean;
  adaptive_schedule_enabled: boolean;
  running: boolean;
  current_source_id: string;
  current_source_name: string;
  elapsed_seconds: number;
  interval_minutes: number;
  next_interval_minutes: number;
  next_interval_reason: string;
  last_started_at: string;
  last_completed_at: string;
  last_passed: boolean;
  sources_checked: number;
  sources_succeeded: number;
  imported: number;
  skipped_duplicates: number;
  baseline_predictions_refreshed: number;
  signals_created: number;
  reports_created: number;
  reports_skipped_reason: string;
  auto_llm_reports_enabled: boolean;
  llm_report_interval_minutes: number;
  max_llm_report_teams: number;
  last_llm_report_run_at: string;
  last_llm_reports_created: number;
  source_runs: ProductAutoCollectionSourceRun[];
  notes: string[];
};

export type ProductAutoCollectionSourceRun = {
  id: string;
  source_name: string;
  provider: string;
  elapsed_ms: number;
  elapsed_label: string;
  timeout_seconds: number;
  raw_items: number;
  imported: number;
  skipped_duplicates: number;
  baseline_predictions_refreshed: number;
  status_label: string;
  tone: "good" | "warning" | "danger" | "neutral" | string;
  error_message: string;
  notes: string[];
};

export type LlmUsageSummary = {
  calls: number;
  prompt_tokens: number;
  completion_tokens: number;
  estimated_cost_usd: number;
  successful_calls: number;
  failed_calls: number;
};

export type ProductOverviewResult = {
  generated_at: string;
  navigation: string[];
  selected_match_id: string;
  queue: ProductMatchQueueItem[];
  featured_match: ProductMatchDetail | null;
  data_trust: ProductDataTrustItem[];
  auto_collection: ProductAutoCollectionStatus;
  summary_metrics: ProductMetricView[];
  llm_usage: LlmUsageSummary;
  passed: boolean;
  notes: string[];
};

export type ProductAuditResult = {
  match_id: string | null;
  team_id: string | null;
  events: ProductActivityView[];
  evidence: ProductEvidenceView[];
  memories: ProductMemoryView[];
  passed: boolean;
  notes: string[];
};

export type ModelBacktestBucket = {
  label: string;
  samples: number;
  hit_rate: number;
  average_brier_score: number;
};

export type ModelBacktestItem = {
  date: string;
  home_team: string;
  away_team: string;
  score: string;
  tournament: string;
  actual_outcome: string;
  predicted_outcome: string;
  hit: boolean;
  home_win_probability: number;
  draw_probability: number;
  away_win_probability: number;
  brier_score: number;
  log_loss: number;
};

export type ModelBacktestResult = {
  strategy_version: string;
  source: string;
  date_from: string;
  date_to: string;
  samples_checked: number;
  samples_used: number;
  skipped_unmapped: number;
  top1_hit_count: number;
  top1_hit_rate: number;
  average_brier_score: number;
  average_log_loss: number;
  draw_samples: number;
  draw_hit_count: number;
  draw_recall: number;
  favorite_samples: number;
  favorite_hit_rate: number;
  buckets: ModelBacktestBucket[];
  sample_items: ModelBacktestItem[];
  passed: boolean;
  notes: string[];
};

export type StrategyEvaluationItem = {
  match_id: string;
  home_team: string;
  away_team: string;
  score: string;
  actual_outcome: string;
  predicted_outcome: string;
  hit: boolean;
  brier_score: number;
  reviewed_at: string;
};

export type StrategyEvaluationSummary = {
  strategy_version: string;
  reviewed_matches: number;
  hit_count: number;
  hit_rate: number;
  average_brier_score: number;
  latest_reviewed_at: string | null;
  items: StrategyEvaluationItem[];
};

export type ProductModelHealthResult = {
  generated_at: string;
  strategy_evaluation: StrategyEvaluationSummary;
  backtest: ModelBacktestResult | null;
  backtest_cached_at: string | null;
  backtest_cache_age_minutes: number | null;
  passed: boolean;
  notes: string[];
};

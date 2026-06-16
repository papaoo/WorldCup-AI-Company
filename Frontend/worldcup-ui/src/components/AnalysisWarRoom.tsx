import { useEffect, useMemo, useState } from "react";
import {
  Activity,
  AlertTriangle,
  Brain,
  CheckCircle2,
  ChevronRight,
  CircleDot,
  Database,
  FileText,
  Gauge,
  Loader2,
  RefreshCw,
  Search,
  ShieldAlert,
  Sparkles,
  Timer,
  Wifi,
  WifiOff
} from "lucide-react";
import {
  loadTeamIntelligenceContextPack,
  loadTeamWorkbench,
  loadWorldCupModelGatewayHealth,
  runTeamContextLlmReview
} from "../api/worldcupApi";
import type {
  CompanyState,
  Employee,
  Match,
  TeamContextLlmReviewResult,
  TeamIntelligenceContextPack,
  TeamWorkbenchResult,
  WatchObject,
  WorldCupModelGatewayHealthResult
} from "../types";
import { pct } from "../utils/format";
import { fallbackLabel, teamDisplayName, teamShortName } from "../utils/teamNames";

type AnalysisWarRoomProps = {
  state: CompanyState;
  selectedMatch: Match | null;
  selectedObjectId: string;
  onSelectObject: (objectId: string) => void;
  onSelectMatch: (matchId: string) => void;
};

type TeamRow = {
  team: WatchObject;
  employee: Employee | null;
  group: string;
  rank: number | null;
  signals: number;
  artifacts: number;
  predictionEligibleMatches: number;
};

export function AnalysisWarRoom({
  state,
  selectedMatch,
  selectedObjectId,
  onSelectObject,
  onSelectMatch
}: AnalysisWarRoomProps) {
  const [query, setQuery] = useState("");
  const [qualityFilter, setQualityFilter] = useState<"all" | "ready" | "risk">("all");
  const [contextPack, setContextPack] = useState<TeamIntelligenceContextPack | null>(null);
  const [workbench, setWorkbench] = useState<TeamWorkbenchResult | null>(null);
  const [review, setReview] = useState<TeamContextLlmReviewResult | null>(null);
  const [gatewayHealth, setGatewayHealth] = useState<WorldCupModelGatewayHealthResult | null>(
    state.modelGatewayHealth ?? null
  );
  const [contextStatus, setContextStatus] = useState<"idle" | "loading" | "failed">("idle");
  const [reviewStatus, setReviewStatus] = useState<"idle" | "running" | "failed">("idle");
  const [statusText, setStatusText] = useState("选择球队员工，查看资料包和证据链。");

  const teamsById = useMemo(() => new Map(state.teams.map((team) => [team.id, team])), [state.teams]);
  const employeesById = useMemo(() => new Map(state.employees.map((employee) => [employee.id, employee])), [state.employees]);
  const assignmentByObject = useMemo(() => new Map(state.assignments.map((assignment) => [assignment.object_id, assignment])), [state.assignments]);
  const predictionsByMatch = useMemo(() => new Map(state.predictions.map((prediction) => [prediction.match_id, prediction])), [state.predictions]);
  const eligibilityByMatch = useMemo(
    () => new Map((state.dataReadiness?.match_eligibility ?? []).map((item) => [item.match_id, item])),
    [state.dataReadiness]
  );

  const selectedTeamId = selectedObjectId || selectedMatch?.home_object_id || state.teams[0]?.id || "";
  const selectedTeam = teamsById.get(selectedTeamId) ?? state.teams[0] ?? null;
  const selectedEmployee = selectedTeam ? employeeForTeam(selectedTeam.id, assignmentByObject, employeesById) : null;
  const selectedTeamMatches = selectedTeam
    ? state.matches.filter((match) => match.home_object_id === selectedTeam.id || match.away_object_id === selectedTeam.id)
    : [];

  const teamRows = useMemo(() => {
    return state.teams
      .filter((team) => team.id !== "slot_tba")
      .map((team) => {
        const metadata = parseTeamMetadata(team.metadata_json);
        const employee = employeeForTeam(team.id, assignmentByObject, employeesById);
        const matches = state.matches.filter((match) => match.home_object_id === team.id || match.away_object_id === team.id);
        const eligible = matches.filter((match) => eligibilityByMatch.get(match.id)?.eligible).length;
        return {
          team,
          employee,
          group: metadata.group ?? "未分组",
          rank: metadata.rank ?? null,
          signals: state.signals.filter((signal) => signal.object_id === team.id).length,
          artifacts: state.artifacts.filter((artifact) => artifact.object_id === team.id).length,
          predictionEligibleMatches: eligible
        } satisfies TeamRow;
      })
      .sort((a, b) => groupSortValue(a.group).localeCompare(groupSortValue(b.group), "zh-CN") || (a.rank ?? 999) - (b.rank ?? 999));
  }, [assignmentByObject, eligibilityByMatch, employeesById, state.artifacts, state.matches, state.signals, state.teams]);

  const filteredRows = teamRows.filter((row) => {
    const needle = `${teamDisplayName(row.team)} ${row.team.symbol} ${row.employee?.name ?? ""}`.toLowerCase();
    const matchesQuery = !query.trim() || needle.includes(query.trim().toLowerCase());
    const matchesFilter = qualityFilter === "all"
      || (qualityFilter === "ready" && row.predictionEligibleMatches > 0)
      || (qualityFilter === "risk" && (row.signals > 0 || row.predictionEligibleMatches === 0));
    return matchesQuery && matchesFilter;
  });

  useEffect(() => {
    setGatewayHealth(state.modelGatewayHealth ?? null);
  }, [state.modelGatewayHealth]);

  useEffect(() => {
    if (!selectedTeamId) return;
    let alive = true;
    setContextStatus("loading");
    setReview(null);
    setStatusText("正在装载球队资料包、证据链和员工工作台...");
    Promise.all([
      loadTeamIntelligenceContextPack(selectedTeamId, 10),
      loadTeamWorkbench(selectedTeamId, 10).catch(() => null)
    ])
      .then(([pack, wb]) => {
        if (!alive) return;
        setContextPack(pack);
        setWorkbench(wb);
        setContextStatus("idle");
        setStatusText(pack.passed ? "资料包已就绪，可以进入模型审查。" : "资料包不完整，需要继续采集或复核。");
      })
      .catch((error) => {
        if (!alive) return;
        setContextStatus("failed");
        setContextPack(null);
        setWorkbench(null);
        setStatusText(error instanceof Error ? error.message : "资料包加载失败");
      });
    return () => {
      alive = false;
    };
  }, [selectedTeamId]);

  async function refreshGatewayHealth() {
    setStatusText("正在检测模型网关，不会消耗 token。");
    const result = await loadWorldCupModelGatewayHealth().catch(() => null);
    setGatewayHealth(result);
    setStatusText(result?.message ?? "模型网关检测失败。");
  }

  async function runSelectedTeamReview() {
    if (!selectedTeamId) return;
    if (gatewayHealth && !gatewayHealth.online) {
      setStatusText(gatewayHealth.message);
      return;
    }
    if (contextPack && contextPack.estimated_tokens > 3500) {
      setStatusText("资料包超过当前模型审查预算，请减少证据条数或先做摘要压缩。");
      return;
    }

    setReviewStatus("running");
    setStatusText("正在调用模型审查球队资料包...");
    try {
      const result = await runTeamContextLlmReview(selectedTeamId, 10);
      setReview(result);
      setStatusText(result.passed ? "模型审查完成，报告已归档。" : firstNote(result.notes) || "模型审查未通过，已保存降级内容。");
    } catch (error) {
      setReviewStatus("failed");
      setStatusText(error instanceof Error ? error.message : "模型审查调用失败");
      return;
    }
    setReviewStatus("idle");
  }

  const readiness = state.dataReadiness;
  const sourceHealth = sourceHealthSummary(readiness?.registered_sources ?? []);
  const selectedMatchEligible = selectedMatch ? eligibilityByMatch.get(selectedMatch.id)?.eligible === true : false;
  const selectedPrediction = selectedMatchEligible && selectedMatch ? predictionsByMatch.get(selectedMatch.id) ?? null : null;
  const latestReport = workbench?.latest_report?.content ? previewLines(workbench.latest_report.content, 5) : [];

  return (
    <section className="analysis-war-room">
      <header className="warroom-hero">
        <div className="warroom-title">
          <span>AI 赛事研究中枢</span>
          <h1>世界杯公司战情室</h1>
          <p>一个球队对应一个员工。系统先采集公开数据、压缩证据，再把有限上下文交给模型审查，避免把未验证信号包装成确定结论。</p>
        </div>
        <div className="warroom-metrics">
          <MetricCard label="可预测比赛" value={`${readiness?.eligible_matches ?? 0}/${readiness?.total_matches ?? state.matches.length}`} tone="good" />
          <MetricCard label="被拦截比赛" value={String(readiness?.blocked_matches ?? 0)} tone="warn" />
          <MetricCard label="证据快照" value={String(state.dashboard?.operations.data_snapshots ?? "-")} tone="info" />
          <MetricCard label="LLM 成本估算" value={`$${(state.llmUsage?.estimated_cost_usd ?? 0).toFixed(4)}`} tone="plain" />
        </div>
      </header>

      <div className="warroom-shell">
        <aside className="warroom-left-panel">
          <div className="warroom-panel-head">
            <div>
              <span>员工席位</span>
              <strong>{filteredRows.length} 名球队研究员</strong>
            </div>
            <CircleDot size={18} />
          </div>
          <label className="warroom-search">
            <Search size={16} />
            <input value={query} onChange={(event) => setQuery(event.target.value)} placeholder="搜索球队、代码或员工" />
          </label>
          <div className="warroom-segments" aria-label="球队筛选">
            <button className={qualityFilter === "all" ? "active" : ""} onClick={() => setQualityFilter("all")}>全部</button>
            <button className={qualityFilter === "ready" ? "active" : ""} onClick={() => setQualityFilter("ready")}>可预测</button>
            <button className={qualityFilter === "risk" ? "active" : ""} onClick={() => setQualityFilter("risk")}>待复核</button>
          </div>
          <div className="warroom-team-list">
            {filteredRows.map((row) => (
              <button
                key={row.team.id}
                className={`warroom-team-row ${row.team.id === selectedTeam?.id ? "selected" : ""}`}
                onClick={() => onSelectObject(row.team.id)}
              >
                <span className="team-orb">{row.team.symbol}</span>
                <span className="team-row-main">
                  <strong>{teamDisplayName(row.team)}</strong>
                  <em>{row.employee?.name ? toChineseEmployeeName(row.employee.name, row.team) : "未分配员工"}</em>
                </span>
                <span className="team-row-meta">
                  <b>{row.group}</b>
                  <i>{row.signals} 信号</i>
                </span>
              </button>
            ))}
          </div>
        </aside>

        <main className="warroom-research-desk">
          <section className="team-focus-card">
            <div className="team-focus-identity">
              <div className="team-focus-orb">{selectedTeam?.symbol ?? "AI"}</div>
              <div>
                <span>专属研究员</span>
                <h2>{selectedTeam ? teamDisplayName(selectedTeam) : "等待选择球队"}</h2>
                <p>{selectedEmployee ? `${toChineseEmployeeName(selectedEmployee.name, selectedTeam)} 负责追踪该球队的公开数据、情报变化和报告产出。` : "该球队还没有绑定员工。"}</p>
              </div>
            </div>
            <div className="team-focus-badges">
              <Badge label="FIFA 排名" value={contextPack?.fifa_rank ? String(contextPack.fifa_rank) : "未记录"} />
              <Badge label="小组" value={contextPack?.group ?? parseTeamMetadata(selectedTeam?.metadata_json).group ?? "待定"} />
              <Badge label="资料包 token" value={contextPack ? String(contextPack.estimated_tokens) : "-"} tone={contextPack && contextPack.estimated_tokens > 3000 ? "warn" : "good"} />
              <Badge label="待处理信号" value={String(workbench?.pending_actionable_signals ?? 0)} tone={(workbench?.pending_actionable_signals ?? 0) > 0 ? "warn" : "good"} />
            </div>
          </section>

          <section className="desk-grid">
            <article className="warroom-card match-card">
              <CardTitle icon={<Timer size={18} />} title="近期赛程与预测资格" subtitle="未定席位和演示队伍会被系统拦截" />
              <div className="match-stack">
                {selectedTeamMatches.slice(0, 6).map((match) => {
                  const home = teamsById.get(match.home_object_id);
                  const away = teamsById.get(match.away_object_id);
                  const eligibility = eligibilityByMatch.get(match.id);
                  const prediction = predictionsByMatch.get(match.id);
                  const usablePrediction = eligibility?.eligible ? prediction : null;
                  const active = selectedMatch?.id === match.id;
                  return (
                    <button
                      key={match.id}
                      className={`match-analysis-row ${active ? "active" : ""}`}
                      onClick={() => onSelectMatch(match.id)}
                    >
                      <span>{stageLabel(match.stage)}</span>
                      <strong>{teamShortName(home, match.home_object_id)} 对阵 {teamShortName(away, match.away_object_id)}</strong>
                      <em>{formatDate(match.kickoff_time)}</em>
                      <i className={eligibility?.eligible ? "ok" : "blocked"}>
                        {eligibility?.eligible ? "可预测" : "已拦截"}
                      </i>
                      {usablePrediction ? <small>{leadingPredictionText(usablePrediction, home, away)}</small> : null}
                    </button>
                  );
                })}
                {selectedTeamMatches.length === 0 ? <EmptyLine text="该球队暂无绑定赛程。" /> : null}
              </div>
            </article>

            <article className="warroom-card evidence-card">
              <CardTitle icon={<Database size={18} />} title="压缩证据链" subtitle="先去重和排序，再进入模型上下文" />
              {contextStatus === "loading" ? <LoadingLine text="正在生成资料包..." /> : null}
              {contextStatus === "failed" ? <EmptyLine text="资料包加载失败。" /> : null}
              <div className="evidence-list">
                {(contextPack?.evidence ?? []).map((item) => (
                  <article key={item.id} className="evidence-row">
                    <div>
                      <span>{evidenceKindLabel(item.kind)}</span>
                      <strong>{cleanEvidenceSummary(item.summary)}</strong>
                    </div>
                    <footer>
                      <em>{sourceLabel(item.source)}</em>
                      <b>{pct(item.confidence)}</b>
                      <i>{formatDate(item.captured_at)}</i>
                    </footer>
                  </article>
                ))}
                {contextPack && contextPack.evidence.length === 0 ? <EmptyLine text="暂无可用证据。请先运行公开数据采集。" /> : null}
              </div>
            </article>

            <article className="warroom-card risk-card">
              <CardTitle icon={<ShieldAlert size={18} />} title="风险与来源边界" subtitle="把事实、发现信号、待复核内容分开" />
              <div className="risk-list">
                {(contextPack?.risks ?? []).map((risk) => <p key={risk}>{translateKnownNote(risk)}</p>)}
                {(contextPack?.source_notes ?? []).map((note) => <p key={note}>{translateKnownNote(note)}</p>)}
                {!contextPack?.risks.length && !contextPack?.source_notes.length ? <EmptyLine text="暂无额外风险提示。" /> : null}
              </div>
              <div className="source-health-strip">
                <span>高可信源 {sourceHealth.strong}</span>
                <span>可用源 {sourceHealth.enabled}</span>
                <span>需复核源 {sourceHealth.needsReview}</span>
              </div>
            </article>

            <article className="warroom-card prediction-card">
              <CardTitle icon={<Gauge size={18} />} title="当前比赛基线" subtitle="概率来自规则与结构化数据，不等于确定预测" />
              {selectedMatch && selectedPrediction ? (
                <div className="probability-panel">
                  <ProbabilityBar label={teamShortName(teamsById.get(selectedMatch.home_object_id), selectedMatch.home_object_id)} value={selectedPrediction.home_win_probability} />
                  <ProbabilityBar label="平局" value={selectedPrediction.draw_probability} />
                  <ProbabilityBar label={teamShortName(teamsById.get(selectedMatch.away_object_id), selectedMatch.away_object_id)} value={selectedPrediction.away_win_probability} />
                  <p>{predictionExplanationText(selectedPrediction.explanation, selectedMatch, teamsById) || "基线模型已生成，等待员工报告和风险官复核。"}</p>
                </div>
              ) : (
                <EmptyLine text={selectedMatch ? "这场比赛未通过预测资格检查，不展示旧预测或占位预测。" : "请选择一场已通过资格检查的比赛。"} />
              )}
            </article>
          </section>
        </main>

        <aside className="warroom-right-panel">
          <section className="model-console">
            <div className="warroom-panel-head">
              <div>
                <span>模型控制台</span>
                <strong>{gatewayHealth?.online ? "网关在线" : "等待模型节点"}</strong>
              </div>
              {gatewayHealth?.online ? <Wifi size={18} /> : <WifiOff size={18} />}
            </div>
            <p>{gatewayHealth?.message ?? "尚未检测模型网关。检测不会触发生成，也不会消耗 token。"}</p>
            <div className="gateway-facts">
              <span>目标：{gatewayHealth?.target_url ?? "http://127.0.0.1:5050"}</span>
              <span>延迟：{gatewayHealth ? `${gatewayHealth.latency_ms}ms` : "-"}</span>
              <span>检查成本：0 token</span>
            </div>
            <div className="model-actions">
              <button onClick={refreshGatewayHealth} className="ghost-action" type="button">
                <RefreshCw size={16} /> 检测网关
              </button>
              <button
                onClick={runSelectedTeamReview}
                className="primary-action"
                type="button"
                disabled={reviewStatus === "running" || contextStatus === "loading" || (gatewayHealth ? !gatewayHealth.online : false)}
              >
                {reviewStatus === "running" ? <Loader2 size={16} /> : <Sparkles size={16} />}
                生成单队审查
              </button>
            </div>
            <div className="status-ribbon">
              {reviewStatus === "failed" ? <AlertTriangle size={16} /> : <Activity size={16} />}
              <span>{statusText}</span>
            </div>
          </section>

          <section className="report-console">
            <CardTitle icon={<FileText size={18} />} title="员工产物" subtitle="最新报告、模型审查和归档摘要" />
            {review ? (
              <article className={`review-result ${review.passed ? "passed" : "failed"}`}>
                <span>{review.passed ? "模型审查完成" : "模型审查需复核"}</span>
                <strong>{displayArtifactTitle(review.artifact?.title, selectedTeam)}</strong>
                <p>{cleanReportContent(review.content)}</p>
              </article>
            ) : latestReport.length > 0 ? (
              <article className="review-result">
                <span>最近员工报告</span>
                <strong>{displayArtifactTitle(workbench?.latest_report?.artifact.title, selectedTeam)}</strong>
                {latestReport.map((line) => <p key={line}>{line}</p>)}
              </article>
            ) : (
              <EmptyLine text="还没有可展示报告。可先运行采集闭环，再生成单队审查。" />
            )}
          </section>

          <section className="source-console">
            <CardTitle icon={<CheckCircle2 size={18} />} title="数据源分层" subtitle="稳定源用于赛程，RSS 只做发现信号" />
            <div className="source-list">
              {(readiness?.registered_sources ?? []).slice(0, 6).map((source) => (
                <article key={source.source_name}>
                  <span>{sourceLabel(source.source_name)}</span>
                  <strong>{source.enabled ? "已启用" : "未启用"}</strong>
                  <em>{Math.round(source.reliability_score * 100)} 分</em>
                </article>
              ))}
            </div>
          </section>
        </aside>
      </div>
    </section>
  );
}

function MetricCard({ label, value, tone }: { label: string; value: string; tone: "good" | "warn" | "info" | "plain" }) {
  return (
    <article className={`metric-card ${tone}`}>
      <span>{label}</span>
      <strong>{value}</strong>
    </article>
  );
}

function Badge({ label, value, tone = "plain" }: { label: string; value: string; tone?: "good" | "warn" | "plain" }) {
  return (
    <div className={`team-badge ${tone}`}>
      <span>{label}</span>
      <strong>{value}</strong>
    </div>
  );
}

function CardTitle({ icon, title, subtitle }: { icon: React.ReactNode; title: string; subtitle: string }) {
  return (
    <div className="warroom-card-title">
      {icon}
      <div>
        <strong>{title}</strong>
        <span>{subtitle}</span>
      </div>
    </div>
  );
}

function ProbabilityBar({ label, value }: { label: string; value: number }) {
  return (
    <div className="warroom-prob-row">
      <span>{label}</span>
      <div><i style={{ width: pct(value) }} /></div>
      <strong>{pct(value)}</strong>
    </div>
  );
}

function EmptyLine({ text }: { text: string }) {
  return <div className="empty-line">{text}</div>;
}

function LoadingLine({ text }: { text: string }) {
  return (
    <div className="loading-line">
      <Loader2 size={16} />
      <span>{text}</span>
    </div>
  );
}

function employeeForTeam(
  objectId: string,
  assignmentByObject: Map<string, { employee_id: string }>,
  employeesById: Map<string, Employee>
) {
  const assignment = assignmentByObject.get(objectId);
  return assignment ? employeesById.get(assignment.employee_id) ?? null : null;
}

function parseTeamMetadata(json: string | undefined): { group?: string; rank?: number } {
  if (!json || json === "{}") return {};
  try {
    const data = JSON.parse(json) as Record<string, unknown>;
    return {
      group: typeof data.group === "string" ? data.group : undefined,
      rank: typeof data.rank === "number" ? data.rank : undefined
    };
  } catch {
    return {};
  }
}

function sourceHealthSummary(sources: { enabled: boolean; reliability_score: number; source_name: string }[]) {
  return {
    enabled: sources.filter((source) => source.enabled).length,
    strong: sources.filter((source) => source.enabled && source.reliability_score >= 0.7).length,
    needsReview: sources.filter((source) => source.enabled && source.reliability_score < 0.55).length
  };
}

function groupSortValue(group: string) {
  return group === "未分组" ? "ZZ" : group;
}

function stageLabel(stage: string) {
  const labels: Record<string, string> = {
    pre_tournament: "赛前",
    group: "小组赛",
    group_stage: "小组赛",
    round_of_32: "32 强",
    round_of_16: "16 强",
    quarter_final: "八强",
    semi_final: "半决赛",
    final: "决赛",
    post_review: "赛后"
  };
  return labels[stage] ?? stage;
}

function sourceLabel(source: string) {
  const labels: Record<string, string> = {
    fifa_official_reference: "FIFA 官方参考",
    worldcup26_games: "WorldCup26 赛程",
    worldcup26_teams: "WorldCup26 球队",
    openfootball_schedule: "OpenFootball 赛程",
    fixturedownload_schedule: "FixtureDownload",
    rss_soccer_news: "公开足球新闻 RSS",
    football_data_worldcup: "football-data",
    the_odds_api_worldcup: "赔率 API",
    intelligence_signal: "情报信号"
  };
  return labels[source] ?? source;
}

function evidenceKindLabel(kind: string) {
  const labels: Record<string, string> = {
    injury_risk: "伤病风险",
    lineup_news: "阵容动态",
    news_intel: "新闻情报",
    team_profile: "球队资料",
    fixture: "赛程快照",
    group_table: "分组资料",
    stadium_profile: "场馆资料"
  };
  return labels[kind] ?? kind;
}

function formatDate(value: string | null | undefined) {
  if (!value) return "时间待定";
  const date = new Date(value.replace(" ", "T"));
  if (Number.isNaN(date.getTime())) return value.slice(0, 16);
  return date.toLocaleString("zh-CN", { month: "2-digit", day: "2-digit", hour: "2-digit", minute: "2-digit" });
}

function leadingPredictionText(
  prediction: { home_win_probability: number; draw_probability: number; away_win_probability: number },
  home: WatchObject | undefined,
  away: WatchObject | undefined
) {
  const choices = [
    { label: `${teamShortName(home)}胜`, value: prediction.home_win_probability },
    { label: "平局", value: prediction.draw_probability },
    { label: `${teamShortName(away)}胜`, value: prediction.away_win_probability }
  ].sort((a, b) => b.value - a.value);
  return `${choices[0].label} ${pct(choices[0].value)}`;
}

function toChineseEmployeeName(name: string, team?: WatchObject | null) {
  const teamName = team ? teamDisplayName(team) : "";
  if (/team researcher/i.test(name) && teamName) return `${teamName}研究员`;
  if (/risk/i.test(name)) return "风险官";
  if (/data/i.test(name)) return "数据分析员";
  if (/ceo/i.test(name)) return "CEO";
  if (/hr/i.test(name)) return "HR";
  return name;
}

function cleanEvidenceSummary(value: string) {
  if (!value) return "暂无摘要";
  return compactText(
    value
      .replace(/\\u003C/g, "<")
      .replace(/\\u003E/g, ">")
      .replace(/\\u0027/g, "'")
      .replace(/\\u0022/g, "\"")
      .replace(/<[^>]+>/g, " ")
      .replace(/\s+/g, " ")
      .replace(/^Injury risk signal from/i, "伤病风险信号：")
      .replace(/^Lineup or squad signal from/i, "阵容名单信号："),
    180
  );
}

function cleanReportContent(value: string) {
  const lines = previewLines(value, 4);
  return lines.join(" ");
}

function previewLines(value: string, limit: number) {
  return value
    .split(/\r?\n/)
    .map((line) => line.replace(/^#+\s*/, "").replace(/^[-*]\s*/, "").trim())
    .filter(Boolean)
    .slice(0, limit)
    .map((line) => compactText(translateKnownNote(line), 150));
}

function compactText(value: string, max: number) {
  if (value.length <= max) return fallbackLabel(value);
  return `${fallbackLabel(value.slice(0, max - 1))}…`;
}

function translateKnownNote(value: string) {
  return value
    .replace("Some attached matches are not prediction-eligible because an opponent is unresolved or demo-only.", "部分关联比赛包含待定席位或演示队伍，系统已禁止生成正式预测。")
    .replace("Actionable team news exists but confidence is below the threshold for direct prediction input.", "存在可行动球队新闻，但置信度不足，不能直接进入概率计算。")
    .replace("RSS evidence is a discovery signal only; verify entity linking before treating it as an injury or lineup fact.", "RSS 只作为发现信号；必须完成人队关联和复核后，才能作为伤病或阵容事实。")
    .replace(/^Team:\s*/i, "球队：")
    .replace(/^Researcher:\s*/i, "研究员：")
    .replace(/^Generated at:\s*/i, "生成时间：")
    .replace(/^Mode:\s*structured no-LLM trigger/i, "模式：结构化生成，未触发大模型")
    .replace(/Mexico Intelligence Brief/i, "墨西哥情报简报")
    .replace(/\bMexico\b/g, "墨西哥")
    .replace(/Team Intelligence Brief/i, "球队情报简报")
    .replace(/intelligence brief/i, "情报简报")
    .replace(/Team Researcher/i, "球队研究员");
}

function predictionExplanationText(value: string, match?: Match | null, teamsById?: Map<string, WatchObject>) {
  if (!value) return "";
  const home = match ? teamDisplayName(teamsById?.get(match.home_object_id), match.home_object_id) : "主队";
  const away = match ? teamDisplayName(teamsById?.get(match.away_object_id), match.away_object_id) : "客队";
  const rankMatch = value.match(/rank\s+(\d+)\s+vs\s+.+?\s+rank\s+(\d+)/i);
  const factorMatch = value.match(/factor score\s+(-?\d+(?:\.\d+)?)/i);
  if (rankMatch) {
    const factor = factorMatch ? `，综合因子分 ${factorMatch[1]}` : "";
    return `${home} 与 ${away} 的 FIFA 排名分别为 ${rankMatch[1]} 和 ${rankMatch[2]}${factor}。基线模型同时考虑排名差、名义主场、热门压力、冷门保护和结构化快照信号；该结果需要员工报告与风险复核。`;
  }
  return value
    .replace(/factor score/i, "因子分")
    .replace(/Factors:/i, "考虑因素：")
    .replace(/ranking edge/i, "排名差")
    .replace(/nominal home edge/i, "名义主场")
    .replace(/favorite pressure/i, "热门压力")
    .replace(/upset guard/i, "冷门保护")
    .replace(/structured snapshot signals/i, "结构化快照信号");
}

function displayArtifactTitle(title: string | undefined, team?: WatchObject | null) {
  if (!title) return team ? `${teamDisplayName(team)}研究报告` : "球队研究报告";
  const teamName = team ? teamDisplayName(team) : "";
  return title
    .replace(/^Mexico/i, teamName || "墨西哥")
    .replace(/DeepSeek intelligence brief/i, "DeepSeek 情报简报")
    .replace(/intelligence brief/i, "情报简报")
    .replace(/context review/i, "上下文审查");
}

function firstNote(notes: string[] | undefined) {
  return notes?.find((note) => note.trim()) ?? "";
}

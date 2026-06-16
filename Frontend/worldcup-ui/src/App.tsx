import { useCallback, useEffect, useMemo, useState, type ReactNode } from "react";
import {
  AlertTriangle,
  BarChart3,
  Brain,
  ChevronRight,
  DatabaseZap,
  FileText,
  Gauge,
  Goal,
  History,
  Info,
  Layers,
  Loader2,
  RefreshCw,
  Search,
  ShieldCheck,
  Sparkles,
  Target,
  Trophy
} from "lucide-react";
import {
  loadProductAudit,
  loadProductMatchDetail,
  loadProductModelHealth,
  loadProductOverview,
  loadProductTeamResearch
} from "./api/worldcupApi";
import type {
  LlmUsageSummary,
  ProductActivityView,
  ProductAutoCollectionStatus,
  ProductCollectionPriorityView,
  ProductAuditResult,
  ProductDataTrustItem,
  ProductDataCoverageItem,
  ProductEvidenceView,
  ProductMatchDetail,
  ProductMatchQueueItem,
  ProductModelHealthResult,
  ProductMemoryView,
  ProductMetricView,
  ProductMarketSignalView,
  ProductOverviewResult,
  ProductPrematchWatchPlanItem,
  ProductPredictionRuleView,
  ProductProbabilityFactorView,
  ProductRadarMetricView,
  ProductTeamResearchItem,
  ProductTeamResearchResult,
  ProductTeamProfileView,
  ProductTeamView
} from "./types";
import { pct } from "./utils/format";
import { cleanCode, flagSrcForCode, teamAccent, teamInitials } from "./utils/teamAssets";
import { LottieSignal } from "./components/motion/LottieSignal";

const PRODUCT_REFRESH_INTERVAL_MS = 60_000;

type MainSection = "matches" | "research" | "trust" | "reports";
type RightPanel = "evidence" | "memory" | "trust" | "model" | "audit";
type TeamSide = "home" | "away";
type ResearchTeamEntry = {
  team: ProductTeamView;
  profile: ProductTeamProfileView | null;
  employeeName: string;
  matches: ProductMatchQueueItem[];
  evidenceCount: number;
  memoryCount: number;
  reportCount: number;
  peakProbability: number;
  evidence: ProductEvidenceView[];
  memories: ProductMemoryView[];
  recentActivity: ProductActivityView[];
  dataNotes: string[];
};

const panelTabs: Array<{ key: RightPanel; label: string; icon: ReactNode }> = [
  { key: "evidence", label: "证据包", icon: <DatabaseZap size={15} /> },
  { key: "memory", label: "记忆", icon: <Brain size={15} /> },
  { key: "trust", label: "可信度", icon: <ShieldCheck size={15} /> },
  { key: "model", label: "模型", icon: <BarChart3 size={15} /> },
  { key: "audit", label: "日志", icon: <History size={15} /> }
];

export function App() {
  const [overview, setOverview] = useState<ProductOverviewResult | null>(null);
  const [selectedMatchId, setSelectedMatchId] = useState("");
  const [detail, setDetail] = useState<ProductMatchDetail | null>(null);
  const [audit, setAudit] = useState<ProductAuditResult | null>(null);
  const [modelHealth, setModelHealth] = useState<ProductModelHealthResult | null>(null);
  const [teamResearch, setTeamResearch] = useState<ProductTeamResearchResult | null>(null);
  const [teamResearchLoading, setTeamResearchLoading] = useState(false);
  const [modelLoading, setModelLoading] = useState(false);
  const [activeSection, setActiveSection] = useState<MainSection>("matches");
  const [panel, setPanel] = useState<RightPanel>("evidence");
  const [rightPanelOpen, setRightPanelOpen] = useState(false);
  const [loading, setLoading] = useState(true);
  const [busyAction, setBusyAction] = useState("");
  const [query, setQuery] = useState("");
  const [selectedTeamId, setSelectedTeamId] = useState("");
  const [message, setMessage] = useState("正在连接世界杯 AI 公司");

  const refreshOverview = useCallback(async (matchId?: string) => {
    setLoading(true);
    try {
      const next = await loadProductOverview(matchId || selectedMatchId || undefined, 24);
      const nextMatchId = matchId || next.selected_match_id || next.queue[0]?.match_id || "";
      setOverview(next);
      setSelectedMatchId(nextMatchId);
      setDetail(next.featured_match);
      setMessage(next.passed ? "系统数据已同步" : next.notes[0] || "系统需要补充数据");
      if (nextMatchId) {
        void loadProductAudit(nextMatchId)
          .then(setAudit)
          .catch(() => setAudit(null));
      }
    } catch (error) {
      setMessage(error instanceof Error ? error.message : "同步失败");
    } finally {
      setLoading(false);
    }
  }, [selectedMatchId]);

  useEffect(() => {
    refreshOverview();
  }, []);

  useEffect(() => {
    const timer = window.setInterval(() => {
      void refreshOverview(selectedMatchId || undefined);
    }, PRODUCT_REFRESH_INTERVAL_MS);
    return () => window.clearInterval(timer);
  }, [refreshOverview, selectedMatchId]);

  useEffect(() => {
    if (panel !== "model" || modelHealth || modelLoading) return;
    setModelLoading(true);
    loadProductModelHealth()
      .then((result) => {
        setModelHealth(result);
        setMessage(result.passed ? "模型体检数据已同步" : result.notes[0] || "模型体检等待后台回测");
      })
      .catch((error) => {
        setMessage(error instanceof Error ? error.message : "模型体检同步失败");
      })
      .finally(() => setModelLoading(false));
  }, [panel, modelHealth, modelLoading]);

  const filteredQueue = useMemo(() => {
    const queue = overview?.queue ?? [];
    const keyword = query.trim().toLowerCase();
    if (!keyword) return queue;
    return queue.filter((item) =>
      [
        item.home.name_cn,
        item.away.name_cn,
        item.home.code,
        item.away.code,
        item.group_name,
        item.stage_label,
        item.venue
      ].join(" ").toLowerCase().includes(keyword)
    );
  }, [overview, query]);

  const researchTeams = useMemo(() => {
    if (teamResearch?.teams?.length) return mapServerTeamResearch(teamResearch.teams);
    return buildResearchTeams(overview?.queue ?? [], detail);
  }, [overview, detail, teamResearch]);
  const selectedResearchTeam = useMemo(() => {
    if (!researchTeams.length) return null;
    return researchTeams.find((entry) => entry.team.id === selectedTeamId)
      ?? researchTeams.find((entry) => entry.team.id === detail?.queue_item.home.id)
      ?? researchTeams[0];
  }, [researchTeams, selectedTeamId, detail]);

  useEffect(() => {
    if (activeSection !== "research" || teamResearch || teamResearchLoading) return;
    setTeamResearchLoading(true);
    loadProductTeamResearch(200)
      .then((result) => {
        setTeamResearch(result);
        if (result.teams[0] && !selectedTeamId) setSelectedTeamId(result.teams[0].team.id);
        setMessage(result.passed ? "球队研究室数据已同步" : result.notes[0] || "球队研究室等待数据");
      })
      .catch((error) => {
        setMessage(error instanceof Error ? error.message : "球队研究室同步失败，已使用本地赛程降级数据");
      })
      .finally(() => setTeamResearchLoading(false));
  }, [activeSection, teamResearch, teamResearchLoading, selectedTeamId]);

  async function selectMatch(matchId: string) {
    setSelectedMatchId(matchId);
    setBusyAction(`select:${matchId}`);
    try {
      const nextDetail = await loadProductMatchDetail(matchId);
      const nextAudit = await loadProductAudit(matchId).catch(() => null);
      setDetail(nextDetail);
      setAudit(nextAudit);
      setMessage(`${nextDetail.queue_item.home.name_cn} 对阵 ${nextDetail.queue_item.away.name_cn} 已打开`);
    } catch (error) {
      setMessage(error instanceof Error ? error.message : "打开比赛失败");
    } finally {
      setBusyAction("");
    }
  }

  async function selectResearchTeam(entry: ResearchTeamEntry) {
    setSelectedTeamId(entry.team.id);
    const currentMatch = detail?.queue_item;
    const alreadyOpen = currentMatch?.home.id === entry.team.id || currentMatch?.away.id === entry.team.id;
    if (!alreadyOpen && entry.matches[0]?.match_id) {
      await selectMatch(entry.matches[0].match_id);
    }
    setMessage(`${entry.team.name_cn} 研究室已打开`);
  }

  const selected = detail?.queue_item ?? overview?.queue.find((item) => item.match_id === selectedMatchId) ?? overview?.queue[0];
  const shellGridStyle = activeSection === "matches"
    ? undefined
    : {
        gridTemplateColumns: rightPanelOpen
          ? "58px minmax(680px, 1fr) 360px"
          : "58px minmax(820px, 1fr) 58px"
      };

  return (
    <main className="tactical-app">
      <TopNav
        overview={overview}
        message={message}
        loading={loading}
        activeSection={activeSection}
        onSectionChange={setActiveSection}
        onRefresh={() => refreshOverview(selectedMatchId)}
      />

      <section
        className={`tactical-shell section-${activeSection} ${rightPanelOpen ? "intel-open" : "intel-collapsed"}`}
        style={shellGridStyle}
      >
        <aside className="match-queue-panel">
          <div className="panel-head">
            <div>
              <span>近期赛程</span>
              <strong>预测队列</strong>
            </div>
            <small>{filteredQueue.length} 场</small>
          </div>
          <label className="queue-search">
            <Search size={16} />
            <input
              value={query}
              onChange={(event) => setQuery(event.target.value)}
              placeholder="搜索球队、分组、场馆"
            />
          </label>
          <div className="queue-list">
            {filteredQueue.map((item) => (
              <MatchQueueCard
                key={item.match_id}
                item={item}
                active={item.match_id === selectedMatchId}
                busy={busyAction === `select:${item.match_id}`}
                onClick={() => selectMatch(item.match_id)}
              />
            ))}
          </div>
        </aside>

        <section className="tactical-stage">
          {activeSection === "matches" && selected && detail ? (
            <>
              <MatchHero
                detail={detail}
                autoCollection={overview?.auto_collection}
              />
              <AgentWorkflowRibbon detail={detail} autoCollection={overview?.auto_collection} />
              <PrematchWatchPlanStrip items={detail.prematch_watch_plan ?? []} />
              <CollectionPriorityStrip items={detail.collection_priorities ?? []} />
              <TacticalTable detail={detail} />
              <DataCoverageStrip coverage={detail.data_coverage ?? []} />
            </>
          ) : activeSection === "research" ? (
            <TeamResearchRoom
              teams={researchTeams}
              selected={selectedResearchTeam}
              detail={detail}
              busyAction={busyAction}
              loading={teamResearchLoading}
              onSelectTeam={selectResearchTeam}
              onOpenMatch={selectMatch}
              onOpenEvidence={() => {
                setPanel("evidence");
                setRightPanelOpen(true);
              }}
            />
          ) : activeSection === "trust" ? (
            <TrustWorkspace
              dataTrust={overview?.data_trust ?? []}
              metrics={overview?.summary_metrics ?? []}
              autoCollection={overview?.auto_collection}
              usage={overview?.llm_usage}
              onOpenModel={() => {
                setPanel("model");
                setRightPanelOpen(true);
              }}
            />
          ) : activeSection === "reports" ? (
            <ReportArchiveWorkspace detail={detail} activity={detail?.recent_activity ?? []} />
          ) : (
            <EmptyState loading={loading} />
          )}
        </section>

        <aside className={`right-intel-panel ${rightPanelOpen ? "open" : "collapsed"}`}>
          <div className="panel-tabs" data-testid="right-panel-tabs">
            {panelTabs.map((item) => (
              <button
                key={item.key}
                data-panel-tab={item.key}
                className={panel === item.key ? "active" : ""}
                onClick={() => {
                  setPanel(item.key);
                  setRightPanelOpen(true);
                }}
                title={item.label}
              >
                {item.icon}
                {item.label}
              </button>
            ))}
          </div>
          <button className="panel-collapse-toggle" onClick={() => setRightPanelOpen(false)} title="收起右侧面板">
            <ChevronRight size={16} />
            <span>收起</span>
          </button>
          <div className="intel-panel-body">
            {panel === "evidence" && <EvidencePanel evidence={detail?.evidence ?? []} dataNotes={detail?.data_notes ?? []} />}
            {panel === "memory" && <MemoryPanel memories={detail?.memories ?? []} risks={detail?.risks ?? []} review={detail?.model_review ?? ""} />}
            {panel === "trust" && <TrustPanel dataTrust={overview?.data_trust ?? []} autoCollection={overview?.auto_collection} usage={overview?.llm_usage} />}
            {panel === "model" && <ModelPanel health={modelHealth} loading={modelLoading} autoCollection={overview?.auto_collection} />}
            {panel === "audit" && <AuditPanel audit={audit} activity={detail?.recent_activity ?? []} />}
          </div>
        </aside>
      </section>
    </main>
  );
}

function TopNav({
  overview,
  message,
  loading,
  activeSection,
  onSectionChange,
  onRefresh
}: {
  overview: ProductOverviewResult | null;
  message: string;
  loading: boolean;
  activeSection: MainSection;
  onSectionChange: (section: MainSection) => void;
  onRefresh: () => void;
}) {
  const nav: Array<{ key: MainSection; label: string }> = [
    { key: "matches", label: "比赛预测" },
    { key: "research", label: "球队研究室" },
    { key: "trust", label: "数据可信度" },
    { key: "reports", label: "研报归档" }
  ];

  return (
    <header className="top-nav">
      <div className="brand-cluster">
        <div className="brand-emblem">
          <Trophy size={25} />
        </div>
        <div>
          <span>世界杯 AI 公司</span>
          <strong>战术预测桌</strong>
        </div>
      </div>
      <nav className="nav-pills" aria-label="主导航">
        {nav.map((item) => (
          <button
            key={item.key}
            className={activeSection === item.key ? "active" : ""}
            onClick={() => onSectionChange(item.key)}
          >
            {item.label}
          </button>
        ))}
      </nav>
      <div className="system-strip">
        <span className={overview?.auto_collection?.running ? "dot running" : overview?.passed ? "dot good" : "dot warning"} />
        <span>{message}</span>
        {overview?.auto_collection?.running ? (
          <b className="collection-chip">
            采集中 · {overview.auto_collection.current_source_name || "公开源"} · {formatElapsed(overview.auto_collection.elapsed_seconds)}
          </b>
        ) : null}
        <button className="icon-button" onClick={onRefresh} disabled={loading} title="刷新数据">
          {loading ? <Loader2 size={17} className="spin" /> : <RefreshCw size={17} />}
        </button>
      </div>
    </header>
  );
}

function MatchQueueCard({
  item,
  active,
  busy,
  onClick
}: {
  item: ProductMatchQueueItem;
  active: boolean;
  busy: boolean;
  onClick: () => void;
}) {
  return (
    <button className={`queue-card ${active ? "active" : ""}`} onClick={onClick}>
      <div className="queue-time">
        <span>{item.kickoff_label}</span>
        <small>{item.stage_label}</small>
      </div>
      <div className="queue-status-row">
        <span className={`match-status-pill status-${item.status}`}>{item.status_label}</span>
      </div>
      <div className="queue-teams">
        <TeamLine team={item.home} probability={item.prediction.home_win} />
        <TeamLine team={item.away} probability={item.prediction.away_win} />
      </div>
      <div className="queue-meta">
        <span className={`ready-pill ${item.is_ready ? "ready" : ""}`}>
          {item.is_ready ? "资料包已就绪" : "等待资料"}
        </span>
        <span>
          {item.status === "finished" && item.home_score != null && item.away_score != null
            ? `${item.home_score}:${item.away_score}`
            : item.prediction.favorite_label}
        </span>
        {busy ? <Loader2 size={14} className="spin" /> : <ChevronRight size={14} />}
      </div>
    </button>
  );
}

function TeamLine({ team, probability }: { team: ProductTeamView; probability: number }) {
  return (
    <div className="team-line">
      <TeamBadge team={team} size="sm" />
      <strong>{team.name_cn}</strong>
      <em>{pct(probability)}</em>
    </div>
  );
}

function MatchHero({
  detail,
  autoCollection
}: {
  detail: ProductMatchDetail;
  autoCollection?: ProductAutoCollectionStatus;
}) {
  const item = detail.queue_item;
  const predictionReady = Boolean(item.prediction.updated_at);
  const evidenceReady = item.evidence_count > 0;
  const statusLabel = predictionReady
    ? "预测已就绪"
    : evidenceReady
      ? "证据已同步"
      : autoCollection?.last_completed_at
        ? "等待预测刷新"
        : "等待后台首轮";
  const statusDetail = predictionReady
    ? `胜率刷新 ${item.prediction.updated_at}`
    : evidenceReady
      ? `${item.evidence_count} 条证据已进入资料包`
      : autoCollection?.last_completed_at || "系统启动后由后台采集器生成结果";
  const backendStatusLabel = autoCollection?.running ? "采集中"
    : predictionReady ? "预测已就绪"
      : statusLabel;
  const backendStatusDetail = autoCollection?.running
    ? `${autoCollection.current_source_name || "公开数据源"} · 已运行 ${formatElapsed(autoCollection.elapsed_seconds)}`
    : statusDetail;
  return (
    <section className="match-hero">
      <div className="hero-copy">
        <span>{item.stage_label} / {item.venue}</span>
        <h1>{item.home.name_cn} <b>vs</b> {item.away.name_cn}</h1>
        <p>{item.summary}</p>
      </div>
      <div className="hero-actions">
        <div className="kickoff-card">
          <span>开球时间</span>
          <strong>{item.kickoff_label}</strong>
        </div>
        <div className="auto-status-card">
          <LottieSignal variant="ai" label="AI 后台复核中" size="sm" />
          <span>后台预测</span>
          <strong>{backendStatusLabel}</strong>
          <small>{backendStatusDetail}</small>
        </div>
      </div>
    </section>
  );
}

function AgentWorkflowRibbon({
  detail,
  autoCollection
}: {
  detail: ProductMatchDetail;
  autoCollection?: ProductAutoCollectionStatus;
}) {
  const item = detail.queue_item;
  const reportReady = item.report_count > 0 || Boolean(detail.latest_report);
  const evidenceReady = item.evidence_count > 0;
  const modelReady = item.prediction.updated_at || autoCollection?.baseline_predictions_refreshed;
  const workers = [
    {
      label: detail.home_employee?.name || `${item.home.name_cn}研究员`,
      role: "主队研究",
      status: evidenceReady ? "资料包已就绪" : "等待证据",
      tone: evidenceReady ? "good" : "warning"
    },
    {
      label: detail.away_employee?.name || `${item.away.name_cn}研究员`,
      role: "客队研究",
      status: evidenceReady ? "交叉对照中" : "等待证据",
      tone: evidenceReady ? "good" : "warning"
    },
    {
      label: "模型审计员",
      role: "概率复核",
      status: modelReady ? "基线已刷新" : "等待后台刷新",
      tone: modelReady ? "good" : "warning"
    },
    {
      label: "CEO 决策台",
      role: "结论归档",
      status: reportReady ? "研报可读" : "等待新情报触发",
      tone: reportReady ? "good" : "neutral"
    }
  ];

  return (
    <section className="agent-workflow-ribbon">
      <div className="workflow-orbit">
        <LottieSignal variant="ai" label="AI 员工协同状态" size="md" />
        <span>AI 协同</span>
      </div>
      <div className="workflow-worker-grid">
        {workers.map((worker) => (
          <article className={`workflow-worker ${worker.tone}`} key={`${worker.role}-${worker.label}`}>
            <span>{worker.role}</span>
            <strong>{worker.label}</strong>
            <small>{worker.status}</small>
          </article>
        ))}
      </div>
    </section>
  );
}

function PrematchWatchPlanStrip({ items }: { items: ProductPrematchWatchPlanItem[] }) {
  if (!items.length) return null;
  return (
    <section className="prematch-watch-strip">
      <div className="watch-strip-title">
        <span>
          <Target size={16} />
          赛前自动监控计划
        </span>
        <strong>{items.filter((item) => item.status === "ready").length}/{items.length} 已有数据</strong>
      </div>
      <div className="watch-plan-grid">
        {items.map((item) => (
          <article className={`watch-plan-card ${item.status}`} key={item.key}>
            <div>
              <span>{item.label}</span>
              <b>{item.status_label}</b>
            </div>
            <strong>{item.window_label}</strong>
            <p>{item.summary}</p>
            <small>{item.source_label}</small>
          </article>
        ))}
      </div>
    </section>
  );
}

function CollectionPriorityStrip({ items }: { items: ProductCollectionPriorityView[] }) {
  if (!items.length) return null;
  return (
    <section className="collection-priority-strip">
      <div className="priority-strip-title">
        <span>
          <DatabaseZap size={16} />
          下一轮采集重点
        </span>
        <strong>{items.filter((item) => item.status === "pending").length} 项待补强</strong>
      </div>
      <div className="priority-card-grid">
        {items.map((item) => (
          <article className={`priority-card ${item.priority} ${item.status}`} key={`${item.key}-${item.priority}`}>
            <div>
              <span>{item.label}</span>
              <b>{item.priority_label}</b>
            </div>
            <strong>{item.window_label}</strong>
            <p>{item.reason}</p>
            <small>{item.status_label} / {item.suggested_source}</small>
          </article>
        ))}
      </div>
    </section>
  );
}

function TacticalTable({ detail }: { detail: ProductMatchDetail }) {
  const [activeSide, setActiveSide] = useState<TeamSide | null>(null);
  const item = detail.queue_item;
  const activeProfile = activeSide === "home" ? detail.home_profile : activeSide === "away" ? detail.away_profile : null;
  const activeTeam = activeSide === "home" ? item.home : activeSide === "away" ? item.away : null;

  useEffect(() => {
    if (!activeSide) return;
    const closeWhenPointerLeavesTeamSurface = (event: MouseEvent) => {
      const target = event.target as HTMLElement | null;
      if (target?.closest(".team-desk") || target?.closest(".team-popover")) return;
      setActiveSide(null);
    };
    document.addEventListener("mousemove", closeWhenPointerLeavesTeamSurface);
    document.addEventListener("click", closeWhenPointerLeavesTeamSurface, true);
    return () => {
      document.removeEventListener("mousemove", closeWhenPointerLeavesTeamSurface);
      document.removeEventListener("click", closeWhenPointerLeavesTeamSurface, true);
    };
  }, [activeSide]);

  return (
    <section className="table-surface" onMouseLeave={() => setActiveSide(null)}>
      <div className="desk-texture" />
      <div className="table-props">
        <span className="pin pin-a" />
        <span className="pin pin-b" />
        <span className="pencil" />
        <span className="paper-tag">AI 复核</span>
      </div>
      <div className="pitch-grid" />
      <div className="table-layout">
        <TeamDesk
          side="home"
          team={item.home}
          profile={detail.home_profile}
          employee={detail.home_employee?.name ?? "主队研究员"}
          probability={item.prediction.home_win}
          evidenceCount={item.evidence_count}
          memoryCount={item.memory_count}
          reportCount={item.report_count}
          factorSummary={summarizeTeamFactor(item.prediction.factors ?? [], "home")}
          onOpen={() => setActiveSide("home")}
          onHover={() => setActiveSide("home")}
        />
        <CenterPrediction item={item} />
        <TeamDesk
          side="away"
          team={item.away}
          profile={detail.away_profile}
          employee={detail.away_employee?.name ?? "客队研究员"}
          probability={item.prediction.away_win}
          evidenceCount={item.evidence_count}
          memoryCount={item.memory_count}
          reportCount={item.report_count}
          factorSummary={summarizeTeamFactor(item.prediction.factors ?? [], "away")}
          onOpen={() => setActiveSide("away")}
          onHover={() => setActiveSide("away")}
        />
        <ProbabilityBars item={item} marketSignals={detail.market_signals ?? []} />
        <PredictionRulePanel
          rule={detail.prediction_rule}
          factors={item.prediction.factors ?? []}
          homeName={item.home.name_cn}
          awayName={item.away.name_cn}
        />
      </div>
      {activeProfile && activeTeam ? (
        <TeamProfilePopover
          side={activeSide ?? "home"}
          team={activeTeam}
          profile={activeProfile}
          evidence={detail.evidence}
          memories={detail.memories}
          onClose={() => setActiveSide(null)}
        />
      ) : null}
    </section>
  );
}

function DataCoverageStrip({ coverage }: { coverage: ProductDataCoverageItem[] }) {
  const ready = coverage.filter((item) => item.status === "ready").length;
  const total = coverage.length;
  return (
    <section className="data-coverage-strip">
      <div className="coverage-title">
        <span>
          <DatabaseZap size={16} />
          数据覆盖率
        </span>
        <strong>{ready}/{total || 0} 已就绪</strong>
      </div>
      <div className="coverage-grid">
        {(coverage.length ? coverage : []).map((item) => (
          <article className={`coverage-card ${item.status === "ready" ? "ready" : "missing"}`} key={item.key}>
            <div>
              <span>{item.label}</span>
              <b>{item.status_label}</b>
            </div>
            <p>{item.summary}</p>
            <small>{item.source_label}{item.updated_at ? ` / ${item.updated_at}` : ""}</small>
          </article>
        ))}
        {!coverage.length ? (
          <article className="coverage-card missing">
            <div>
              <span>数据覆盖率</span>
              <b>待生成</b>
            </div>
            <p>当前详情接口还没有返回覆盖率，建议先刷新公开数据源。</p>
            <small>系统状态</small>
          </article>
        ) : null}
      </div>
    </section>
  );
}

function buildResearchTeams(queue: ProductMatchQueueItem[], detail: ProductMatchDetail | null): ResearchTeamEntry[] {
  const map = new Map<string, ResearchTeamEntry>();
  const add = (team: ProductTeamView, match: ProductMatchQueueItem, probability: number) => {
    const current = map.get(team.id);
    if (current) {
      current.matches.push(match);
      current.evidenceCount += match.evidence_count;
      current.memoryCount += match.memory_count;
      current.reportCount += match.report_count;
      current.peakProbability = Math.max(current.peakProbability, probability);
      return;
    }
    map.set(team.id, {
      team,
      profile: null,
      employeeName: `${team.name_cn}研究员`,
      matches: [match],
      evidenceCount: match.evidence_count,
      memoryCount: match.memory_count,
      reportCount: match.report_count,
      peakProbability: probability,
      evidence: [],
      memories: [],
      recentActivity: [],
      dataNotes: []
    });
  };

  queue.forEach((match) => {
    add(match.home, match, match.prediction.home_win);
    add(match.away, match, match.prediction.away_win);
  });

  if (detail?.home_profile) {
    const entry = map.get(detail.queue_item.home.id);
    if (entry) entry.profile = detail.home_profile;
  }
  if (detail?.away_profile) {
    const entry = map.get(detail.queue_item.away.id);
    if (entry) entry.profile = detail.away_profile;
  }

  return Array.from(map.values()).sort((a, b) => {
    const group = a.team.group.localeCompare(b.team.group, "zh-Hans-CN");
    if (group !== 0) return group;
    return (a.team.fifa_rank ?? 999) - (b.team.fifa_rank ?? 999);
  });
}

function mapServerTeamResearch(items: ProductTeamResearchItem[]): ResearchTeamEntry[] {
  return items.map((item) => ({
    team: item.team,
    profile: item.profile,
    employeeName: item.employee?.name || `${item.team.name_cn}研究员`,
    matches: item.matches,
    evidenceCount: item.evidence_count,
    memoryCount: item.memory_count,
    reportCount: item.report_count,
    peakProbability: item.peak_probability,
    evidence: item.evidence,
    memories: item.memories,
    recentActivity: item.recent_activity,
    dataNotes: item.data_notes
  }));
}

function TeamResearchRoom({
  teams,
  selected,
  detail,
  busyAction,
  loading,
  onSelectTeam,
  onOpenMatch,
  onOpenEvidence
}: {
  teams: ResearchTeamEntry[];
  selected: ResearchTeamEntry | null;
  detail: ProductMatchDetail | null;
  busyAction: string;
  loading: boolean;
  onSelectTeam: (entry: ResearchTeamEntry) => void;
  onOpenMatch: (matchId: string) => void;
  onOpenEvidence: () => void;
}) {
  const profile = selected?.profile ?? null;
  const team = selected?.team ?? null;
  const relatedEvidence = selected?.evidence?.length
    ? selected.evidence
    : team && detail
      ? detail.evidence.filter((item) =>
        item.summary.includes(team.name_cn)
        || item.summary.includes(team.name)
        || item.summary.includes(team.code)
      )
      : [];
  const nextMatch = selected?.matches[0];

  return (
    <section className="research-room">
      <div className="research-hero">
        <div>
          <span>球队研究室</span>
          <h1>{team ? `${team.name_cn} 档案台` : "选择一支球队"}</h1>
          <p>每支球队对应一名 AI 研究员，自动沉淀赛程、公开数据、证据快照、长期记忆和模型变量，用于后续每场比赛预测。</p>
        </div>
        {team ? (
          <div className="research-hero-card">
            <TeamBadge team={team} size="xl" />
            <strong>{loading ? "正在同步球队研究室数据..." : profile?.headline || `${team.name_cn} 的资料正在由研究员整理。`}</strong>
          </div>
        ) : null}
      </div>

      <div className="research-layout">
        <aside className="team-directory">
          <div className="board-title">
            <Search size={16} />
            <span>球队目录</span>
            <small>{teams.length} 队</small>
          </div>
          <div className="team-directory-list">
            {teams.map((entry) => (
              <button
                key={entry.team.id}
                className={selected?.team.id === entry.team.id ? "active" : ""}
                onClick={() => onSelectTeam(entry)}
              >
                <TeamBadge team={entry.team} size="md" />
                <span>
                  <strong>{entry.team.name_cn}</strong>
                  <small>{entry.team.group || "分组待定"} / FIFA {entry.team.fifa_rank ?? "暂无"}</small>
                </span>
                <em>{pct(entry.peakProbability)}</em>
              </button>
            ))}
          </div>
        </aside>

        <section className="research-main">
          {team && selected ? (
            <>
              <div className="research-profile">
                <div className="research-team-card">
                  <div className="profile-head">
                    <TeamBadge team={team} size="xl" />
                    <div>
                      <span>{team.group || "分组待定"} / {team.status || "状态待核验"}</span>
                      <strong>{team.name_cn}</strong>
                      <small>FIFA 排名 {team.fifa_rank ?? "暂无"} / 阵型 {profile?.formation || "待核验"} / 研究员 {selected.employeeName}</small>
                    </div>
                  </div>
                  <div className="profile-tags">
                    {(profile?.style_tags.length ? profile.style_tags : ["等待情报分拣", "赛前复核"]).map((tag) => <span key={tag}>{tag}</span>)}
                  </div>
                  <div className="profile-intel-metrics">
                    {(profile?.intel_metrics.length ? profile.intel_metrics : fallbackTeamMetrics(selected)).map((metric) => (
                      <span className={metric.tone} key={`${metric.label}-${metric.value}`}>
                        <b>{metric.label}</b>
                        {metric.value}
                      </span>
                    ))}
                  </div>
                </div>
                <RadarChart metrics={profile?.radar ?? []} />
              </div>

              <div className="research-grid">
                <ProfileBlock title="近期状态" items={profile?.recent_form_notes ?? []} />
                <ProfileBlock title="关键变量" items={profile?.key_variables ?? []} />
                <ProfileBlock title="优势" items={profile?.strengths ?? []} />
                <ProfileBlock title="短板" items={profile?.weaknesses ?? []} tone="risk" />
                <ProfileBlock title="伤停观察" items={profile?.injury_watch ?? []} tone="risk" />
                <ProfileBlock title="阵容观察" items={profile?.lineup_watch ?? []} />
              </div>

              <div className="research-bottom">
                <section className="research-match-card">
                  <div className="board-title">
                    <Target size={16} />
                    <span>关联比赛</span>
                    <small>{selected.matches.length} 场</small>
                  </div>
                  {selected.matches.slice(0, 5).map((match) => {
                    const isHome = match.home.id === team.id;
                    const opponent = isHome ? match.away : match.home;
                    const probability = isHome ? match.prediction.home_win : match.prediction.away_win;
                    return (
                      <button
                        key={match.match_id}
                        className="research-match-row"
                        onClick={() => onOpenMatch(match.match_id)}
                        disabled={busyAction === `select:${match.match_id}`}
                      >
                        <span>{match.kickoff_label}</span>
                        <strong>对 {opponent.name_cn}</strong>
                        <em>{pct(probability)}</em>
                        {busyAction === `select:${match.match_id}` ? <Loader2 size={14} className="spin" /> : <ChevronRight size={14} />}
                      </button>
                    );
                  })}
                </section>

                <section className="research-evidence-card">
                  <div className="board-title">
                    <DatabaseZap size={16} />
                    <span>证据与记忆</span>
                    <small>{selected.evidenceCount} 证据 / {selected.memoryCount} 记忆</small>
                  </div>
                  <div className="research-evidence-strip">
                    <MetricTile label="证据快照" value={`${selected.evidenceCount} 条`} tone={selected.evidenceCount > 0 ? "good" : "warning"} />
                    <MetricTile label="长期记忆" value={`${selected.memoryCount} 条`} tone={selected.memoryCount > 0 ? "good" : "neutral"} />
                    <MetricTile label="研报产物" value={`${selected.reportCount} 份`} />
                  </div>
                  <ProfileEvidence title="当前可追溯证据" evidence={relatedEvidence.slice(0, 5)} />
                  {selected.dataNotes.length ? (
                    <div className="note-box compact">
                      {selected.dataNotes.map((note) => <span key={note}>{note}</span>)}
                    </div>
                  ) : null}
                  <button className="secondary-action" onClick={onOpenEvidence}>打开右侧证据包</button>
                </section>

                <section className="research-action-card">
                  <div className="board-title">
                    <Brain size={16} />
                    <span>研究员下一步</span>
                  </div>
                  <p>赛前优先补强伤停、首发、赔率与临场新闻。大模型只负责抽取、压缩和复核证据，不直接替代结构化胜率模型。</p>
                  <div className="research-next-match">
                    <span>下一场</span>
                    <strong>{nextMatch ? `${nextMatch.home.name_cn} vs ${nextMatch.away.name_cn}` : "暂无赛程"}</strong>
                    <small>{nextMatch?.kickoff_label ?? "等待公开赛程更新"}</small>
                  </div>
                </section>
              </div>
            </>
          ) : (
            <EmptyState loading={false} />
          )}
        </section>
      </div>
    </section>
  );
}

function fallbackTeamMetrics(entry: ResearchTeamEntry): ProductMetricView[] {
  return [
    { label: "证据", value: `${entry.evidenceCount} 条`, tone: entry.evidenceCount > 0 ? "good" : "warning" },
    { label: "记忆", value: `${entry.memoryCount} 条`, tone: entry.memoryCount > 0 ? "good" : "neutral" },
    { label: "最高胜率", value: pct(entry.peakProbability), tone: entry.peakProbability >= 0.58 ? "good" : "warning" }
  ];
}

function TrustWorkspace({
  dataTrust,
  metrics,
  autoCollection,
  usage,
  onOpenModel
}: {
  dataTrust: ProductDataTrustItem[];
  metrics: ProductMetricView[];
  autoCollection?: ProductAutoCollectionStatus;
  usage?: LlmUsageSummary;
  onOpenModel: () => void;
}) {
  return (
    <section className="workspace-page">
      <div className="research-hero">
        <div>
          <span>数据可信度</span>
          <h1>公开数据源体检</h1>
          <p>这里集中查看数据覆盖、来源稳定性和模型调用成本。更细的模型回测放在右侧“模型体检”面板。</p>
        </div>
        <button className="primary-action" onClick={onOpenModel}>
          <BarChart3 size={18} />
          打开模型体检
        </button>
      </div>
      <InsightStrip metrics={metrics} />
      <AutoCollectionBanner autoCollection={autoCollection} />
      <SourceRunBoard autoCollection={autoCollection} />
      <DataSourceFlow sources={dataTrust} />
      <div className="trust-workspace-grid">
        {dataTrust.map((item) => (
          <article className="trust-card detailed" key={item.source_name}>
            <div>
              <strong>{item.source_name}</strong>
              <span>{item.provider}</span>
            </div>
            <GaugeRing value={item.reliability_score} />
            <small>{item.authority_label} / 稳定性 {item.stability_label} / {item.snapshot_count} 条快照 / {item.freshness_label}</small>
            <SourceBoundary item={item} />
          </article>
        ))}
        <article className="usage-card">
          <span>大模型调用成本</span>
          <strong>{usage?.calls ?? 0} 次</strong>
          <small>预估 ${usage?.estimated_cost_usd?.toFixed(4) ?? "0.0000"} / 失败 {usage?.failed_calls ?? 0} 次</small>
        </article>
      </div>
    </section>
  );
}

function SourceRunBoard({ autoCollection }: { autoCollection?: ProductAutoCollectionStatus }) {
  const runs = autoCollection?.source_runs ?? [];
  if (!runs.length) {
    return (
      <section className="source-run-board empty">
        <div>
          <span>采集源明细</span>
          <strong>等待首轮自动采集</strong>
          <p>后台完成一次采集后，这里会显示每个公开源的耗时、导入量和错误原因。</p>
        </div>
      </section>
    );
  }

  const slowRuns = runs.filter((run) => run.tone === "warning").length;
  const failedRuns = runs.filter((run) => run.tone === "danger").length;
  return (
    <section className="source-run-board">
      <div className="source-run-head">
        <div>
          <span>采集源明细</span>
          <strong>{runs.length} 个公开源运行记录</strong>
        </div>
        <small>{failedRuns ? `${failedRuns} 个失败` : "无失败"} / {slowRuns ? `${slowRuns} 个偏慢` : "耗时正常"}</small>
      </div>
      <div className="source-run-list">
        {runs.map((run) => (
          <article className={`source-run-row ${run.tone}`} key={`${run.id}-${run.elapsed_ms}`}>
            <div>
              <b>{run.source_name}</b>
              <span>{run.provider || "custom"} / 超时 {run.timeout_seconds}s</span>
            </div>
            <strong>{run.status_label}</strong>
            <em>{run.elapsed_label}</em>
            <small>{run.raw_items} 原始 / {run.imported} 新 / {run.skipped_duplicates} 重复 / 刷新 {run.baseline_predictions_refreshed}</small>
            {run.error_message ? <p>{translateAutoCollectionNote(run.error_message)}</p> : null}
          </article>
        ))}
      </div>
    </section>
  );
}

function DataSourceFlow({ sources }: { sources: ProductDataTrustItem[] }) {
  const activeSources = sources.filter((item) => item.enabled);
  const staleSources = activeSources.filter((item) => item.freshness_label.includes("天前") || item.freshness_label.includes("暂无"));
  const modelReady = activeSources.filter((item) => item.best_for.some((usage) =>
    usage.includes("排名") || usage.includes("实力") || usage.includes("近期") || usage.includes("校准")
  ));

  return (
    <section className="source-flow-board">
      <div className="source-flow-copy">
        <span>数据流转</span>
        <strong>公开源进入预测模型的路径</strong>
        <p>赛程、排名、Elo、近期战绩会先被结构化和去重；新闻类资料只进入分拣队列，复核后才允许影响胜率。</p>
      </div>
      <div className="source-flow-line" aria-label="数据源流转">
        <FlowNode label="采集" value={`${activeSources.length} 源`} tone="good" />
        <FlowConnector />
        <FlowNode label="去重" value={`${sources.reduce((sum, item) => sum + item.snapshot_count, 0)} 快照`} tone="neutral" />
        <FlowConnector animated />
        <FlowNode label="可入模" value={`${modelReady.length} 源`} tone="good" />
        <FlowConnector />
        <FlowNode label="需关注" value={`${staleSources.length} 源`} tone={staleSources.length ? "warning" : "good"} />
      </div>
    </section>
  );
}

function FlowNode({ label, value, tone }: { label: string; value: string; tone: string }) {
  return (
    <div className={`flow-node ${tone}`}>
      <span>{label}</span>
      <strong>{value}</strong>
    </div>
  );
}

function FlowConnector({ animated = false }: { animated?: boolean }) {
  return (
    <div className={`flow-connector ${animated ? "animated" : ""}`}>
      {animated ? <LottieSignal variant="evidence" label="证据流转" size="wide" /> : null}
    </div>
  );
}

function AutoCollectionBanner({ autoCollection }: { autoCollection?: ProductAutoCollectionStatus }) {
  const skippedText = autoCollection?.reports_skipped_reason
    ? translateAutoCollectionNote(autoCollection.reports_skipped_reason)
    : autoCollection?.reports_created
      ? `本轮生成 ${autoCollection.reports_created} 份员工报告`
      : "无新情报时不会触发大模型报告";
  const llmStatus = autoCollection?.auto_llm_reports_enabled ? "已开启" : "已关闭";
  const llmDetail = autoCollection?.auto_llm_reports_enabled
    ? `间隔 ${autoCollection.llm_report_interval_minutes} 分钟 / 上次生成 ${autoCollection.last_llm_reports_created} 份`
    : "普通采集不会自动调用 DeepSeek 深度报告";
  const collectionState = autoCollection?.running
    ? `运行中 · ${autoCollection.current_source_name || "公开源"}`
    : autoCollection?.enabled ? "已开启" : "未开启";
  const collectionDetail = autoCollection?.running
    ? `本轮已运行 ${formatElapsed(autoCollection.elapsed_seconds)}`
    : `${autoCollection?.adaptive_schedule_enabled ? "动态频率" : "固定频率"} / 基础 ${autoCollection?.interval_minutes ?? 30} 分钟`;
  const lastRunTitle = autoCollection?.running
    ? `${autoCollection.sources_succeeded}/${autoCollection.sources_checked} 源已完成`
    : autoCollection?.sources_checked ? `${autoCollection.sources_succeeded}/${autoCollection.sources_checked} 源成功` : "暂无记录";
  const lastRunDetail = autoCollection?.running
    ? "后台仍在采集，本轮完成后会写入最终统计。"
    : autoCollection?.last_completed_at || "当前进程还没有完成采集。";
  const sourceOrderNote = autoCollection?.notes.find((note) => note.startsWith("source_order:"));
  return (
    <section className="auto-collection-banner">
      <div className={`animated-status-cell ${autoCollection?.running ? "running" : ""}`}>
        <LottieSignal variant="data" label="公开数据源采集状态" size="sm" />
        <span>自动采集</span>
        <strong>{collectionState}</strong>
        <small>{collectionDetail}</small>
      </div>
      <div>
        <span>下次间隔</span>
        <strong>{autoCollection?.next_interval_minutes ? `${autoCollection.next_interval_minutes} 分钟` : "等待计算"}</strong>
        <small>{autoCollection?.next_interval_reason || "启动后根据下一场比赛时间计算。"}</small>
      </div>
      <div>
        <span>上次采集</span>
        <strong>{lastRunTitle}</strong>
        <small>{lastRunDetail}</small>
      </div>
      <div>
        <span>新增数据</span>
        <strong>{autoCollection ? `${autoCollection.imported} 新 / ${autoCollection.skipped_duplicates} 重复` : "等待采集"}</strong>
        <small>刷新预测 {autoCollection?.baseline_predictions_refreshed ?? 0} 场 / 新信号 {autoCollection?.signals_created ?? 0} 条</small>
      </div>
      <div className="collection-strategy-cell">
        <span>本轮策略</span>
        <strong>{sourceOrderNote ? "动态排序" : "常规顺序"}</strong>
        <small>{sourceOrderNote ? translateAutoCollectionNote(sourceOrderNote) : "按照公开源配置顺序采集，并结合赛程窗口自动调整下次间隔。"}</small>
      </div>
      <div>
        <span>员工简报</span>
        <strong>{autoCollection?.reports_created ? `${autoCollection.reports_created} 份` : "按需跳过"}</strong>
        <small>{skippedText}</small>
      </div>
      <div>
        <span>LLM 深度报告</span>
        <strong>{llmStatus}</strong>
        <small>{llmDetail}</small>
      </div>
    </section>
  );
}

function translateAutoCollectionNote(note: string): string {
  return note
    .replace("source_order:", "采集排序：")
    .replace("employee_trigger: skipped because no new snapshots were imported.", "本轮没有新增快照，已跳过员工报告，避免无意义消耗 token。")
    .replace("Auto collection failed:", "自动采集失败：");
}

function SourceBoundary({ item }: { item: ProductDataTrustItem }) {
  return (
    <div className="source-boundary">
      <div>
        <b>适合</b>
        {(item.best_for.length ? item.best_for : ["用途待定义"]).slice(0, 3).map((value) => <span key={value}>{value}</span>)}
      </div>
      <div>
        <b>不适合</b>
        {(item.not_for.length ? item.not_for : ["边界待定义"]).slice(0, 3).map((value) => <span key={value}>{value}</span>)}
      </div>
    </div>
  );
}

function ReportArchiveWorkspace({ detail, activity }: { detail: ProductMatchDetail | null; activity: ProductActivityView[] }) {
  return (
    <section className="workspace-page">
      <div className="research-hero">
        <div>
          <span>研报归档</span>
          <h1>{detail ? `${detail.queue_item.home.name_cn} vs ${detail.queue_item.away.name_cn}` : "暂无选中比赛"}</h1>
          <p>研报归档会保存 AI 员工生成的判断、证据链、模型审查和赛后复盘。当前先展示最近一场的可用产物。</p>
        </div>
      </div>
      <div className="report-layout">
        <article className="report-paper">
          <span>最新研报</span>
          <p>{detail?.latest_report || "暂无研报摘要。生成深度报告后，这里会呈现核心结论和证据引用。"}</p>
        </article>
        <AuditPanel audit={null} activity={activity} />
      </div>
    </section>
  );
}

function TeamDesk({
  side,
  team,
  profile,
  employee,
  probability,
  evidenceCount,
  memoryCount,
  reportCount,
  factorSummary,
  onOpen,
  onHover
}: {
  side: TeamSide;
  team: ProductTeamView;
  profile: ProductTeamProfileView | null;
  employee: string;
  probability: number;
  evidenceCount: number;
  memoryCount: number;
  reportCount: number;
  factorSummary: string;
  onOpen: () => void;
  onHover: () => void;
}) {
  const tags = (profile?.style_tags ?? []).slice(0, 2);

  return (
    <button
      className={`team-desk ${side} accent-${teamAccent(team.code)}`}
      onClick={onOpen}
      onFocus={onHover}
      onMouseEnter={onHover}
      title={`查看${team.name_cn}球队画像`}
    >
      <TeamBadge team={team} size="lg" />
      <div className="desk-copy">
        <span>{employee}</span>
        <strong>{team.name_cn}</strong>
        <small>
          FIFA 排名 {team.fifa_rank ?? "暂无"} / {profile?.formation || "阵型待核验"} / {team.group || "分组待定"}
        </small>
        <div className="desk-tags">
          {tags.length ? tags.map((tag) => <b key={tag}>{tag}</b>) : <b>信息待核验</b>}
        </div>
      </div>
      <em>{pct(probability)}</em>
      <div className="desk-stats" aria-label={`${team.name_cn}资料状态`}>
        <span><b>{evidenceCount}</b> 证据</span>
        <span><b>{memoryCount}</b> 记忆</span>
        <span><b>{reportCount}</b> 研报</span>
      </div>
      <div className="desk-signal">
        <span>关键判断</span>
        <strong>{factorSummary}</strong>
      </div>
    </button>
  );
}

function summarizeTeamFactor(factors: ProductProbabilityFactorView[], side: TeamSide): string {
  const signed = side === "home"
    ? factors
    : factors.map((factor) => ({ ...factor, home_contribution: -factor.home_contribution }));
  const positive = signed
    .filter((factor) => factor.home_contribution > 0.01)
    .sort((a, b) => Math.abs(b.home_contribution) - Math.abs(a.home_contribution))[0];
  if (positive) return `${positive.label}支撑`;
  const pressure = signed
    .filter((factor) => factor.home_contribution < -0.01)
    .sort((a, b) => Math.abs(b.home_contribution) - Math.abs(a.home_contribution))[0];
  if (pressure) return `${pressure.label}承压`;
  return "暂无显著单边因子";
}

function CenterPrediction({ item }: { item: ProductMatchQueueItem }) {
  const phase = item.prediction.phase;
  const label = phase?.is_post_match && phase.score_label
    ? phase.score_label
    : phase?.primary_label || item.prediction.favorite_label;
  return (
    <div className={`center-disc phase-${phase?.phase ?? "pre_match"}`}>
      <Goal size={26} />
      <span>{label}</span>
      <strong>{phase?.phase_label || item.prediction.confidence_label}</strong>
      <small>{phase?.is_post_match ? phase.primary_label : item.prediction.risk_label}</small>
    </div>
  );
}

function ProbabilityBars({ item, marketSignals }: { item: ProductMatchQueueItem; marketSignals: ProductMarketSignalView[] }) {
  const rows = [
    { label: `${item.home.name_cn}胜`, value: item.prediction.home_win, tone: "home" },
    { label: "平局", value: item.prediction.draw, tone: "draw" },
    { label: `${item.away.name_cn}胜`, value: item.prediction.away_win, tone: "away" }
  ];
  return (
    <div className="probability-board">
      <div className="board-title">
        <BarChart3 size={16} />
        <span>胜率分布</span>
      </div>
      {rows.map((row) => (
        <div className="prob-row" key={row.label}>
          <span>{row.label}</span>
          <div className="prob-track">
            <i className={row.tone} style={{ width: `${Math.max(4, row.value * 100)}%` }} />
          </div>
          <strong>{pct(row.value)}</strong>
        </div>
      ))}
      <PredictionPhaseCard item={item} />
      <PredictionQualityGate gate={item.prediction.quality_gate} />
      {item.prediction.phase?.is_actionable_pre_match ? (
        <div className={`betting-advice action-${item.prediction.betting_advice.action}`}>
          <span>体彩策略</span>
          <strong>{item.prediction.betting_advice.action_label}</strong>
          <p>{item.prediction.betting_advice.suggested_play}</p>
          <small>{item.prediction.betting_advice.threshold}</small>
        </div>
      ) : (
        <div className="betting-advice action-no_bet">
          <span>投注策略</span>
          <strong>赛前建议已冻结</strong>
          <p>{item.prediction.phase?.summary || "当前比赛不处于赛前预测阶段，不输出投注建议。"}</p>
          <small>系统保留概率用于复盘、校准和长期记忆，不作为赛后投注判断。</small>
        </div>
      )}
      <MarketCalibration signals={marketSignals} />
    </div>
  );
}

function PredictionPhaseCard({ item }: { item: ProductMatchQueueItem }) {
  const phase = item.prediction.phase;
  if (!phase) return null;
  return (
    <section className={`prediction-phase-card phase-${phase.phase}`}>
      <div>
        <span>{phase.phase_label}</span>
        <strong>{phase.primary_label}</strong>
      </div>
      <p>{phase.summary}</p>
      {phase.is_post_match ? (
        <small>
          比分 {phase.score_label || "-"} · 预测 {phase.predicted_label || "未知"} · 实际 {phase.actual_label || "未知"}
          {phase.brier_score != null ? ` · Brier ${phase.brier_score.toFixed(3)}` : ""}
        </small>
      ) : (
        <small>{phase.predicted_label ? `当前模型倾向：${phase.predicted_label}` : "等待结构化胜率生成"}</small>
      )}
    </section>
  );
}

function PredictionQualityGate({ gate }: { gate: ProductMatchQueueItem["prediction"]["quality_gate"] }) {
  if (!gate) return null;
  const missing = gate.missing_sources?.length ? gate.missing_sources.join("、") : "无关键缺口";
  return (
    <section className={`quality-gate level-${gate.level}`}>
      <div className="quality-gate-head">
        <span>数据质量门槛</span>
        <strong>{gate.level_label}</strong>
        <b>{pct(gate.score)}</b>
      </div>
      <div className="quality-gate-track">
        <i style={{ width: `${Math.max(4, gate.score * 100)}%` }} />
      </div>
      <p>{gate.explanation}</p>
      <small>
        数据源 {gate.required_sources_ready}/{gate.required_sources_total} · 缺失：{missing}
      </small>
      {gate.source_checks?.length ? (
        <div className="quality-source-grid">
          {gate.source_checks.map((source) => (
            <article className={source.ready ? "ready" : "missing"} key={source.key}>
              <div>
                <b>{source.label}</b>
                <span>{source.status_label}</span>
              </div>
              <small>权重 {pct(source.weight)}</small>
              <p>{source.reason}</p>
            </article>
          ))}
        </div>
      ) : null}
      {gate.warnings?.length ? (
        <ul>
          {gate.warnings.slice(0, 2).map((warning) => <li key={warning}>{warning}</li>)}
        </ul>
      ) : null}
    </section>
  );
}

function MarketCalibration({ signals }: { signals: ProductMarketSignalView[] }) {
  return (
    <section className="market-calibration">
      <div className="market-head">
        <span>市场校准</span>
        <strong>{signals.length ? "已读取赔率" : "等待赔率"}</strong>
      </div>
      {signals.length ? signals.map((signal) => (
        <article className={`market-row ${signal.edge >= 0.015 ? "positive" : signal.edge <= -0.015 ? "negative" : "flat"}`} key={signal.side}>
          <div>
            <span>{signal.label}</span>
            <b>{signal.moneyline == null ? "赔率待定" : formatMoneyline(signal.moneyline)}</b>
          </div>
          <div className="market-bars">
            <i className="model" style={{ width: `${Math.max(4, signal.model_probability * 100)}%` }} />
            <i className="market" style={{ width: `${Math.max(4, signal.market_probability * 100)}%` }} />
          </div>
          <small>
            模型 {pct(signal.model_probability)} / 市场 {pct(signal.market_probability)}
            <em>{formatSignedPct(signal.edge)} · {signal.edge_label}</em>
          </small>
        </article>
      )) : (
        <p>当前比赛尚未拿到可用公开赔率字段；系统会在 ESPN 记分牌后续刷新时自动补齐。</p>
      )}
      {signals[0] ? <footer>{signals[0].provider} / {signals[0].updated_at}</footer> : null}
    </section>
  );
}

function PredictionRulePanel({
  rule,
  factors,
  homeName,
  awayName
}: {
  rule: ProductPredictionRuleView;
  factors: ProductProbabilityFactorView[];
  homeName: string;
  awayName: string;
}) {
  return (
    <section className="rule-drawer open">
      <div className="rule-summary">
        <span>
          <Layers size={16} />
          {rule.title || "结构化胜率规则"}
        </span>
        <small>默认平铺展示</small>
      </div>
      <div className="rule-body">
        <p>{rule.summary}</p>
        <div className="rule-columns">
          <RuleList title="计算步骤" items={rule.steps} />
          <RuleList title="约束条件" items={rule.guardrails} />
          <RuleList title="当前限制" items={rule.limitations} />
        </div>
        <FactorList factors={factors} homeName={homeName} awayName={awayName} />
      </div>
    </section>
  );
}

function RuleList({ title, items }: { title: string; items: string[] }) {
  return (
    <div className="rule-list">
      <strong>{title}</strong>
      {(items.length ? items : ["暂无说明"]).map((item) => <span key={item}>{item}</span>)}
    </div>
  );
}

function FactorList({
  factors,
  homeName,
  awayName
}: {
  factors: ProductProbabilityFactorView[];
  homeName: string;
  awayName: string;
}) {
  return (
    <div className="factor-list">
      <div className="factor-head">
        <Target size={16} />
        <strong>显式因子</strong>
        <span>正值偏向 {homeName}，负值偏向 {awayName}</span>
      </div>
      {(factors.length ? factors : []).map((factor) => {
        const value = Math.max(-1, Math.min(1, factor.home_contribution));
        const width = `${Math.max(5, Math.abs(value) * 100)}%`;
        const direction = value > 0.01 ? "home" : value < -0.01 ? "away" : "neutral";
        const label = direction === "home" ? `偏向 ${homeName}` : direction === "away" ? `偏向 ${awayName}` : "中性";
        return (
          <article className={`factor-row ${direction}`} key={factor.id}>
            <div>
              <strong>{factor.label}</strong>
              <small>{factor.explanation}</small>
            </div>
            <div className="factor-meter" aria-label={`${factor.label} ${label}`}>
              <i style={{ width }} />
            </div>
            <span>{label}</span>
            <em>权重 {pct(Math.min(1, factor.weight))}</em>
          </article>
        );
      })}
    </div>
  );
}

function TeamProfilePopover({
  side,
  team,
  profile,
  evidence,
  memories,
  onClose
}: {
  side: TeamSide;
  team: ProductTeamView;
  profile: ProductTeamProfileView;
  evidence: ProductEvidenceView[];
  memories: ProductMemoryView[];
  onClose: () => void;
}) {
  const relatedEvidence = evidence
    .filter((item) => item.summary.includes(profile.name_cn) || item.summary.includes(team.name) || item.summary.includes(team.code))
    .slice(0, 4);
  const visibleEvidence = (relatedEvidence.length ? relatedEvidence : evidence.slice(0, 4));
  const visibleMemories = memories.slice(0, 3);

  return (
    <aside className={`team-popover ${side}`} onMouseLeave={onClose}>
      <button className="popover-close" onClick={onClose} title="关闭球队画像">×</button>
      <div className="profile-head">
        <TeamBadge team={team} size="xl" />
        <div>
          <span>{profile.group} / {profile.status}</span>
          <strong>{profile.name_cn}</strong>
          <small>FIFA 排名 {profile.fifa_rank ?? "暂无"} / 常用阵型 {profile.formation || "待核验"}</small>
        </div>
      </div>
      <p>{profile.headline}</p>
      <div className="profile-tags">
        {profile.style_tags.map((tag) => <span key={tag}>{tag}</span>)}
      </div>
      <div className="profile-intel-metrics">
        {profile.intel_metrics.map((metric) => (
          <span className={metric.tone} key={`${metric.label}-${metric.value}`}>
            <b>{metric.label}</b>
            {metric.value}
          </span>
        ))}
      </div>
      <div className="profile-grid">
        <RadarChart metrics={profile.radar} />
        <div className="profile-facts">
          <ProfileBlock title="核心球员" items={profile.stars} />
          <ProfileBlock title="优势" items={profile.strengths} />
          <ProfileBlock title="短板" items={profile.weaknesses} tone="risk" />
        </div>
      </div>
      <div className="profile-source-grid">
        <ProfileBlock title="近期状态" items={profile.recent_form_notes} />
        <ProfileBlock title="伤停观察" items={profile.injury_watch} tone="risk" />
        <ProfileBlock title="阵容观察" items={profile.lineup_watch} />
        <ProfileBlock title="关键变量" items={profile.key_variables} />
        <ProfileEvidence title="可用证据源" evidence={visibleEvidence} />
        <ProfileBlock title="系统记忆" items={visibleMemories.map((memory) => memory.summary)} />
        <ProfileBlock title="备注" items={profile.notes} />
      </div>
    </aside>
  );
}

function ProfileEvidence({ title, evidence }: { title: string; evidence: ProductEvidenceView[] }) {
  return (
    <div className="profile-block evidence">
      <strong>{title}</strong>
      {(evidence.length ? evidence : []).map((item) => (
        <span key={item.id}>
          <b>{item.source_label || item.source}</b>
          {item.kind_label ? ` / ${item.kind_label}` : ""}
          {item.fact_label ? ` / ${item.fact_label}` : ""}
          {item.captured_at ? ` / ${item.captured_at.slice(5, 16)}` : ""}
        </span>
      ))}
      {!evidence.length ? <span>暂无直接证据，等待自动采集补充。</span> : null}
    </div>
  );
}

function ProfileBlock({ title, items, tone = "normal" }: { title: string; items: string[]; tone?: "normal" | "risk" }) {
  return (
    <div className={`profile-block ${tone}`}>
      <strong>{title}</strong>
      {(items.length ? items : ["暂无可靠信息"]).map((item) => <span key={item}>{item}</span>)}
    </div>
  );
}

function RadarChart({ metrics }: { metrics: ProductRadarMetricView[] }) {
  const data = metrics.length
    ? metrics
    : [
        { key: "attack", label: "进攻", value: 0.5 },
        { key: "defense", label: "防守", value: 0.5 },
        { key: "form", label: "状态", value: 0.5 },
        { key: "depth", label: "阵容", value: 0.5 },
        { key: "data", label: "数据", value: 0.5 }
      ];
  const points = radarPoints(data, 46);
  const grid = [0.33, 0.66, 1].map((scale) => radarPoints(data.map((item) => ({ ...item, value: scale })), 46));
  return (
    <div className="radar-card">
      <span>球队能力雷达</span>
      <svg viewBox="-68 -68 136 136" role="img" aria-label="球队能力雷达图">
        {grid.map((point, index) => <polygon className="radar-grid-line" points={point} key={index} />)}
        {data.map((item, index) => {
          const angle = (-90 + index * 360 / data.length) * Math.PI / 180;
          const x = Math.cos(angle) * 58;
          const y = Math.sin(angle) * 58;
          return <text x={x} y={y} textAnchor="middle" dominantBaseline="middle" key={item.key}>{item.label}</text>;
        })}
        <polygon className="radar-fill" points={points} />
        <polygon className="radar-stroke" points={points} />
        {data.map((item, index) => {
          const angle = (-90 + index * 360 / data.length) * Math.PI / 180;
          const radius = Math.max(0.08, Math.min(1, item.value)) * 46;
          return <circle cx={Math.cos(angle) * radius} cy={Math.sin(angle) * radius} r="2.5" key={item.key} />;
        })}
      </svg>
    </div>
  );
}

function radarPoints(metrics: ProductRadarMetricView[], radius: number): string {
  return metrics.map((item, index) => {
    const angle = (-90 + index * 360 / metrics.length) * Math.PI / 180;
    const value = Math.max(0, Math.min(1, item.value));
    return `${Math.cos(angle) * radius * value},${Math.sin(angle) * radius * value}`;
  }).join(" ");
}
function TeamBadge({ team, size }: { team: ProductTeamView; size: "sm" | "md" | "lg" | "xl" }) {
  const code = cleanCode(team.code);
  const src = flagSrcForCode(code);
  return (
    <span className={`team-badge ${size} accent-${teamAccent(code)}`}>
      {src ? <img src={src} alt={`${team.name_cn}国旗`} loading="lazy" /> : <b>{code.slice(0, 3)}</b>}
      <i>{teamInitials(team.name_cn, code)}</i>
    </span>
  );
}

function InsightStrip({ metrics }: { metrics: ProductMetricView[] }) {
  return (
    <section className="insight-strip">
      {metrics.map((metric) => (
        <article className={`metric-chip ${metric.tone}`} key={`${metric.label}-${metric.value}`}>
          <span>{metric.label}</span>
          <strong>{metric.value}</strong>
        </article>
      ))}
    </section>
  );
}

function EvidencePanel({ evidence, dataNotes }: { evidence: ProductEvidenceView[]; dataNotes: string[] }) {
  return (
    <div className="intel-stack">
      <PanelTitle icon={<DatabaseZap size={18} />} title="证据包" subtitle={`${evidence.length} 条可追溯资料`} />
      {dataNotes.length ? (
        <div className="note-box">
          {dataNotes.map((note) => <span key={note}>{note}</span>)}
        </div>
      ) : null}
      {evidence.length ? (
        <div className="evidence-flow-banner">
          <LottieSignal variant="evidence" label="证据正在进入模型上下文" size="wide" />
          <div>
            <strong>证据流已接入</strong>
            <span>事实分级、预测用途和时间戳会随每条资料进入模型上下文。</span>
          </div>
        </div>
      ) : null}
      {evidence.length === 0 ? <BlankCopy text="暂无证据，建议先刷新公开数据源。" /> : evidence.map((item) => (
        <article className="intel-card" key={item.id}>
          <div className="intel-card-head">
            <span>{sourceKindLabel(item.kind_label)}</span>
            <b>{pct(item.confidence)}</b>
          </div>
          <div className="evidence-tags">
            <i>{item.fact_label || "未分级"}</i>
            <i>{predictionUsageLabel(item.prediction_usage)}</i>
          </div>
          <p>{cleanEvidenceSummary(item.summary)}</p>
          <small>{item.source_label} / {item.captured_at}</small>
        </article>
      ))}
    </div>
  );
}

function MemoryPanel({ memories, risks, review }: { memories: ProductMemoryView[]; risks: string[]; review: string }) {
  return (
    <div className="intel-stack">
      <PanelTitle icon={<Brain size={18} />} title="历史记忆" subtitle="进入下一次研报的长期上下文" />
      <div className="risk-box">
        <span><AlertTriangle size={14} /> 风险提示</span>
        {(risks.length ? risks : ["暂无显著风险提示。"]).map((risk) => <p key={risk}>{risk}</p>)}
      </div>
      {review ? (
        <div className="review-box">
          <span><Info size={14} /> 模型审查口径</span>
          <p>{review}</p>
        </div>
      ) : null}
      {memories.length === 0 ? <BlankCopy text="暂无相关记忆，比赛复盘后会逐步沉淀。" /> : memories.map((item) => (
        <article className="intel-card memory" key={item.id}>
          <div className="intel-card-head">
            <span>{item.scope} / {item.type}</span>
            <b>重要度 {pct(item.importance)}</b>
          </div>
          <p>{item.summary}</p>
          <small>{item.created_at}</small>
        </article>
      ))}
    </div>
  );
}

function TrustPanel({
  dataTrust,
  autoCollection,
  usage
}: {
  dataTrust: ProductDataTrustItem[];
  autoCollection?: ProductAutoCollectionStatus;
  usage?: LlmUsageSummary;
}) {
  return (
    <div className="intel-stack">
      <PanelTitle icon={<ShieldCheck size={18} />} title="数据可信度" subtitle="公开来源质量与模型成本" />
      <AutoCollectionBanner autoCollection={autoCollection} />
      <div className="usage-card">
        <span>大模型调用</span>
        <strong>{usage?.calls ?? 0} 次</strong>
        <small>预估成本 ${usage?.estimated_cost_usd?.toFixed(4) ?? "0.0000"} / 失败 {usage?.failed_calls ?? 0} 次</small>
      </div>
      {dataTrust.map((item) => (
        <article className="trust-card" key={item.source_name}>
          <div>
            <strong>{item.source_name}</strong>
            <span>{item.provider}</span>
          </div>
          <GaugeRing value={item.reliability_score} />
          <small>{item.authority_label} / 稳定性 {item.stability_label} / {item.snapshot_count} 条 / {item.freshness_label}</small>
        </article>
      ))}
    </div>
  );
}

function ModelPanel({
  health,
  loading,
  autoCollection
}: {
  health: ProductModelHealthResult | null;
  loading: boolean;
  autoCollection?: ProductAutoCollectionStatus;
}) {
  const evaluation = health?.strategy_evaluation;
  const backtest = health?.backtest ?? null;
  const weakDraw = backtest ? backtest.draw_samples > 0 && backtest.draw_recall < 0.12 : false;
  return (
    <div className="intel-stack">
      <PanelTitle icon={<BarChart3 size={18} />} title="模型体检" subtitle="赛后复盘、历史回测与风险短板" />
      <div className="model-refresh read-only">
        <Info size={15} />
        回测与深度模型任务由后台计划执行，公网界面只读取缓存结果。
      </div>
      <AutoCollectionBanner autoCollection={autoCollection} />
      {loading && !health ? <BlankCopy text="正在读取模型体检缓存。" /> : null}
      {!loading && !health ? <BlankCopy text="当前没有模型体检结果；后台自动采集会定期生成，避免访客触发重型任务。" /> : null}
      {health ? (
        <>
          <div className="live-evaluation-card">
            <div>
              <span>本届实战复盘</span>
              <strong>
                {evaluation?.reviewed_matches
                  ? `${evaluation.hit_count}/${evaluation.reviewed_matches} 命中`
                  : "等待赛后样本"}
              </strong>
              <small>
                {evaluation?.reviewed_matches
                  ? `命中率 ${pct(evaluation.hit_rate)} / Brier ${evaluation.average_brier_score.toFixed(3)}`
                  : "已结束比赛会自动进入赛后复盘和模型评估。"}
              </small>
            </div>
            <GaugeRing value={evaluation?.reviewed_matches ? evaluation.hit_rate : 0} />
          </div>
          {evaluation?.items?.length ? (
            <div className="evaluation-list">
              <strong>赛后复盘样本</strong>
              {evaluation.items.slice(0, 4).map((item) => (
                <article className={item.hit ? "hit" : "miss"} key={item.match_id}>
                  <span>{item.home_team} {item.score} {item.away_team}</span>
                  <b>{item.hit ? "命中" : "偏差"} / 实际 {outcomeLabel(item.actual_outcome)} / 预测 {outcomeLabel(item.predicted_outcome)}</b>
                  <small>Brier {item.brier_score.toFixed(3)} / {item.reviewed_at}</small>
                </article>
              ))}
            </div>
          ) : null}
        </>
      ) : null}
      {backtest ? (
        <>
          <div className="model-health-grid">
            <MetricTile label="策略版本" value={backtest.strategy_version} />
            <MetricTile label="回测样本" value={`${backtest.samples_used} 场`} />
            <MetricTile label="Top1 命中" value={pct(backtest.top1_hit_rate)} tone={backtest.top1_hit_rate >= 0.5 ? "good" : "warning"} />
            <MetricTile label="Brier" value={backtest.average_brier_score.toFixed(3)} tone={backtest.average_brier_score <= 0.6 ? "good" : "warning"} />
            <MetricTile label="Log Loss" value={backtest.average_log_loss.toFixed(3)} />
            <MetricTile label="平局召回" value={pct(backtest.draw_recall)} tone={weakDraw ? "danger" : "good"} />
          </div>
          <div className={`model-warning ${weakDraw ? "danger" : "neutral"}`}>
            <span>{weakDraw ? "主要短板" : "当前判断"}</span>
            <p>{weakDraw ? "模型仍低估平局，更适合用平局概率做防平/双选，而不是直接把平局作为 Top1。" : "当前模型体检未发现单一突出短板，仍需赛前伤停和首发复核。"}</p>
          </div>
          <div className="cache-note">
            <Info size={14} />
            <span>
              历史回测缓存：{health?.backtest_cached_at || "时间待记录"}
              {health?.backtest_cache_age_minutes != null ? ` / 约 ${health.backtest_cache_age_minutes} 分钟前生成` : ""}
            </span>
          </div>
          <div className="bucket-list">
            <strong>概率分桶</strong>
            {backtest.buckets.map((bucket) => (
              <div className="bucket-row" key={bucket.label}>
                <span>{bucket.label}</span>
                <i><b style={{ width: `${Math.max(4, bucket.hit_rate * 100)}%` }} /></i>
                <em>{bucket.samples} 场 / 命中 {pct(bucket.hit_rate)}</em>
              </div>
            ))}
          </div>
          <div className="model-notes">
            {backtest.notes.slice(0, 3).map((note) => <p key={note}>{note}</p>)}
          </div>
          <div className="model-samples">
            <strong>近期高误差样本</strong>
            {backtest.sample_items.slice(0, 5).map((item) => (
              <article key={`${item.date}-${item.home_team}-${item.away_team}`}>
                <span>{item.date} / {item.tournament}</span>
                <b>{item.home_team} {item.score} {item.away_team}</b>
                <small>实际 {outcomeLabel(item.actual_outcome)} / 预测 {outcomeLabel(item.predicted_outcome)} / Brier {item.brier_score.toFixed(3)}</small>
              </article>
            ))}
          </div>
        </>
      ) : null}
      {health?.notes?.length ? (
        <div className="model-notes">
          {health.notes.slice(0, 3).map((note) => <p key={note}>{note}</p>)}
        </div>
      ) : null}
    </div>
  );
}

function MetricTile({ label, value, tone = "neutral" }: { label: string; value: string; tone?: string }) {
  return (
    <div className={`model-metric ${tone}`}>
      <span>{label}</span>
      <strong>{value}</strong>
    </div>
  );
}

function AuditPanel({ audit, activity }: { audit: ProductAuditResult | null; activity: ProductActivityView[] }) {
  const events = audit?.events?.length ? audit.events : activity;
  return (
    <div className="intel-stack">
      <PanelTitle icon={<History size={18} />} title="过程日志" subtitle="采集、分拣、算法与模型动作" />
      {events.length === 0 ? <BlankCopy text="暂无过程日志。" /> : events.slice(0, 18).map((item) => (
        <article className="timeline-item" key={item.id}>
          <i />
          <div>
            <strong>{activityTitle(item.title)}</strong>
            <p>{activityMessage(item.message)}</p>
            <span>{item.time}</span>
          </div>
        </article>
      ))}
    </div>
  );
}

function outcomeLabel(value: string) {
  if (value === "home_win") return "主胜";
  if (value === "away_win") return "客胜";
  if (value === "draw") return "平局";
  return value;
}

function PanelTitle({ icon, title, subtitle }: { icon: ReactNode; title: string; subtitle: string }) {
  return (
    <div className="panel-title">
      {icon}
      <div>
        <strong>{title}</strong>
        <span>{subtitle}</span>
      </div>
    </div>
  );
}

function GaugeRing({ value }: { value: number }) {
  const degree = Math.round(Math.max(0, Math.min(1, value)) * 360);
  return (
    <div className="gauge-ring" style={{ background: `conic-gradient(#e8c16d ${degree}deg, rgba(255,255,255,.1) 0deg)` }}>
      <span>{Math.round(value * 100)}</span>
    </div>
  );
}

function BlankCopy({ text }: { text: string }) {
  return <div className="blank-copy">{text}</div>;
}

function EmptyState({ loading }: { loading: boolean }) {
  return (
    <section className="empty-state">
      {loading ? <Loader2 className="spin" size={30} /> : <Sparkles size={30} />}
      <strong>{loading ? "正在装载战术桌" : "还没有可展示的比赛"}</strong>
      <span>系统会读取赛程、预测、证据和记忆后生成主界面。</span>
    </section>
  );
}

function cleanEvidenceSummary(value: string) {
  return value
    .replace("Fixture update signal from", "赛程更新信号来自")
    .replace("News intelligence signal from", "新闻情报信号来自")
    .replace("Data snapshot imported", "数据快照已导入")
    .replace("espn_scoreboard/market_signal imported.", "ESPN 公开记分牌/市场赔率已导入。")
    .replace("espn_scoreboard/fixture_status imported.", "ESPN 公开记分牌/赛程比分已导入。")
    .slice(0, 220);
}

function sourceKindLabel(value: string) {
  return value
    .replace("team_match_stats", "技术统计")
    .replace("team_recent_form", "近期战绩")
    .replace("team_ranking", "FIFA 排名")
    .replace("team_elo", "Elo 战力")
    .replace("fixture_status", "赛程比分")
    .replace("fixture_update", "赛程更新")
    .replace("fixture_crosscheck", "赛程交叉校验")
    .replace("team_profile", "球队资料")
    .replace("fixture_intel", "赛程情报")
    .replace("match_summary", "比赛摘要")
    .replace("lineup_fact", "阵容首发")
    .replace("news_intel", "新闻线索")
    .replace("news_update", "新闻信号")
    .replace("market_signal", "市场赔率");
}

function predictionUsageLabel(value: string) {
  if (value === "structured_prediction_input") return "可进模型";
  if (value === "requires_review_before_prediction_input") return "复核后进模型";
  if (value === "fixture_context") return "赛程上下文";
  if (value === "market_calibration") return "市场校准";
  if (value === "context_only") return "仅作上下文";
  return value || "用途待定";
}

function formatMoneyline(value: number) {
  return value > 0 ? `+${value}` : String(value);
}

function formatSignedPct(value: number) {
  const sign = value > 0 ? "+" : "";
  return `${sign}${pct(value)}`;
}

function formatElapsed(seconds: number) {
  if (!Number.isFinite(seconds) || seconds <= 0) return "0 秒";
  if (seconds < 60) return `${Math.round(seconds)} 秒`;
  const minutes = Math.floor(seconds / 60);
  const rest = Math.round(seconds % 60);
  return rest ? `${minutes} 分 ${rest} 秒` : `${minutes} 分钟`;
}

function activityTitle(value: string) {
  return value
    .replace("Baseline prediction refreshed", "基线预测已刷新")
    .replace("LLM report generated", "模型研报已生成");
}

function activityMessage(value: string) {
  return value
    .replace("draw", "平局")
    .replace("Mexico", "墨西哥")
    .replace("South Africa", "南非");
}

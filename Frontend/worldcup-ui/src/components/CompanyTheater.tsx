import { useEffect, useState } from "react";
import {
  BarChart3,
  Brain,
  ChevronDown,
  Gauge,
  FileText,
  ShieldAlert,
  Sparkles,
  Trophy,
  UserRoundCheck
} from "lucide-react";
import type {
  Assignment,
  Artifact,
  ArtifactContent,
  BaselinePrediction,
  CompanyState,
  DataSnapshot,
  Employee,
  IntelligenceSignal,
  IntelligenceContentQualityResult,
  LlmCall,
  LlmUsageSummary,
  Match,
  SystemEventLog,
  TeamWorkbenchResult,
  WatchObject,
  WorkflowRun,
  WorkflowStep
} from "../types";
import {
  loadArtifactContent,
  loadIntelligenceContentQuality,
  loadTeamWorkbench,
  loadWorkflowSteps,
  runTeamIntelligenceLlmReport
} from "../api/worldcupApi";
import { pct } from "../utils/format";
import { teamDisplayName, teamShortName } from "../utils/teamNames";

type CompanyTheaterProps = {
  state: CompanyState | null;
  selectedMatch: Match | null;
  selectedObjectId: string;
  onSelectObject: (objectId: string) => void;
  onSelectMatch: (matchId: string) => void;
};

type CompanyMaps = {
  teamsById: Map<string, WatchObject>;
  employeesById: Map<string, Employee>;
  assignmentByObject: Map<string, Assignment>;
};

type ReportTab = "employee" | "prediction" | "signals" | "artifacts" | "model";
type WorkflowPhase = {
  key: string;
  title: string;
  owner: string;
  body: string;
  log: SystemEventLog | null;
  step: WorkflowStep | null;
  done: boolean;
};

type AuditRefs = {
  artifacts: CompanyState["artifacts"];
  signals: CompanyState["signals"];
  llmUsage: LlmUsageSummary | null;
};

const avatarBySymbol: Record<string, string> = {
  ARG: "/worldcup-company-assets/v2/avatars/team_arg.png",
  BRA: "/worldcup-company-assets/v2/avatars/team_bra.png",
  FRA: "/worldcup-company-assets/v2/avatars/team_fra.png",
  GER: "/worldcup-company-assets/v2/avatars/team_ger.png",
  JPN: "/worldcup-company-assets/v2/avatars/team_jpn.png",
  KOR: "/worldcup-company-assets/v2/avatars/team_kor.png",
  MAR: "/worldcup-company-assets/v2/avatars/team_mar.png",
  USA: "/worldcup-company-assets/v2/avatars/team_usa.png"
};

export function CompanyTheater({
  state,
  selectedMatch,
  selectedObjectId,
  onSelectObject,
  onSelectMatch
}: CompanyTheaterProps) {
  if (!state) {
    return (
      <section className="theater-loading">
        <div className="loader-orb"><Sparkles size={28} /></div>
        <strong>正在装载 AI 公司</strong>
        <span>连接球队、员工、比赛与日志数据。</span>
      </section>
    );
  }

  const maps = createMaps(state);
  const selectedTeam = maps.teamsById.get(selectedObjectId)
    ?? (selectedMatch ? maps.teamsById.get(selectedMatch.home_object_id) : undefined)
    ?? state.teams[0];
  const prediction = selectedMatch
    ? state.predictions.find((item) => item.match_id === selectedMatch.id) ?? null
    : null;

  return (
    <section className="theater-grid">
      <TeamWall
        teams={state.teams.slice(0, 48)}
        maps={maps}
        selectedObjectId={selectedTeam?.id ?? ""}
        onSelectObject={onSelectObject}
      />
      <main className="stage-panel">
        <StageHeader state={state} selectedMatch={selectedMatch} maps={maps} onSelectMatch={onSelectMatch} />
        <CollaborationStage
          state={state}
          selectedMatch={selectedMatch}
          selectedObjectId={selectedTeam?.id ?? ""}
          maps={maps}
          onSelectObject={onSelectObject}
        />
        <WorkflowStrip state={state} selectedMatch={selectedMatch} maps={maps} selectedObjectId={selectedTeam?.id ?? ""} />
      </main>
      <ReportPanel
        state={state}
        maps={maps}
        selectedTeam={selectedTeam}
        selectedMatch={selectedMatch}
        prediction={prediction}
      />
      <AuditCornerPanel state={state} selectedMatch={selectedMatch} />
    </section>
  );
}

function AuditCornerPanel({ state, selectedMatch }: { state: CompanyState; selectedMatch: Match | null }) {
  const [open, setOpen] = useState(false);
  const workflows = selectedMatch
    ? state.workflows.filter((workflow) => workflow.match_id === selectedMatch.id)
    : state.workflows;
  const workflow = workflows[0] ?? null;
  const steps = workflow ? state.workflowSteps.filter((step) => step.workflow_run_id === workflow.id) : [];
  const completedSteps = steps.filter((step) => step.status === "completed").length;
  const usage = state.llmUsage;

  return (
    <aside className={`audit-corner ${open ? "open" : ""}`}>
      <button
        className="audit-corner-trigger"
        data-testid="audit-corner-trigger"
        onClick={() => setOpen((current) => !current)}
        type="button"
      >
        <Gauge size={16} />
        <span>审计</span>
        <ChevronDown size={15} />
      </button>
      {open ? (
        <section className="audit-popover">
          <div>
            <span>成本与可信度</span>
            <strong>{workflow ? workflowTypeLabel(workflow.workflow_type) : "暂无流程"}</strong>
          </div>
          <dl>
            <div><dt>步骤</dt><dd>{completedSteps}/{steps.length || 5}</dd></div>
            <div><dt>调用</dt><dd>{usage?.calls ?? 0}</dd></div>
            <div><dt>Token</dt><dd>{formatNumber((usage?.prompt_tokens ?? 0) + (usage?.completion_tokens ?? 0))}</dd></div>
            <div><dt>成本</dt><dd>{formatUsd(usage?.estimated_cost_usd ?? 0)}</dd></div>
          </dl>
          {state.dataReadiness ? (
            <dl>
              <div><dt>可预测</dt><dd>{state.dataReadiness.eligible_matches}/{state.dataReadiness.total_matches}</dd></div>
              <div><dt>已拦截</dt><dd>{state.dataReadiness.blocked_matches}</dd></div>
              <div><dt>源健康</dt><dd>{state.dataReadiness.source_health_passed ? "通过" : "复核"}</dd></div>
              <div><dt>演示数据</dt><dd>{state.dataReadiness.demo_or_harness_matches}</dd></div>
            </dl>
          ) : null}
          <p>{auditCornerSummary(workflow, usage)}</p>
          {state.dataReadiness?.notes[0] ? <p>{state.dataReadiness.notes[0]}</p> : null}
        </section>
      ) : null}
    </aside>
  );
}

function TeamWall({
  teams,
  maps,
  selectedObjectId,
  onSelectObject
}: {
  teams: WatchObject[];
  maps: CompanyMaps;
  selectedObjectId: string;
  onSelectObject: (objectId: string) => void;
}) {
  return (
    <aside className="team-wall">
      <div className="panel-title">
        <UserRoundCheck size={18} />
        <div>
          <strong>球队员工墙</strong>
          <span>一支球队，一名长期研究员</span>
        </div>
      </div>
      <div className="team-grid">
        {teams.map((team) => {
          const employee = employeeForTeam(team, maps);
          const selected = selectedObjectId === team.id;
          return (
            <button
              className={`team-chip ${selected ? "selected" : ""} ${team.status === "eliminated" ? "offboarded" : ""}`}
              key={team.id}
              onClick={() => onSelectObject(team.id)}
              title={employeeNameForTeam(employee, team)}
              type="button"
            >
              <TeamAvatar team={team} size="sm" />
              <span>{teamShortName(team)}</span>
              <i>{employee ? statusLabel(team.status) : "未分配"}</i>
            </button>
          );
        })}
      </div>
    </aside>
  );
}

function getStageLabel(stage: string): { label: string; description: string } {
  switch (stage) {
    case "pre_tournament":
      return { label: "赛前准备阶段", description: "正在收集球队数据和情报" };
    case "group_stage":
      return { label: "小组赛阶段", description: "各小组比赛进行中" };
    case "knockout_stage":
      return { label: "淘汰赛阶段", description: "决战时刻，一决胜负" };
    case "post_review":
      return { label: "赛后回顾阶段", description: "总结经验，分析得失" };
    default:
      return { label: "未知阶段", description: "状态未知" };
  }
}

function StageHeader({
  state,
  selectedMatch,
  maps,
  onSelectMatch
}: {
  state: CompanyState;
  selectedMatch: Match | null;
  maps: CompanyMaps;
  onSelectMatch: (matchId: string) => void;
}) {
  const stageInfo = state.dashboard?.company.stage 
    ? getStageLabel(state.dashboard.company.stage) 
    : null;
  const seedDataNote = "队名、赛程与球队基本信息来自种子数据，预测与情报来自 AI 分析。";

  return (
    <header className="stage-header">
      <div className="stage-info">
        {stageInfo && (
          <>
            <span className="stage-badge">{stageInfo.label}</span>
            <p className="stage-description">{stageInfo.description}</p>
          </>
        )}
      </div>
      <div className="match-selector-enhanced">
        <div className="source-row">
          <span className="data-source-indicator">种子数据</span>
          <span className="data-source-indicator live">AI 预测</span>
        </div>
        <select value={selectedMatch?.id ?? ""} onChange={(event) => onSelectMatch(event.target.value)} aria-label="选择比赛">
          {state.matches.map((match) => {
            const home = maps.teamsById.get(match.home_object_id);
            const away = maps.teamsById.get(match.away_object_id);
            return (
              <option key={match.id} value={match.id}>
                {teamDisplayName(home, match.home_object_id)} 对阵 {teamDisplayName(away, match.away_object_id)}
              </option>
            );
          })}
        </select>
        <p className="data-hint">{seedDataNote}</p>
      </div>
    </header>
  );
}

function CollaborationStage({
  state,
  selectedMatch,
  selectedObjectId,
  maps,
  onSelectObject
}: {
  state: CompanyState;
  selectedMatch: Match | null;
  selectedObjectId: string;
  maps: CompanyMaps;
  onSelectObject: (objectId: string) => void;
}) {
  const home = selectedMatch ? maps.teamsById.get(selectedMatch.home_object_id) : undefined;
  const away = selectedMatch ? maps.teamsById.get(selectedMatch.away_object_id) : undefined;

  return (
    <section className="collab-stage collab-stage-enhanced">
      <div className="stage-soft-glow" />
      <EmployeeActor
        label="主队研究员"
        team={home}
        employee={employeeForTeam(home, maps)}
        align="left"
        active={home?.id === selectedObjectId}
        onClick={() => home && onSelectObject(home.id)}
      />
      <div className="stage-center">
        <SpecialistDock employees={state.employees} />
        <div className="stage-center-hint">
          <span>{selectedMatch ? "协作进行中" : "等待选择比赛"}</span>
          <p>研究员采集情报 → 数据官校准 → 风险官审查 → CEO 汇总</p>
        </div>
      </div>
      <EmployeeActor
        label="客队研究员"
        team={away}
        employee={employeeForTeam(away, maps)}
        align="right"
        active={away?.id === selectedObjectId}
        onClick={() => away && onSelectObject(away.id)}
      />
    </section>
  );
}

function EmployeeActor({
  label,
  team,
  employee,
  align,
  active,
  onClick
}: {
  label: string;
  team?: WatchObject;
  employee?: Employee;
  align: "left" | "right";
  active: boolean;
  onClick: () => void;
}) {
  return (
    <button className={`employee-actor ${align} ${active ? "active" : ""}`} onClick={onClick} disabled={!team} type="button">
      <span>{label}</span>
      <TeamAvatar team={team} size="lg" />
      <strong>{teamDisplayName(team)}</strong>
      <em>{employeeNameForTeam(employee, team)}</em>
      <p>{compactText(employeeSpecialty(employee), 56)}</p>
    </button>
  );
}

function SpecialistDock({ employees }: { employees: Employee[] }) {
  const specialists = [
    { role: "数据", title: "数据官", icon: <BarChart3 size={18} />, image: "/worldcup-company-assets/v2/avatars/exec_data.png" },
    { role: "风险", title: "风险官", icon: <ShieldAlert size={18} />, image: "/worldcup-company-assets/v2/avatars/exec_risk.png" },
    { role: "CEO", title: "CEO", icon: <Brain size={18} />, image: "/worldcup-company-assets/v2/avatars/exec_ceo.png" }
  ];

  return (
    <div className="specialist-dock">
      {specialists.map((item) => {
        const employee = employees.find((candidate) => candidate.name.includes(item.role) || candidate.role.includes(item.role));
        return (
          <article key={item.title}>
            <img src={item.image} alt="" />
            <div>
              <span>{item.icon}{item.title}</span>
              <strong>{specialistName(employee, item.title)}</strong>
            </div>
          </article>
        );
      })}
    </div>
  );
}

function WorkflowStrip({
  state,
  selectedMatch,
  selectedObjectId,
  maps
}: {
  state: CompanyState;
  selectedMatch: Match | null;
  selectedObjectId: string;
  maps: CompanyMaps;
}) {
  const [selectedPhaseKey, setSelectedPhaseKey] = useState("data");
  const [localSteps, setLocalSteps] = useState<WorkflowStep[]>([]);
  const [selectedWorkflowId, setSelectedWorkflowId] = useState("");
  const home = selectedMatch ? maps.teamsById.get(selectedMatch.home_object_id) : undefined;
  const away = selectedMatch ? maps.teamsById.get(selectedMatch.away_object_id) : undefined;
  const workflowOptions = selectWorkflowOptions(state.workflows, selectedMatch?.id ?? null, selectedObjectId);
  const workflowOptionIds = workflowOptions.map((workflow) => workflow.id).join("|");
  const activeWorkflow = workflowOptions.find((workflow) => workflow.id === selectedWorkflowId) ?? workflowOptions[0] ?? null;
  const logs = activeWorkflow
    ? state.logs.filter((log) =>
      log.workflow_run_id === activeWorkflow.id
      || (activeWorkflow.match_id && log.match_id === activeWorkflow.match_id)
      || (activeWorkflow.object_id && log.object_id === activeWorkflow.object_id))
    : state.logs;
  const cachedSteps = activeWorkflow
    ? state.workflowSteps.filter((step) => step.workflow_run_id === activeWorkflow.id)
    : [];
  const activeSteps = cachedSteps.length > 0 ? cachedSteps : localSteps.filter((step) => step.workflow_run_id === activeWorkflow?.id);
  const workflowFinished = activeWorkflow?.status === "completed" || logs.length > 0;
  const auditRefs: AuditRefs = {
    artifacts: state.artifacts.filter((artifact) =>
      artifact.workflow_run_id === activeWorkflow?.id
      || artifact.object_id === selectedObjectId
      || artifact.object_id === selectedMatch?.home_object_id
      || artifact.object_id === selectedMatch?.away_object_id
    ).slice(0, 4),
    signals: state.signals.filter((signal) =>
      signal.match_id === selectedMatch?.id
      || signal.object_id === selectedObjectId
      || signal.object_id === selectedMatch?.home_object_id
      || signal.object_id === selectedMatch?.away_object_id
    ).slice(0, 4),
    llmUsage: state.llmUsage
  };
  const steps = makeWorkflowPhasesFor(activeWorkflow, home, away, logs, activeSteps, workflowFinished, maps);
  const selectedPhase = steps.find((step) => step.key === selectedPhaseKey) ?? steps[0];

  useEffect(() => {
    let cancelled = false;
    if (!activeWorkflow?.id || cachedSteps.length > 0) {
      setLocalSteps([]);
      return;
    }
    loadWorkflowSteps(activeWorkflow.id)
      .then((items) => {
        if (!cancelled) setLocalSteps(items);
      })
      .catch(() => {
        if (!cancelled) setLocalSteps([]);
      });
    return () => {
      cancelled = true;
    };
  }, [activeWorkflow?.id, cachedSteps.length]);

  useEffect(() => {
    if (!activeWorkflow?.id) return;
    setSelectedWorkflowId((current) => workflowOptions.some((workflow) => workflow.id === current) ? current : activeWorkflow.id);
  }, [activeWorkflow?.id, workflowOptionIds]);

  useEffect(() => {
    if (!steps.some((step) => step.key === selectedPhaseKey)) {
      setSelectedPhaseKey(steps[0]?.key ?? "data");
    }
  }, [selectedPhaseKey, steps]);

  return (
    <section className="workflow-strip">
      <div className="workflow-overview">
        <span>流程复盘</span>
        <strong>{workflowHeadline(activeWorkflow)}</strong>
        <em>{workflowSummaryText(activeWorkflow, logs.length)}</em>
        {workflowOptions.length > 1 ? (
          <select
            value={activeWorkflow?.id ?? ""}
            onChange={(event) => {
              setSelectedWorkflowId(event.target.value);
              setSelectedPhaseKey("data");
            }}
            aria-label="选择审计流程"
          >
            {workflowOptions.slice(0, 12).map((workflow) => (
              <option key={workflow.id} value={workflow.id}>
                {workflowOptionLabel(workflow, maps)}
              </option>
            ))}
          </select>
        ) : null}
      </div>
      <div className="workflow-phases">
        {steps.map((step, index) => (
          <button
            className={`${step.done ? "done" : ""} ${selectedPhase.key === step.key ? "selected" : ""}`}
            key={step.key}
            onClick={() => setSelectedPhaseKey(step.key)}
            data-testid={`workflow-phase-${step.key}`}
            type="button"
          >
            <i>{index + 1}</i>
            <div>
              <span>{step.owner}</span>
              <strong>{step.title}</strong>
              <p>{workflowText(step.log?.title || step.log?.message || step.body)}</p>
              <WorkflowPhaseBadges phase={step} />
            </div>
          </button>
        ))}
      </div>
      <WorkflowPhaseDetail phase={selectedPhase} workflow={activeWorkflow} refs={auditRefs} />
    </section>
  );
}

function WorkflowPhaseBadges({ phase }: { phase: WorkflowPhase }) {
  const badges = phaseBadges(phase);
  if (badges.length === 0) return null;
  return (
    <div className="phase-badges">
      {badges.map((badge) => <em key={badge}>{badge}</em>)}
    </div>
  );
}

function ReportPanel({
  state,
  maps,
  selectedTeam,
  selectedMatch,
  prediction
}: {
  state: CompanyState;
  maps: CompanyMaps;
  selectedTeam?: WatchObject;
  selectedMatch: Match | null;
  prediction: BaselinePrediction | null;
}) {
  const [activeTab, setActiveTab] = useState<ReportTab>("employee");
  const [workbench, setWorkbench] = useState<TeamWorkbenchResult | null>(null);
  const [workbenchStatus, setWorkbenchStatus] = useState<"idle" | "loading" | "ready" | "error">("idle");
  const [workbenchMessage, setWorkbenchMessage] = useState("");
  const selectedEmployee = employeeForTeam(selectedTeam, maps);
  const activeWorkbench = workbench?.object_id === selectedTeam?.id ? workbench : null;
  const signals = activeWorkbench?.signals.slice(0, 4)
    ?? (selectedTeam ? state.signals.filter((signal) => signal.object_id === selectedTeam.id).slice(0, 4) : []);
  const artifacts = activeWorkbench?.artifacts.slice(0, 3)
    ?? (selectedTeam ? state.artifacts.filter((artifact) => artifact.object_id === selectedTeam.id).slice(0, 3) : []);
  const relatedLogs = selectedTeam
    ? (activeWorkbench?.logs.slice(0, 3)
      ?? state.logs.filter((log) => log.object_id === selectedTeam.id || log.match_id === selectedMatch?.id).slice(0, 3))
    : state.logs.slice(0, 3);
  const employeeModelCalls = selectedEmployee
    ? state.llmUsage?.recent_calls.filter((call) => call.employee_id === selectedEmployee.id) ?? []
    : [];
  const tabs: Array<{ id: ReportTab; label: string; count?: number }> = [
    { id: "employee", label: "工作台" },
    { id: "prediction", label: "预测" },
    { id: "signals", label: "情报", count: signals.length },
    { id: "artifacts", label: "产物", count: artifacts.length },
    { id: "model", label: "模型", count: employeeModelCalls.length }
  ];

  useEffect(() => {
    let cancelled = false;
    if (!selectedTeam?.id) {
      setWorkbench(null);
      setWorkbenchStatus("idle");
      return;
    }
    setWorkbenchStatus("loading");
    setWorkbenchMessage("");
    loadTeamWorkbench(selectedTeam.id, 12)
      .then((result) => {
        if (!cancelled) {
          setWorkbench(result);
          setWorkbenchStatus("ready");
        }
      })
      .catch(() => {
        if (!cancelled) {
          setWorkbench(null);
          setWorkbenchStatus("error");
          setWorkbenchMessage("员工工作台读取失败");
        }
      });
    return () => {
      cancelled = true;
    };
  }, [selectedTeam?.id]);

  return (
    <aside className="report-panel">
      <div className="panel-title">
        <FileText size={18} />
        <div>
          <strong>员工报告台</strong>
          <span>真实数据、预测与产物</span>
        </div>
      </div>

      <div className="report-tabs" role="tablist" aria-label="报告内容">
        {tabs.map((tab) => (
          <button
            key={tab.id}
            className={activeTab === tab.id ? "active" : ""}
            onClick={() => setActiveTab(tab.id)}
            type="button"
          >
            {tab.label}
            {typeof tab.count === "number" ? <i>{tab.count}</i> : null}
          </button>
        ))}
      </div>

      <section className="report-brief">
        <strong>{teamDisplayName(selectedTeam)}协作摘要</strong>
        <p>{reportBriefText(selectedTeam, selectedMatch, prediction, signals.length, artifacts.length, maps)}</p>
        <div>
          <span>最近动作</span>
          <em>{workflowText(relatedLogs[0]?.title || relatedLogs[0]?.message || "等待员工提交新记录")}</em>
        </div>
      </section>

      {activeTab === "employee" ? (
        <section className="focus-card report-section">
          <TeamAvatar team={selectedTeam} size="md" />
          <div>
            <span>当前员工</span>
            <strong>{employeeNameForTeam(selectedEmployee, selectedTeam)}</strong>
            <p>{compactText(employeeSpecialty(selectedEmployee), 82)}</p>
          </div>
          <dl className="report-metrics">
            <div><dt>球队状态</dt><dd>{statusLabel(selectedTeam?.status)}</dd></div>
            <div><dt>情报</dt><dd>{signals.length}</dd></div>
            <div><dt>产物</dt><dd>{artifacts.length}</dd></div>
          </dl>
          <TeamWorkbenchCard
            objectId={selectedTeam?.id ?? ""}
            workbench={activeWorkbench}
            status={workbenchStatus}
            message={workbenchMessage}
            onWorkbenchLoaded={(nextWorkbench, nextMessage) => {
              setWorkbench(nextWorkbench);
              setWorkbenchStatus("ready");
              setWorkbenchMessage(nextMessage);
            }}
            onWorkbenchError={(nextMessage) => setWorkbenchMessage(nextMessage)}
          />
          <EvidenceTrail logs={relatedLogs} />
        </section>
      ) : null}

      {activeTab === "prediction" ? (
        prediction ? (
          <section className="prob-card report-section">
            <strong>Baseline 胜率</strong>
            <ProbabilityRow label="主胜" value={prediction.home_win_probability} />
            <ProbabilityRow label="平局" value={prediction.draw_probability} />
            <ProbabilityRow label="客胜" value={prediction.away_win_probability} />
            <p>{predictionExplanation(prediction, selectedMatch, maps)}</p>
            <div className="decision-summary">
              <span>当前倾向</span>
              <strong>{predictionLeader(prediction, selectedMatch, maps)}</strong>
              <em>{predictionGapText(prediction)}</em>
            </div>
          </section>
        ) : (
          <section className="prob-card empty report-section">当前比赛暂无 Baseline 预测。</section>
        )
      ) : null}

      {activeTab === "signals" ? (
        <section className="signal-list report-section">
          <strong>情报信号</strong>
          {signals.length > 0 ? signals.map((signal) => (
            <SignalEvidenceCard
              key={signal.id}
              signal={signal}
              snapshot={activeWorkbench?.snapshots.find((item) => item.id === signal.source_snapshot_id) ?? null}
            />
          )) : <p className="muted-copy">该员工暂未收到新的情报信号。</p>}
        </section>
      ) : null}

      {activeTab === "artifacts" ? (
        <section className="artifact-list report-section">
          <strong>工作产物</strong>
          {artifacts.length > 0 ? artifacts.map((artifact) => (
            <ArtifactAuditCard key={artifact.id} artifact={artifact} team={selectedTeam} />
          )) : <p className="muted-copy">暂无归档产物。</p>}
        </section>
      ) : null}

      {activeTab === "model" ? (
        <ModelAuditPanel usage={state.llmUsage} employee={selectedEmployee} calls={employeeModelCalls} />
      ) : null}
    </aside>
  );
}

function TeamWorkbenchCard({
  objectId,
  workbench,
  status,
  message,
  onWorkbenchLoaded,
  onWorkbenchError
}: {
  objectId: string;
  workbench: TeamWorkbenchResult | null;
  status: "idle" | "loading" | "ready" | "error";
  message: string;
  onWorkbenchLoaded: (workbench: TeamWorkbenchResult, message: string) => void;
  onWorkbenchError: (message: string) => void;
}) {
  const [expanded, setExpanded] = useState(false);
  const [running, setRunning] = useState(false);

  if (status === "loading") {
    return <div className="team-workbench-card loading">正在读取员工专属工作台...</div>;
  }
  if (status === "error") {
    return <div className="team-workbench-card loading">员工工作台读取失败，当前先展示全局筛选数据。</div>;
  }
  if (!workbench) {
    return <div className="team-workbench-card loading">暂无员工专属工作台数据。</div>;
  }

  const budgetTokens = (workbench.llm_budget.estimated_prompt_tokens ?? 0)
    + (workbench.llm_budget.estimated_completion_tokens ?? 0);
  const budgetCost = workbench.llm_budget.estimated_cost_usd ?? 0;
  const isCurrentWorkbench = workbench.object_id === objectId;
  const pendingSignals = isCurrentWorkbench ? workbench.pending_actionable_signals : 0;
  const canRunDeepReport = pendingSignals > 0 && !running;
  const reportContent = workbench.latest_report?.content ?? "";
  const latestReport = workbench.latest_report?.content
    ? artifactContentPreview(workbench.latest_report.content)
    : artifactSummary(workbench.latest_report?.artifact.summary || "");
  const evidenceRows = [
    {
      label: "最新快照",
      value: workbench.snapshots[0]
        ? `${sourceLabel(workbench.snapshots[0].source)} · ${snapshotTypeLabel(workbench.snapshots[0].snapshot_type)}`
        : "暂无快照"
    },
    {
      label: "最新情报",
      value: workbench.signals[0] ? signalSummaryText(workbench.signals[0].summary || workbench.signals[0].title) : "暂无情报"
    },
    {
      label: "最近日志",
      value: workbench.logs[0] ? workflowText(workbench.logs[0].title || workbench.logs[0].message) : "暂无日志"
    }
  ];

  return (
    <div className="team-workbench-card">
      <div className="workbench-head">
        <div>
          <span>专属工作台</span>
          <strong>{workbench.passed ? "资料包已就绪" : "需要复核"}</strong>
        </div>
        <button
          className="workbench-run-button"
          type="button"
          disabled={!canRunDeepReport}
          onClick={async () => {
            if (!objectId) return;
            setRunning(true);
            onWorkbenchError("正在刷新员工工作台，确认是否需要调用大模型...");
            try {
              const beforeRun = await loadTeamWorkbench(objectId, 12);
              if (beforeRun.pending_actionable_signals <= 0) {
                onWorkbenchLoaded(beforeRun, "大模型可以调用，但当前没有待复核的可行动情报。请先运行自动化采集/情报分拣，或选择有待处理情报的球队。");
                return;
              }
              onWorkbenchLoaded(beforeRun, `已确认 ${beforeRun.pending_actionable_signals} 条待复核情报，正在调用大模型生成深度报告...`);
              const result = await runTeamIntelligenceLlmReport(objectId, 8);
              const refreshed = await loadTeamWorkbench(objectId, 12);
              const resultMessage = result.passed
                ? `深度报告已生成，使用 ${result.signals_used} 条情报。`
                : llmReportMessage(result.notes);
              onWorkbenchLoaded(refreshed, resultMessage);
              setExpanded(true);
            } catch (error) {
              onWorkbenchError(error instanceof Error ? error.message : "深度报告生成失败");
            } finally {
              setRunning(false);
            }
          }}
        >
          {running ? "生成中..." : "生成深度报告"}
        </button>
      </div>
      <p className="workbench-hint">
        {message || (pendingSignals > 0
          ? `预计消耗 ${formatNumber(budgetTokens)} Token，约 ${formatUsd(budgetCost)}。`
          : "当前没有待复核情报，暂不建议调用大模型。")}
      </p>
      <div className="workbench-grid">
        <div><span>待处理情报</span><strong>{formatNumber(pendingSignals)}</strong></div>
        <div><span>快照</span><strong>{formatNumber(workbench.snapshots.length)}</strong></div>
        <div><span>流程</span><strong>{formatNumber(workbench.workflows.length)}</strong></div>
        <div><span>预算 Token</span><strong>{formatNumber(budgetTokens)}</strong></div>
      </div>
      <article>
        <span>最新简报</span>
        <p>{latestReport || "该员工还没有可展示的最新简报。"}</p>
        {reportContent ? (
          <button className="text-action" type="button" onClick={() => setExpanded((current) => !current)}>
            {expanded ? "收起正文" : "展开正文"}
          </button>
        ) : null}
      </article>
      {expanded && reportContent ? (
        <section className="workbench-report-body">
          {reportContentLines(reportContent).map((line, index) => (
            <p key={`${line}-${index}`}>{line}</p>
          ))}
        </section>
      ) : null}
      <section className="workbench-evidence-chain">
        <strong>证据链</strong>
        {evidenceRows.map((row) => (
          <div key={row.label}>
            <span>{row.label}</span>
            <p>{compactText(row.value, 92)}</p>
          </div>
        ))}
      </section>
    </div>
  );
}

function SignalEvidenceCard({ signal, snapshot }: { signal: IntelligenceSignal; snapshot: DataSnapshot | null }) {
  const [open, setOpen] = useState(false);
  const evidence = parseEvidence(signal.evidence_json);
  const source = evidence.source || snapshot?.source || "来源待确认";
  const capturedAt = evidence.captured_at || snapshot?.captured_at || signal.created_at;
  const excerpt = evidence.excerpt || snapshot?.content_json || signal.summary;

  return (
    <article className={`signal-evidence-card ${open ? "open" : ""}`}>
      <button type="button" onClick={() => setOpen((current) => !current)}>
        <span>{signalTypeLabel(signal.signal_type)} · {severityLabel(signal.severity)} · {Math.round(signal.confidence * 100)}%</span>
        <strong>{signalSummaryText(signal.summary || signal.title)}</strong>
        <i>{open ? "收起" : "证据"}</i>
      </button>
      {open ? (
        <section>
          <dl>
            <div><dt>来源</dt><dd>{sourceLabel(source)}</dd></div>
            <div><dt>快照</dt><dd>{compactText(signal.source_snapshot_id || "未绑定", 28)}</dd></div>
            <div><dt>状态</dt><dd>{statusLabel(signal.status)}</dd></div>
            <div><dt>时间</dt><dd>{formatTime(capturedAt)}</dd></div>
          </dl>
          {evidence.url ? (
            <a href={evidence.url} target="_blank" rel="noreferrer">打开来源链接</a>
          ) : null}
          <p>{evidenceText(excerpt)}</p>
        </section>
      ) : null}
    </article>
  );
}

function ArtifactAuditCard({ artifact, team }: { artifact: Artifact; team?: WatchObject }) {
  const [open, setOpen] = useState(false);
  const [content, setContent] = useState<ArtifactContent | null>(null);
  const [quality, setQuality] = useState<IntelligenceContentQualityResult | null>(null);
  const [status, setStatus] = useState<"idle" | "loading" | "ready" | "error">("idle");

  useEffect(() => {
    let cancelled = false;
    if (!open || content || status === "loading") return;
    setStatus("loading");
    Promise.all([
      loadArtifactContent(artifact.id).catch(() => null),
      loadIntelligenceContentQuality(artifact.id).catch(() => null)
    ]).then(([nextContent, nextQuality]) => {
      if (cancelled) return;
      setContent(nextContent);
      setQuality(nextQuality);
      setStatus(nextContent || nextQuality ? "ready" : "error");
    });
    return () => {
      cancelled = true;
    };
  }, [artifact.id, content, open, status]);

  return (
    <article className={`artifact-audit-card ${open ? "open" : ""}`}>
      <button type="button" onClick={() => setOpen((current) => !current)}>
        <span>{artifactTitle(artifact.title, team)}</span>
        <strong>{artifactSummary(artifact.summary || artifact.file_path)}</strong>
        <i>{open ? "收起" : "审计"}</i>
      </button>
      {open ? (
        <section>
          <dl>
            <div><dt>生成时间</dt><dd>{formatTime(artifact.created_at)}</dd></div>
            <div><dt>工作流</dt><dd>{compactText(artifact.workflow_run_id || "未绑定", 24)}</dd></div>
            <div><dt>Hash</dt><dd>{compactText(artifact.content_hash || "未记录", 24)}</dd></div>
            <div><dt>路径</dt><dd>{compactText(artifact.file_path || "未记录", 24)}</dd></div>
          </dl>
          {status === "loading" ? <p>正在读取产物正文与质量结果...</p> : null}
          {status === "error" ? <p>产物审计读取失败，当前只能展示摘要。</p> : null}
          {quality ? <QualityChecklist quality={quality} /> : null}
          {content?.content ? (
            <div className="artifact-audit-body">
              {reportContentLines(content.content).map((line, index) => (
                <p key={`${artifact.id}-${index}`}>{line}</p>
              ))}
            </div>
          ) : null}
        </section>
      ) : null}
    </article>
  );
}

function ModelAuditPanel({
  usage,
  employee,
  calls
}: {
  usage: LlmUsageSummary | null;
  employee?: Employee;
  calls: LlmCall[];
}) {
  const employeeUsage = employee
    ? usage?.by_employee.find((item) => item.employee_id === employee.id)
    : undefined;
  const totalTokens = (employeeUsage?.prompt_tokens ?? 0) + (employeeUsage?.completion_tokens ?? 0);

  return (
    <section className="model-audit-panel report-section">
      <strong>模型调用审计</strong>
      <div className="model-ledger-summary">
        <div><span>员工</span><strong>{employee ? displayEmployeeName(employee, "员工") : "未选择"}</strong></div>
        <div><span>调用</span><strong>{formatNumber(employeeUsage?.calls ?? calls.length)}</strong></div>
        <div><span>Token</span><strong>{formatNumber(totalTokens)}</strong></div>
        <div><span>成本</span><strong>{formatUsd(employeeUsage?.estimated_cost_usd ?? 0)}</strong></div>
      </div>
      {calls.length > 0 ? (
        <div className="model-call-list">
          {calls.map((call) => <ModelCallCard key={call.id} call={call} />)}
        </div>
      ) : (
        <p className="muted-copy">该员工最近没有单独绑定的大模型调用记录。全局账本仍会在审计角落显示累计成本。</p>
      )}
    </section>
  );
}

function ModelCallCard({ call }: { call: LlmCall }) {
  const tokens = call.prompt_tokens + call.completion_tokens;
  return (
    <article className={`model-call-card ${call.status === "success" ? "success" : "failed"}`}>
      <div>
        <span>{modelDisplayName(call.model_name)} · {providerDisplayName(call.provider)}</span>
        <strong>{statusLabel(call.status)}</strong>
      </div>
      <dl>
        <div><dt>Prompt</dt><dd>{formatNumber(call.prompt_tokens)}</dd></div>
        <div><dt>Completion</dt><dd>{formatNumber(call.completion_tokens)}</dd></div>
        <div><dt>总 Token</dt><dd>{formatNumber(tokens)}</dd></div>
        <div><dt>成本</dt><dd>{formatUsd(call.cost_estimate)}</dd></div>
      </dl>
      <p>{call.error_message ? `错误：${call.error_message}` : `任务：${compactText(call.agent_task_id || call.prompt_version, 72)} · ${formatTime(call.created_at)}`}</p>
    </article>
  );
}

function QualityChecklist({ quality }: { quality: IntelligenceContentQualityResult }) {
  const items = [
    ["核心判断", quality.contains_core_judgment],
    ["关键证据", quality.contains_evidence],
    ["不确定性", quality.contains_uncertainty_or_risk],
    ["行动建议", quality.contains_action],
    ["信号追踪", quality.contains_signal_trace],
    ["非投注声明", quality.contains_no_betting_guardrail],
    ["禁忌表述", quality.avoids_forbidden_claims]
  ] as const;

  return (
    <div className="quality-checklist">
      <div>
        <span>质量检查</span>
        <strong>{quality.passed ? "通过" : "需复核"} · {formatNumber(quality.content_chars)} 字符</strong>
      </div>
      <ul>
        {items.map(([label, passed]) => (
          <li className={passed ? "passed" : "missing"} key={label}>
            <i>{passed ? "✓" : "!"}</i>{label}
          </li>
        ))}
      </ul>
    </div>
  );
}

function EvidenceTrail({ logs }: { logs: SystemEventLog[] }) {
  return (
    <div className="evidence-trail">
      <strong>依据链</strong>
      {logs.length > 0 ? logs.map((log) => (
        <article key={log.id}>
          <span>{categoryLabel(log.category)} · {formatTime(log.created_at)}</span>
          <p>{workflowText(log.title || log.message || log.event_type)}</p>
        </article>
      )) : <p className="muted-copy">暂无可展示的流程日志。</p>}
    </div>
  );
}

function makeWorkflowPhase(
  key: string,
  title: string,
  fallbackOwner: string,
  body: string,
  stepType: string,
  logs: SystemEventLog[],
  steps: WorkflowStep[],
  workflowFinished: boolean,
  maps: CompanyMaps
): WorkflowPhase {
  const step = steps.find((item) => item.step_type === stepType) ?? null;
  const employee = step?.assignee_employee_id ? maps.employeesById.get(step.assignee_employee_id) : undefined;
  return {
    key,
    title,
    owner: employee ? displayEmployeeName(employee, fallbackOwner) : fallbackOwner,
    body,
    log: findPhaseLog(logs, stepType),
    step,
    done: step ? step.status === "completed" : workflowFinished
  };
}

function phaseBadges(phase: WorkflowPhase) {
  const badges: string[] = [];
  if (phase.step?.input_json && phase.step.input_json !== "{}") badges.push("输入");
  if (phase.step?.output_json && phase.step.output_json !== "{}") badges.push("输出");
  if (phase.step?.artifact_id || phase.log?.artifact_id) badges.push("产物");
  if (phase.log?.llm_call_id) badges.push("模型");
  if (phase.step?.error_message) badges.push("需复核");
  return badges;
}

function selectWorkflowOptions(workflows: WorkflowRun[], matchId: string | null, objectId: string) {
  const preferred = workflows.filter((workflow) =>
    (matchId && workflow.match_id === matchId)
    || (objectId && workflow.object_id === objectId)
    || workflow.workflow_type === "auto_collection"
    || workflow.workflow_type === "intelligence_triage"
  );
  const seen = new Set<string>();
  return preferred
    .filter((workflow) => {
      if (seen.has(workflow.id)) return false;
      seen.add(workflow.id);
      return true;
    })
    .slice(0, 24);
}

function makeWorkflowPhasesFor(
  workflow: WorkflowRun | null,
  home: WatchObject | undefined,
  away: WatchObject | undefined,
  logs: SystemEventLog[],
  activeSteps: WorkflowStep[],
  workflowFinished: boolean,
  maps: CompanyMaps
) {
  if (workflow?.workflow_type === "auto_collection") {
    return [
      makeWorkflowPhase("collect", "公开采集", "采集器", "公开数据源完成采集、去重和导入。", "auto_collection", logs, activeSteps, workflowFinished, maps),
      makeWorkflowPhase("triage", "情报分拣", "情报分拣器", "从快照里识别阵容、伤病、赛程和风险信号。", "intelligence_triage", logs, activeSteps, workflowFinished, maps),
      makeWorkflowPhase("reports", "员工简报", "球队员工", "触发相关球队员工生成结构化简报。", "team_intelligence_report", logs, activeSteps, workflowFinished, maps),
      makeWorkflowPhase("quality", "质量检查", "审计员", "检查快照质量和情报队列健康。", "auto_collection", logs, activeSteps, workflowFinished, maps)
    ];
  }

  if (workflow?.workflow_type === "intelligence_triage") {
    return [
      makeWorkflowPhase("snapshots", "读取快照", "情报分拣器", "读取最新公开数据快照。", "intelligence_triage", logs, activeSteps, workflowFinished, maps),
      makeWorkflowPhase("signals", "生成信号", "情报分拣器", "识别可行动情报和重复信号。", "intelligence_triage", logs, activeSteps, workflowFinished, maps),
      makeWorkflowPhase("queue", "进入队列", "情报队列", "将待复核信号交给员工工作台。", "intelligence_triage", logs, activeSteps, workflowFinished, maps)
    ];
  }

  if (workflow?.workflow_type === "team_intelligence_report" || workflow?.workflow_type === "team_intelligence_llm_report") {
    const stepType = workflow.workflow_type;
    return [
      makeWorkflowPhase("signals", "读取情报", "球队员工", "读取该球队待复核情报信号。", stepType, logs, activeSteps, workflowFinished, maps),
      makeWorkflowPhase("report", workflow.workflow_type === "team_intelligence_llm_report" ? "深度报告" : "结构化简报", "球队员工", "生成给 CEO 阅读的员工简报。", stepType, logs, activeSteps, workflowFinished, maps),
      makeWorkflowPhase("archive", "归档产物", "系统", "归档报告正文、Hash、日志和质量检查依据。", stepType, logs, activeSteps, workflowFinished, maps)
    ];
  }

  return [
    makeWorkflowPhase("data", "数据进入", "采集器", "公开数据快照进入系统。", "data_analysis", logs, activeSteps, workflowFinished, maps),
    makeWorkflowPhase("home", `${teamShortName(home)} 情报`, "主队员工", "主队研究员形成初稿。", "team_report_home", logs, activeSteps, workflowFinished, maps),
    makeWorkflowPhase("away", `${teamShortName(away)} 情报`, "客队员工", "客队研究员补充反向观察。", "team_report_away", logs, activeSteps, workflowFinished, maps),
    makeWorkflowPhase("risk", "风险复核", "风险官", "检查冷门、伤停和模型偏差。", "risk_review", logs, activeSteps, workflowFinished, maps),
    makeWorkflowPhase("ceo", "CEO 汇总", "CEO", "合并多岗位意见并归档产物。", "ceo_summary", logs, activeSteps, workflowFinished, maps)
  ];
}

function workflowOptionLabel(workflow: WorkflowRun, maps: CompanyMaps) {
  const team = workflow.object_id ? maps.teamsById.get(workflow.object_id) : undefined;
  const subject = team ? ` · ${teamDisplayName(team)}` : workflow.match_id ? " · 比赛" : "";
  return `${workflowGroupLabel(workflow.workflow_type)}｜${workflowTypeLabel(workflow.workflow_type)}${subject} · ${formatTime(workflow.started_at)}`;
}

function workflowGroupLabel(type: string) {
  if (type === "auto_collection") return "运营";
  if (type === "intelligence_triage") return "情报";
  if (type === "team_intelligence_report" || type === "team_intelligence_llm_report") return "员工";
  if (type.includes("prediction")) return "比赛";
  return "流程";
}

function WorkflowPhaseDetail({
  phase,
  workflow,
  refs
}: {
  phase: WorkflowPhase;
  workflow: WorkflowRun | null;
  refs: AuditRefs;
}) {
  const log = phase.log;
  const artifactId = phase.step?.artifact_id || log?.artifact_id;
  const artifact = artifactId
    ? refs.artifacts.find((item) => item.id === artifactId) ?? refs.artifacts[0]
    : refs.artifacts[0];
  const signal = log?.snapshot_id
    ? refs.signals.find((item) => item.source_snapshot_id === log.snapshot_id) ?? refs.signals[0]
    : refs.signals[0];
  return (
    <aside className="workflow-detail">
      <div>
        <span>阶段详情</span>
        <strong>{phase.title}</strong>
      </div>
      <p>{phaseDetailText(phase, workflow)}</p>
      <dl>
        <div><dt>负责人</dt><dd>{phase.owner}</dd></div>
        <div><dt>状态</dt><dd>{phase.step ? statusLabel(phase.step.status) : phase.done ? (log ? "已完成" : "流程已完成") : "待运行"}</dd></div>
        <div><dt>时间</dt><dd>{formatTime(phase.step?.completed_at || log?.created_at)}</dd></div>
      </dl>
      <article>
        <span>关联记录</span>
        <p>{workflowText(log?.title || log?.message || phase.step?.error_message || phase.body)}</p>
      </article>
      <div className="audit-grid">
        <section>
          <span>输入快照</span>
          <strong>{snapshotRefText(log, signal)}</strong>
          <p>{phase.step ? jsonSummaryText(phase.step.input_json, "已记录步骤输入。") : signal ? signalSummaryText(signal.summary || signal.title) : "当前阶段没有匹配到具体快照正文，后续可接入快照详情接口展开。"}</p>
        </section>
        <section>
          <span>模型调用</span>
          <strong>{llmCallTitle(log, refs.llmUsage)}</strong>
          <p>{llmCallSummary(log, refs.llmUsage)}</p>
        </section>
        <section>
          <span>输出产物</span>
          <strong>{artifact ? artifactTitle(artifact.title, undefined) : "暂无产物"}</strong>
          <p>{phase.step ? jsonSummaryText(phase.step.output_json, artifact ? artifactSummary(artifact.summary || artifact.file_path) : "已记录步骤输出。") : artifact ? artifactSummary(artifact.summary || artifact.file_path) : "当前阶段未匹配到归档产物。"}</p>
        </section>
      </div>
      <WorkflowJsonAudit phase={phase} workflow={workflow} />
      <ArtifactPreview artifact={artifact} />
    </aside>
  );
}

function WorkflowJsonAudit({ phase, workflow }: { phase: WorkflowPhase; workflow: WorkflowRun | null }) {
  const [open, setOpen] = useState(false);
  const step = phase.step;
  const hasStructuredData = Boolean(step?.input_json && step.input_json !== "{}") || Boolean(step?.output_json && step.output_json !== "{}");

  return (
    <section className="workflow-json-audit">
      <button type="button" onClick={() => setOpen((current) => !current)}>
        <span>结构化审计</span>
        <strong>{step ? "已绑定真实步骤" : "暂无步骤记录"}</strong>
        <i>{open ? "收起" : "展开"}</i>
      </button>
      {open ? (
        <div>
          <dl>
            <div><dt>流程</dt><dd>{compactText(workflow?.id || "未绑定", 30)}</dd></div>
            <div><dt>阶段</dt><dd>{phase.title}</dd></div>
            <div><dt>步骤</dt><dd>{compactText(step?.id || "未绑定", 30)}</dd></div>
            <div><dt>状态</dt><dd>{step ? statusLabel(step.status) : "待记录"}</dd></div>
          </dl>
          {hasStructuredData ? (
            <div className="json-audit-grid">
              <JsonAuditBox title="输入 JSON" json={step?.input_json} />
              <JsonAuditBox title="输出 JSON" json={step?.output_json} />
            </div>
          ) : (
            <p>当前阶段没有可展开的结构化输入输出，可能是日志型记录或规则阶段。</p>
          )}
        </div>
      ) : null}
    </section>
  );
}

function JsonAuditBox({ title, json }: { title: string; json?: string | null }) {
  return (
    <article>
      <span>{title}</span>
      <pre>{prettyJsonText(json)}</pre>
    </article>
  );
}

function ArtifactPreview({ artifact }: { artifact?: Artifact }) {
  const [content, setContent] = useState<ArtifactContent | null>(null);
  const [status, setStatus] = useState<"idle" | "loading" | "ready" | "error">("idle");

  useEffect(() => {
    let cancelled = false;
    setContent(null);
    if (!artifact?.id) {
      setStatus("idle");
      return;
    }
    setStatus("loading");
    loadArtifactContent(artifact.id)
      .then((nextContent) => {
        if (cancelled) return;
        setContent(nextContent);
        setStatus("ready");
      })
      .catch(() => {
        if (cancelled) return;
        setStatus("error");
      });
    return () => {
      cancelled = true;
    };
  }, [artifact?.id]);

  if (!artifact) {
    return (
      <section className="artifact-preview">
        <span>产物正文</span>
        <p>当前阶段没有可展开的产物正文。</p>
      </section>
    );
  }

  return (
    <section className="artifact-preview">
      <div>
        <span>产物正文</span>
        <strong>{artifactTitle(artifact.title, undefined)}</strong>
      </div>
      {status === "loading" ? <p>正在读取产物正文...</p> : null}
      {status === "error" ? <p>产物正文读取失败，摘要仍可用于审计。</p> : null}
      {status === "ready" ? <p>{artifactContentPreview(content?.content ?? "")}</p> : null}
    </section>
  );
}

function findPhaseLog(logs: SystemEventLog[], needle: string) {
  return logs.find((log) =>
    `${log.event_type} ${log.title} ${log.message}`.toLowerCase().includes(needle.toLowerCase())
  ) ?? null;
}

function workflowHeadline(workflow: WorkflowRun | null) {
  if (!workflow) return "当前没有正在展示的流程";
  return `${workflowTypeLabel(workflow.workflow_type)} · ${statusLabel(workflow.status)}`;
}

function workflowSummaryText(workflow: WorkflowRun | null, logCount: number) {
  if (!workflow) return "选择一场比赛后，会在这里展示完整协作链。";
  return `当前匹配到 ${logCount} 条系统记录，可顺着下方步骤查看数据进入、员工分析、风险复核与 CEO 汇总。`;
}

function phaseDetailText(phase: WorkflowPhase, workflow: WorkflowRun | null) {
  const workflowName = workflow ? workflowTypeLabel(workflow.workflow_type) : "当前流程";
  if (phase.step) {
    return `${workflowName}中的「${phase.title}」已经匹配到真实步骤记录，下面展示该步骤的输入、输出、产物与模型调用引用。`;
  }
  if (phase.log) {
    return `${workflowName}中的「${phase.title}」已留下系统记录，可作为后续复盘、记忆沉淀和报告解释依据。`;
  }
  return `${workflowName}中的「${phase.title}」还没有匹配到专属日志，当前展示的是该阶段的计划职责。`;
}

function snapshotRefText(log: SystemEventLog | null, signal: CompanyState["signals"][number] | undefined) {
  const id = log?.snapshot_id || signal?.source_snapshot_id;
  if (!id) return "暂无快照引用";
  return "快照引用已记录";
}

function jsonSummaryText(raw: string, fallback: string) {
  if (!raw || raw === "{}") return fallback;
  try {
    const parsed = JSON.parse(raw) as unknown;
    const text = summarizeJsonValue(parsed);
    return text || fallback;
  } catch {
    if (/[A-Za-z]{4,}/.test(raw)) return fallback;
    return compactText(raw, 86);
  }
}

function llmCallTitle(log: SystemEventLog | null, usage: LlmUsageSummary | null) {
  const call = log?.llm_call_id ? usage?.recent_calls.find((item) => item.id === log.llm_call_id) : undefined;
  if (call) return `${modelDisplayName(call.model_name)} · ${statusLabel(call.status)}`;
  if ((usage?.calls ?? 0) > 0) return "全局模型账本已记录";
  return "暂无调用记录";
}

function llmCallSummary(log: SystemEventLog | null, usage: LlmUsageSummary | null) {
  const call = log?.llm_call_id ? usage?.recent_calls.find((item) => item.id === log.llm_call_id) : undefined;
  if (call) {
    return `本次调用消耗 ${formatNumber(call.prompt_tokens + call.completion_tokens)} Token，估算成本 ${formatUsd(call.cost_estimate)}。`;
  }
  if ((usage?.calls ?? 0) > 0) {
    return `系统累计 ${usage?.calls ?? 0} 次模型调用，消耗 ${formatNumber((usage?.prompt_tokens ?? 0) + (usage?.completion_tokens ?? 0))} Token，估算成本 ${formatUsd(usage?.estimated_cost_usd ?? 0)}。当前阶段未绑定单独调用。`;
  }
  return "该阶段可能是规则计算或结构化流程，没有直接的大模型调用记录。";
}

function auditCornerSummary(workflow: WorkflowRun | null, usage: LlmUsageSummary | null) {
  if (!workflow) return "当前没有可审计流程。";
  if ((usage?.calls ?? 0) === 0) return "当前流程以结构化规则和归档产物为主，尚未记录模型成本。";
  return `全局模型账本已记录 ${usage?.successful_calls ?? 0} 次成功调用，失败 ${usage?.failed_calls ?? 0} 次。`;
}

function modelDisplayName(modelName: string) {
  if (!modelName) return "模型";
  if (/modelIndex/i.test(modelName)) return "配置模型";
  if (/deepseek/i.test(modelName)) return "DeepSeek";
  if (/gpt/i.test(modelName)) return "GPT";
  return "模型";
}

function providerDisplayName(provider: string) {
  if (!provider) return "供应商待确认";
  if (/pipi/i.test(provider)) return "PiPiClaw 网关";
  if (/harness/i.test(provider)) return "测试账本";
  return provider;
}

function formatNumber(value: number) {
  return new Intl.NumberFormat("zh-CN").format(Math.round(value));
}

function formatUsd(value: number) {
  if (value <= 0) return "$0.00";
  if (value < 0.01) return `$${value.toFixed(6)}`;
  return `$${value.toFixed(2)}`;
}

function llmReportMessage(notes: string[] | undefined) {
  const note = notes?.find(Boolean) ?? "";
  if (/no pending/i.test(note)) {
    return "大模型可以调用，但当前没有待复核的可行动情报。请先运行自动化采集/情报分拣，或选择有待处理情报的球队。";
  }
  if (/budget/i.test(note)) {
    return "本次深度报告超过当前模型预算保护，系统已阻止调用以避免 Token 消耗失控。";
  }
  return note || "当前没有可用于深度报告的待复核情报。";
}

function summarizeJsonValue(value: unknown): string {
  if (value == null) return "";
  if (typeof value === "string") {
    if (/[A-Za-z]{4,}/.test(value)) return "";
    return compactText(value, 86);
  }
  if (typeof value === "number" || typeof value === "boolean") return String(value);
  if (Array.isArray(value)) return value.length > 0 ? `包含 ${value.length} 项结构化数据。` : "";
  if (typeof value === "object") {
    const entries = Object.entries(value as Record<string, unknown>);
    const useful = entries
      .filter(([, item]) => item != null && item !== "")
      .slice(0, 3)
      .map(([key, item]) => `${jsonKeyLabel(key)}：${summarizeJsonScalar(item)}`)
      .filter(Boolean);
    return useful.length > 0 ? useful.join("；") : "";
  }
  return "";
}

function prettyJsonText(json: string | null | undefined) {
  if (!json || json === "{}") return "暂无结构化数据";
  try {
    return JSON.stringify(JSON.parse(json), null, 2);
  } catch {
    return json;
  }
}

function summarizeJsonScalar(value: unknown) {
  if (typeof value === "string") {
    if (/[A-Za-z]{4,}/.test(value)) return "已记录";
    return compactText(value, 32);
  }
  if (typeof value === "number" || typeof value === "boolean") return String(value);
  if (Array.isArray(value)) return `${value.length} 项`;
  if (typeof value === "object" && value) return "结构化对象";
  return "已记录";
}

function jsonKeyLabel(key: string) {
  const labels: Record<string, string> = {
    team: "球队",
    summary: "摘要",
    baseline_prediction_id: "基线预测",
    risk: "风险",
    decision: "结论",
    source: "来源",
    status: "状态"
  };
  return labels[key] ?? "字段";
}

function workflowTypeLabel(workflowType: string) {
  const labels: Record<string, string> = {
    match_prediction: "比赛预测流程",
    mock_prediction: "演示预测流程",
    intelligence: "情报流程",
    snapshot: "数据快照流程"
  };
  return labels[workflowType] ?? workflowType.replaceAll("_", " ");
}

function TeamAvatar({ team, size }: { team?: WatchObject; size: "sm" | "md" | "lg" }) {
  const image = team ? avatarBySymbol[team.symbol] : undefined;
  const initials = team ? teamShortName(team).slice(0, size === "sm" ? 1 : 2) : "AI";
  return (
    <span className={`team-avatar ${size}`}>
      {image ? <img src={image} alt="" /> : <b>{initials}</b>}
    </span>
  );
}

function ProbabilityRow({ label, value }: { label: string; value: number }) {
  return (
    <div className="theater-prob-row">
      <span>{label}</span>
      <div><i style={{ width: pct(value) }} /></div>
      <strong>{pct(value)}</strong>
    </div>
  );
}

function createMaps(state: CompanyState): CompanyMaps {
  return {
    teamsById: new Map(state.teams.map((team) => [team.id, team])),
    employeesById: new Map(state.employees.map((employee) => [employee.id, employee])),
    assignmentByObject: new Map(state.assignments.map((assignment) => [assignment.object_id, assignment]))
  };
}

function employeeForTeam(team: WatchObject | undefined, maps: CompanyMaps) {
  if (!team) return undefined;
  const assignment = maps.assignmentByObject.get(team.id);
  return assignment ? maps.employeesById.get(assignment.employee_id) : undefined;
}

function compactText(text: string, maxLength: number) {
  return text.length > maxLength ? `${text.slice(0, maxLength)}...` : text;
}

function employeeNameForTeam(employee: Employee | undefined, team: WatchObject | undefined) {
  const fallback = `${teamDisplayName(team)}研究员`;
  if (!employee) return fallback;
  return /Team Researcher|team researcher/i.test(employee.name) ? fallback : employee.name;
}

function displayEmployeeName(employee: Employee, fallback: string) {
  if (/Team Researcher|team researcher/i.test(employee.name)) return fallback;
  if (/data|analyst/i.test(employee.name)) return "数据官";
  if (/risk|review/i.test(employee.name)) return "风险官";
  if (/ceo|chief/i.test(employee.name)) return "CEO";
  return employee.name;
}

function specialistName(employee: Employee | undefined, fallback: string) {
  if (!employee) return fallback;
  if (/data|analyst/i.test(employee.name)) return "数据官";
  if (/risk|review/i.test(employee.name)) return "风险官";
  if (/ceo|chief/i.test(employee.name)) return "CEO";
  return employee.name;
}

function employeeSpecialty(employee: Employee | undefined) {
  if (!employee?.specialty) return "负责球队长期跟踪、公开情报归纳、风险观察与比赛简报。";
  if (/team form|squad|tactics|news|risk/i.test(employee.specialty)) {
    return "负责球队状态、阵容动态、战术变化、公开新闻和风险监控。";
  }
  return employee.specialty;
}

function workflowText(text: string) {
  if (/[A-Za-z]{4,}/.test(text)) {
    if (/intelligence brief triggered/i.test(text)) return "情报简报已触发";
    if (/lineup|squad/i.test(text)) return "阵容或名单信号";
  }
  return text
    .replaceAll("Baseline prediction refreshed", "基线预测已刷新")
    .replaceAll("Employee report generated", "员工报告已生成")
    .replaceAll("CEO summary generated", "CEO 汇总已生成")
    .replaceAll("Data snapshot imported", "数据快照已导入")
    .replaceAll("Workflow step", "工作流步骤")
    .replaceAll("team_report_home", "主队员工报告")
    .replaceAll("team_report_away", "客队员工报告")
    .replaceAll("data_analysis", "数据分析")
    .replaceAll("risk_review", "风险审查")
    .replaceAll("ceo_summary", "CEO 汇总");
}

function predictionExplanation(
  prediction: BaselinePrediction,
  selectedMatch: Match | null,
  maps: CompanyMaps
) {
  if (!selectedMatch) return "系统已生成基线概率，等待选择具体比赛。";
  const home = teamDisplayName(maps.teamsById.get(selectedMatch.home_object_id), selectedMatch.home_object_id);
  const away = teamDisplayName(maps.teamsById.get(selectedMatch.away_object_id), selectedMatch.away_object_id);
  const winner = prediction.home_win_probability >= prediction.away_win_probability ? home : away;
  return `${home} 对阵 ${away}：Baseline 根据排名、主客场因素、热门压力和结构化情报信号给出初始概率。目前模型略倾向 ${winner}，仍需要员工报告和风险官复核。`;
}

function reportBriefText(
  selectedTeam: WatchObject | undefined,
  selectedMatch: Match | null,
  prediction: BaselinePrediction | null,
  signalCount: number,
  artifactCount: number,
  maps: CompanyMaps
) {
  const teamName = teamDisplayName(selectedTeam);
  if (!selectedMatch) return `${teamName}研究员已就绪，等待选择比赛后进入协作流程。`;
  const home = teamDisplayName(maps.teamsById.get(selectedMatch.home_object_id), selectedMatch.home_object_id);
  const away = teamDisplayName(maps.teamsById.get(selectedMatch.away_object_id), selectedMatch.away_object_id);
  const predictionText = prediction ? `系统已有 ${predictionLeader(prediction, selectedMatch, maps)} 的 Baseline 倾向` : "系统还没有生成 Baseline 倾向";
  return `${teamName}正在参与 ${home} 对阵 ${away} 的赛前研究。当前已归集 ${signalCount} 条情报和 ${artifactCount} 个产物，${predictionText}。`;
}

function predictionLeader(
  prediction: BaselinePrediction,
  selectedMatch: Match | null,
  maps: CompanyMaps
) {
  if (!selectedMatch) return "待判断";
  const home = teamDisplayName(maps.teamsById.get(selectedMatch.home_object_id), selectedMatch.home_object_id);
  const away = teamDisplayName(maps.teamsById.get(selectedMatch.away_object_id), selectedMatch.away_object_id);
  const entries = [
    { label: `${home}胜`, value: prediction.home_win_probability },
    { label: "平局", value: prediction.draw_probability },
    { label: `${away}胜`, value: prediction.away_win_probability }
  ].sort((a, b) => b.value - a.value);
  return entries[0].label;
}

function predictionGapText(prediction: BaselinePrediction) {
  const values = [
    prediction.home_win_probability,
    prediction.draw_probability,
    prediction.away_win_probability
  ].sort((a, b) => b - a);
  const gap = Math.max(0, values[0] - values[1]);
  if (gap < 0.06) return "优势很小，需要重点看风险官复核。";
  if (gap < 0.14) return "存在轻微优势，仍需结合情报变化。";
  return "优势较清晰，可以优先进入 CEO 汇总。";
}

function signalTypeLabel(type: string) {
  const labels: Record<string, string> = {
    news_update: "新闻更新",
    team_profile: "球队资料",
    fixture_update: "赛程更新",
    injury: "伤病信号",
    ranking: "排名变化",
    roster: "阵容动态",
    odds: "赔率变化",
    risk: "风险提示"
  };
  return labels[type] ?? "情报信号";
}

function severityLabel(severity: string) {
  const labels: Record<string, string> = {
    low: "低",
    medium: "中",
    high: "高",
    critical: "关键"
  };
  return labels[severity] ?? "普通";
}

function categoryLabel(category: string) {
  const labels: Record<string, string> = {
    data: "数据",
    intelligence: "情报",
    employee: "员工",
    workflow: "流程",
    llm: "模型",
    system: "系统"
  };
  return labels[category] ?? "记录";
}

function formatTime(value: string | undefined) {
  if (!value) return "时间待确认";
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;
  return date.toLocaleString("zh-CN", {
    month: "2-digit",
    day: "2-digit",
    hour: "2-digit",
    minute: "2-digit"
  });
}

function signalSummaryText(text: string) {
  if (!text) return "系统捕捉到一条公开情报，已进入该员工的待分析队列。";
  if (/lineup|squad/i.test(text)) return "系统捕捉到一条阵容或名单相关情报，已归档为员工分析素材。";
  if (/[A-Za-z]{4,}/.test(text)) {
    return "系统捕捉到一条公开新闻情报，已归档为员工分析素材。";
  }
  return compactText(text, 92);
}

function sourceLabel(source: string) {
  const labels: Record<string, string> = {
    worldcup26_games: "世界杯赛程源",
    worldcup26_teams: "世界杯球队源",
    worldcup26_groups: "世界杯分组源",
    worldcup26_stadiums: "世界杯场馆源",
    openfootball_schedule: "OpenFootball 赛程",
    fixturedownload_schedule: "FixtureDownload 赛程",
    rss_soccer_news: "公开足球新闻"
  };
  return labels[source] ?? source;
}

function snapshotTypeLabel(type: string) {
  const labels: Record<string, string> = {
    team_profile: "球队资料",
    fixture: "赛程快照",
    group_table: "分组快照",
    stadium_profile: "场馆资料",
    news_intel: "新闻情报"
  };
  return labels[type] ?? type;
}

function parseEvidence(raw: string) {
  const fallback = { source: "", captured_at: "", excerpt: "", url: "" };
  if (!raw) return fallback;
  try {
    const data = JSON.parse(raw) as Record<string, unknown>;
    const excerpt = String(data.excerpt ?? "");
    return {
      source: String(data.source ?? ""),
      captured_at: String(data.captured_at ?? ""),
      excerpt,
      url: extractUrl(excerpt) || extractUrl(raw)
    };
  } catch {
    return { ...fallback, excerpt: raw, url: extractUrl(raw) };
  }
}

function extractUrl(text: string) {
  const direct = text.match(/https?:\/\/[^\s"'<>\\]+/);
  if (direct) return direct[0];
  try {
    const data = JSON.parse(text) as Record<string, unknown>;
    return typeof data.url === "string" ? data.url : "";
  } catch {
    return "";
  }
}

function evidenceText(text: string) {
  if (!text) return "没有可展示的原始证据片段。";
  let cleaned = text
    .replace(/\\u003C/g, "<")
    .replace(/\\u003E/g, ">")
    .replace(/\\u0022/g, "\"")
    .replace(/<[^>]+>/g, " ")
    .replace(/\s+/g, " ")
    .trim();

  try {
    const data = JSON.parse(cleaned) as Record<string, unknown>;
    const title = typeof data.title === "string" ? data.title : "";
    const description = typeof data.description === "string" ? data.description : "";
    cleaned = [title, description].filter(Boolean).join("。");
  } catch {
    // Keep the cleaned string when the evidence is not JSON.
  }

  return compactText(cleaned, 220);
}

function reportContentLines(content: string) {
  return content
    .split(/\r?\n/)
    .map((line) => line.replace(/^#+\s*/, "").replace(/^[-*]\s*/, "").trim())
    .filter(Boolean)
    .filter((line) => !line.startsWith("Team:") && !line.startsWith("Researcher:") && !line.startsWith("Generated at:"))
    .map((line) => line.replaceAll("Mode: structured no-LLM trigger", "模式：结构化触发"))
    .slice(0, 10);
}

function artifactTitle(title: string, team: WatchObject | undefined) {
  if (!title) return "员工产物";
  if (/intelligence brief/i.test(title)) return `${teamDisplayName(team)}情报简报`;
  if (/\bvs\b/i.test(title)) return "赛前预测报告";
  return title
    .replaceAll("Team intelligence brief", "球队情报简报")
    .replaceAll("CEO Summary", "CEO 汇总")
    .replaceAll("Baseline prediction", "基线预测")
    .replaceAll("Match report", "比赛报告");
}

function artifactSummary(summary: string) {
  if (!summary) return "该产物已归档，可作为后续复盘和记忆材料。";
  if (/[A-Za-z]{4,}/.test(summary)) {
    return "员工已生成结构化工作产物，系统已归档供 CEO 汇总和后续复盘使用。";
  }
  return compactText(summary, 92);
}

function artifactContentPreview(content: string) {
  if (!content.trim()) return "产物正文为空，当前仅展示归档摘要。";
  const cleaned = content
    .split(/\r?\n/)
    .map((line) => line.replace(/^#+\s*/, "").replace(/^[-*]\s*/, "").trim())
    .filter(Boolean)
    .filter((line) => !/[A-Za-z]{4,}/.test(line))
    .slice(0, 3)
    .join("。");
  if (cleaned) return compactText(cleaned, 180);
  return "产物正文已读取，但主要内容包含英文或结构化标识；当前中文界面先展示摘要，后续可增加正文翻译层。";
}

function statusLabel(status: string | undefined) {
  const labels: Record<string, string> = {
    active: "在岗",
    completed: "已完成",
    failed: "失败",
    running: "运行中",
    needs_review: "需复核",
    eliminated: "已离职",
    pending: "待入职",
    archived: "已归档"
  };
  return status ? labels[status] ?? status : "待确认";
}

import { Brain, BriefcaseBusiness, Database, FileText, MapPinned } from "lucide-react";
import type { CompanyState, Match, SceneSelection } from "../types";
import { pct } from "../utils/format";
import { teamDisplayName } from "../utils/teamNames";

type InspectorPanelProps = {
  state: CompanyState | null;
  selection: SceneSelection;
  selectedMatch: Match | null;
  onSelectMatch: (matchId: string) => void;
};

export function InspectorPanel({ state, selection, selectedMatch, onSelectMatch }: InspectorPanelProps) {
  if (!state) {
    return (
      <aside className="inspector-panel">
        <PanelHeader icon={<Database size={18} />} title="公司系统" />
        <div className="empty-block">正在读取世界杯公司数据。</div>
      </aside>
    );
  }

  const teamsById = new Map(state.teams.map((team) => [team.id, team]));
  const employeesById = new Map(state.employees.map((employee) => [employee.id, employee]));
  const assignmentByObject = new Map(state.assignments.map((assignment) => [assignment.object_id, assignment]));
  const selectedPrediction = selectedMatch
    ? state.predictions.find((prediction) => prediction.match_id === selectedMatch.id)
    : null;

  let detail = (
    <div className="detail-card">
      <strong>点击员工、工位气泡或房间</strong>
      <p>主场景是公司经营视角。点击任何球队员工可以查看负责人、球队状态、信号和工作产出。</p>
    </div>
  );

  if (selection?.type === "employee") {
    const team = teamsById.get(selection.objectId);
    const employee = employeesById.get(selection.employeeId);
    const signals = state.signals.filter((signal) => signal.object_id === selection.objectId);
    const artifacts = state.artifacts.filter((artifact) => artifact.object_id === selection.objectId).slice(0, 4);
    detail = (
      <div className="detail-card">
        <div className="employee-focus">
          <div className="employee-token">{team?.symbol ?? "AI"}</div>
          <div>
            <span>{employee?.role ?? "球队研究员"}</span>
            <strong>{employee?.name ?? "未分配员工"}</strong>
          </div>
        </div>
        <p>{teamDisplayName(team, selection.objectId)} 专属研究员，负责跟踪公开信息、分拣信号并触发简报。</p>
        <div className="detail-grid-mini">
          <div><span>状态</span><strong>{team?.status ?? "-"}</strong></div>
          <div><span>情报信号</span><strong>{signals.length}</strong></div>
          <div><span>产出</span><strong>{artifacts.length}</strong></div>
        </div>
        {signals.slice(0, 3).map((signal) => (
          <article className="mini-event" key={signal.id}>
            <span>{signal.signal_type}</span>
            <p>{signal.summary}</p>
          </article>
        ))}
      </div>
    );
  }

  if (selection?.type === "room") {
    detail = (
      <div className="detail-card">
        <strong>{roomLabel(selection.roomId)}</strong>
        <p>{roomDescription(selection.roomId)}</p>
      </div>
    );
  }

  if (selection?.type === "event") {
    const log = state.logs.find((item) => item.id === selection.logId);
    detail = (
      <div className="detail-card">
        <strong>{log?.title || "系统事件"}</strong>
        <p>{log?.message || log?.event_type || "暂无详情"}</p>
        <code>{[log?.category, log?.source, log?.created_at].filter(Boolean).join(" / ")}</code>
      </div>
    );
  }

  return (
    <aside className="inspector-panel">
      <PanelHeader icon={<MapPinned size={18} />} title="公司控制台" />
      <section className="match-selector">
        <div className="section-title">
          <BriefcaseBusiness size={16} />
          <span>当前比赛</span>
        </div>
        <select value={selectedMatch?.id ?? ""} onChange={(event) => onSelectMatch(event.target.value)}>
          {state.matches.map((match) => {
            const home = teamsById.get(match.home_object_id);
            const away = teamsById.get(match.away_object_id);
            return (
              <option key={match.id} value={match.id}>
                {teamDisplayName(home, match.home_object_id)} 对阵 {teamDisplayName(away, match.away_object_id)}
              </option>
            );
          })}
        </select>
        {selectedMatch && selectedPrediction ? (
          <div className="prediction-stack">
            <ProbabilityRow label="主胜" value={selectedPrediction.home_win_probability} />
            <ProbabilityRow label="平局" value={selectedPrediction.draw_probability} />
            <ProbabilityRow label="客胜" value={selectedPrediction.away_win_probability} />
            <p>{selectedPrediction.explanation}</p>
          </div>
        ) : null}
      </section>
      <section>
        <div className="section-title">
          <Brain size={16} />
          <span>对象详情</span>
        </div>
        {detail}
      </section>
      <section>
        <div className="section-title">
          <FileText size={16} />
          <span>员工状态速览</span>
        </div>
        <div className="roster-mini-list">
          {state.teams.slice(0, 10).map((team) => {
            const assignment = assignmentByObject.get(team.id);
            const employee = assignment ? employeesById.get(assignment.employee_id) : null;
            return (
              <article key={team.id}>
                <b>{team.symbol}</b>
                <span>{employee?.name ?? "未分配"}</span>
                <em>{statusLabel(team.status)}</em>
              </article>
            );
          })}
        </div>
      </section>
    </aside>
  );
}

function PanelHeader({ icon, title }: { icon: React.ReactNode; title: string }) {
  return (
    <div className="panel-header">
      {icon}
      <strong>{title}</strong>
    </div>
  );
}

function ProbabilityRow({ label, value }: { label: string; value: number }) {
  return (
    <div className="probability-row">
      <span>{label}</span>
      <div><i style={{ width: pct(value) }} /></div>
      <strong>{pct(value)}</strong>
    </div>
  );
}

function roomLabel(roomId: string) {
  const labels: Record<string, string> = {
    ceo_room: "CEO 决策室",
    data_room: "数据情报室",
    risk_room: "风险审查席",
    hr_room: "员工状态席",
    meeting_room: "战术会议室",
    source_gate: "公开数据入口",
    memory_wall: "长期记忆墙"
  };
  return labels[roomId] ?? roomId;
}

function roomDescription(roomId: string) {
  const descriptions: Record<string, string> = {
    ceo_room: "CEO 汇总各岗位意见，形成最终比赛判断和执行摘要。",
    data_room: "数据分析员工在这里处理排名、赛程、历史表现和结构化证据。",
    risk_room: "风险官审查伤停、冷门、数据偏差和模型不确定性。",
    hr_room: "HR 追踪球队员工状态，球队出局后负责离职和档案归档。",
    meeting_room: "球队员工、风险官和 CEO 在这里协作，形成交叉验证。",
    source_gate: "公开数据、新闻、赛程和情报快照从这里进入公司。",
    memory_wall: "赛后复盘和长期记忆沉淀在这里，供后续比赛调用。"
  };
  return descriptions[roomId] ?? "公司运行区域。";
}

function statusLabel(status: string) {
  const labels: Record<string, string> = {
    active: "在岗",
    eliminated: "已离职",
    pending: "待入职",
    archived: "已归档"
  };
  return labels[status] ?? status;
}

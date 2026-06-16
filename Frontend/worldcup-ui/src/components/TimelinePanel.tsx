import { Activity, Database, FileCheck, MessageSquareText } from "lucide-react";
import type { CompanyState } from "../types";

type TimelinePanelProps = {
  state: CompanyState | null;
};

export function TimelinePanel({ state }: TimelinePanelProps) {
  const logs = state?.logs.slice(0, 12) ?? [];

  return (
    <footer className="timeline-panel">
      <div className="timeline-summary">
        <Metric icon={<Database size={17} />} label="球队对象" value={String(state?.teams.length ?? "-")} />
        <Metric icon={<Activity size={17} />} label="情报信号" value={String(state?.signals.length ?? "-")} />
        <Metric icon={<FileCheck size={17} />} label="流程记录" value={String(state?.workflows.length ?? "-")} />
        <Metric icon={<MessageSquareText size={17} />} label="系统事件" value={String(state?.logs.length ?? "-")} />
      </div>
      <div className="event-rail">
        {logs.length > 0 ? logs.map((log) => (
          <article key={log.id}>
            <span>{categoryLabel(log.category)}</span>
            <strong>{log.title || log.message || log.event_type}</strong>
            <em>{log.created_at}</em>
          </article>
        )) : <div className="empty-inline">等待公司事件进入时间线。</div>}
      </div>
    </footer>
  );
}

function Metric({ icon, label, value }: { icon: React.ReactNode; label: string; value: string }) {
  return (
    <div className="timeline-metric">
      {icon}
      <span>{label}</span>
      <strong>{value}</strong>
    </div>
  );
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
  return labels[category] ?? category;
}

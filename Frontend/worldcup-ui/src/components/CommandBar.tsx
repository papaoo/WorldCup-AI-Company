import { Activity, ChevronDown, Eye, Globe, Layout, Pause, Play, RefreshCw, Route, Sparkles, Swords } from "lucide-react";
import { useState } from "react";
import type { AutoCollectionRunResult, SimulationMode } from "../types";

type AppView = "theater" | "warroom";

type CommandBarProps = {
  mode: SimulationMode;
  loading: boolean;
  message: string;
  automationRun: AutoCollectionRunResult | null;
  view: AppView;
  onRefresh: () => void;
  onSeed: () => void;
  onBootstrap: () => void;
  onRunAutomation: () => void;
  onToggleMode: () => void;
  onToggleView: () => void;
};

export function CommandBar({
  mode,
  loading,
  message,
  automationRun,
  view,
  onRefresh,
  onSeed,
  onBootstrap,
  onRunAutomation,
  onToggleMode,
  onToggleView
}: CommandBarProps) {
  const [consoleOpen, setConsoleOpen] = useState(false);

  return (
    <header className="command-bar">
      <div className="brand-lockup">
        <div className="brand-mark">WC</div>
        <div>
          <span>世界杯 AI 公司</span>
          <strong>{view === "warroom" ? "研究战情室" : "公司剧场"}</strong>
        </div>
      </div>
      <div className="command-actions">
        <button onClick={onToggleView} className="command-button view-toggle" disabled={loading}>
          {view === "theater" ? <Swords size={17} /> : <Layout size={17} />}
          {view === "theater" ? "战情室" : "公司剧场"}
        </button>
        <button onClick={onToggleMode} className="command-button secondary" disabled={loading}>
          {mode === "live" ? <Pause size={17} /> : <Play size={17} />}
          {mode === "live" ? "暂停" : "继续"}
        </button>
        <button onClick={onSeed} className="command-button" disabled={loading}>
          <Sparkles size={17} />
          演示数据
        </button>
        <button onClick={onBootstrap} className="command-button primary" disabled={loading}>
          <Globe size={17} />
          真实数据
        </button>
        <button onClick={onRunAutomation} className="command-button automation" disabled={loading}>
          <Route size={17} /> 运行闭环
        </button>
        <button
          onClick={() => setConsoleOpen((current) => !current)}
          className={`command-button secondary console-toggle ${consoleOpen ? "open" : ""}`}
          type="button"
          title="自动化控制台"
        >
          <Activity size={17} /> 自动化 <ChevronDown size={14} />
        </button>
        <button onClick={onRefresh} className="command-button secondary" disabled={loading}>
          <RefreshCw size={17} /> 同步数据
        </button>
        <div className={`sync-status ${loading ? "busy" : ""}`}>
          {loading ? <Activity size={15} /> : <Eye size={15} />}
          <span>{loading ? "连接公司系统" : message}</span>
        </div>
      </div>
      {consoleOpen ? <AutomationConsole run={automationRun} /> : null}
    </header>
  );
}

function AutomationConsole({ run }: { run: AutoCollectionRunResult | null }) {
  if (!run) {
    return (
      <section className="automation-console">
        <div className="automation-console-title">
          <span>自动化运行记录</span>
          <strong>暂无采集记录</strong>
        </div>
        <p>运行一次公开数据采集后，这里会显示最近一次数据源检查、情报分拣、员工报告触发和质量检查结果。</p>
      </section>
    );
  }

  const sourceText = `${num(run.sources_succeeded)}/${num(run.sources_checked)}`;
  const note = firstNote(run.notes)
    || firstNote(run.snapshot_quality?.notes)
    || firstNote(run.intelligence_queue_quality?.notes)
    || "最近一次自动化流程已归档。";

  return (
    <section className="automation-console">
      <div className="automation-console-title">
        <span>自动化运行记录</span>
        <strong>{run.passed === false ? "需要复核" : "最近一次运行正常"}</strong>
        <em>{timeText(run.completed_at || run.started_at)}</em>
      </div>
      <div className="automation-metrics">
        <Metric label="数据源" value={sourceText} />
        <Metric label="新增快照" value={num(run.imported)} />
        <Metric label="重复跳过" value={num(run.skipped_duplicates)} />
        <Metric label="刷新预测" value={num(run.baseline_predictions_refreshed)} />
      </div>
      <div className="automation-pipeline">
        <Step title="数据质量" ok={run.snapshot_quality?.passed} meta={`${num(run.snapshot_quality?.valid_json_count)} 条有效 JSON`} />
        <Step title="情报队列" ok={run.intelligence_queue_quality?.passed} meta={`${num(run.intelligence_queue_quality?.actionable_signals)} 条可行动信号`} />
        <Step title="情报分拣" ok={run.intelligence_triage?.passed} meta={`${num(run.intelligence_triage?.signals_created)} 条新增信号`} />
        <Step title="员工简报" ok={run.employee_report_trigger?.passed} meta={`${num(run.employee_report_trigger?.reports_created)} 份报告`} />
      </div>
      <p>{note}</p>
    </section>
  );
}

function Metric({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <span>{label}</span>
      <strong>{value}</strong>
    </div>
  );
}

function Step({ title, ok, meta }: { title: string; ok?: boolean; meta: string }) {
  return (
    <article className={ok === false ? "needs-review" : "passed"}>
      <i>{ok === false ? "!" : "✓"}</i>
      <div>
        <strong>{title}</strong>
        <span>{meta}</span>
      </div>
    </article>
  );
}

function num(value: number | undefined) {
  return new Intl.NumberFormat("zh-CN").format(value ?? 0);
}

function timeText(value: string | undefined) {
  if (!value) return "时间待确认";
  const date = new Date(value.replace(" ", "T"));
  if (Number.isNaN(date.getTime())) return value;
  return date.toLocaleString("zh-CN", { month: "2-digit", day: "2-digit", hour: "2-digit", minute: "2-digit" });
}

function firstNote(notes: string[] | undefined) {
  const note = notes?.find((item) => item.trim().length > 0) ?? "";
  return translateAutomationNote(note);
}

function translateAutomationNote(note: string) {
  return note
    .replace("No new snapshots were imported; existing duplicate data was reused.", "本次没有导入新快照，系统复用了已存在的重复数据。")
    .replace("No pending intelligence signals are available for LLM report generation.", "当前没有待处理情报信号可用于生成模型报告。");
}

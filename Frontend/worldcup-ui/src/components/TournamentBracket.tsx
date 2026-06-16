import { useState } from "react";
import {
  ChevronRight,
  Flag,
  Gauge,
  Medal,
  Swords,
  Trophy,
  Users
} from "lucide-react";
import type { BaselinePrediction, CompanyState, Employee, Match, WatchObject } from "../types";
import { pct } from "../utils/format";
import { teamDisplayName, teamShortName } from "../utils/teamNames";

type TournamentBracketProps = {
  state: CompanyState;
  selectedMatch: Match | null;
  onSelectMatch: (matchId: string) => void;
};

type GroupTeam = {
  team: WatchObject;
  employee: Employee | null;
  played: number;
  won: number;
  drawn: number;
  lost: number;
  goalsFor: number;
  goalsAgainst: number;
  points: number;
};

type GroupTable = {
  name: string;
  teams: GroupTeam[];
};

type KnockoutSlot = {
  round: string;
  label: string;
  matchId: string | null;
  homeTeam: WatchObject | null;
  awayTeam: WatchObject | null;
  homeScore: number | null;
  awayScore: number | null;
  prediction: BaselinePrediction | null;
};

export function TournamentBracket({ state, selectedMatch, onSelectMatch }: TournamentBracketProps) {
  const [view, setView] = useState<"groups" | "bracket" | "schedule">("groups");

  const teamsById = new Map(state.teams.map((t) => [t.id, t]));
  const employeesById = new Map(state.employees.map((e) => [e.id, e]));
  const assignmentByObject = new Map(state.assignments.map((a) => [a.object_id, a]));
  const predictionsByMatch = new Map(state.predictions.map((p) => [p.match_id, p]));

  const groups = buildGroupTables(state.teams, state.matches, employeesById, assignmentByObject);
  const matches = state.matches;

  return (
    <section className="tournament-bracket">
      <header className="bracket-header">
        <div className="bracket-title">
          <Trophy size={24} />
          <div>
            <strong>2026 世界杯 · 战情室</strong>
            <span>48支球队 · 12个小组 · AI 驱动的赛事前瞻与预测</span>
          </div>
        </div>
        <nav className="bracket-nav">
          <button className={view === "groups" ? "active" : ""} onClick={() => setView("groups")}>
            <Users size={16} /> 小组赛
          </button>
          <button className={view === "bracket" ? "active" : ""} onClick={() => setView("bracket")}>
            <Swords size={16} /> 淘汰赛对阵
          </button>
          <button className={view === "schedule" ? "active" : ""} onClick={() => setView("schedule")}>
            <Flag size={16} /> 赛程列表
          </button>
        </nav>
      </header>

      {view === "groups" && (
        <GroupStageView groups={groups} predictionsByMatch={predictionsByMatch} matches={matches} teamsById={teamsById} />
      )}
      {view === "bracket" && (
        <KnockoutBracketView
          teamsById={teamsById}
          matches={matches}
          predictionsByMatch={predictionsByMatch}
          selectedMatch={selectedMatch}
          onSelectMatch={onSelectMatch}
        />
      )}
      {view === "schedule" && (
        <ScheduleView
          matches={matches}
          teamsById={teamsById}
          predictionsByMatch={predictionsByMatch}
          selectedMatch={selectedMatch}
          onSelectMatch={onSelectMatch}
        />
      )}

      <footer className="bracket-footer">
        <Gauge size={14} />
        <span>数据来源：系统内置 2026 世界杯球队与赛程数据（种子数据）</span>
        <span className="data-badge seed">种子数据</span>
      </footer>
    </section>
  );
}

function GroupStageView({
  groups,
  predictionsByMatch,
  matches,
  teamsById
}: {
  groups: GroupTable[];
  predictionsByMatch: Map<string, BaselinePrediction>;
  matches: Match[];
  teamsById: Map<string, WatchObject>;
}) {
  const [expandedGroup, setExpandedGroup] = useState<string | null>(null);

  return (
    <div className="group-stage">
      <div className="group-stage-intro">
        <p>48支球队分为12个小组（A-L），每组4队。小组前两名和8个成绩最好的小组第三名晋级32强淘汰赛。</p>
      </div>
      <div className="group-grid">
        {groups.map((group) => {
          const expanded = expandedGroup === group.name;
          const groupMatches = matches.filter(
            (m) => m.group_name === group.name && m.stage === "group_stage"
          );
          return (
            <article
              key={group.name}
              className={`group-card ${expanded ? "expanded" : ""}`}
            >
              <button
                className="group-card-header"
                onClick={() => setExpandedGroup(expanded ? null : group.name)}
              >
                <span className="group-badge">G{group.name}</span>
                <strong>{group.name}组</strong>
                <ChevronRight size={14} className={expanded ? "rotated" : ""} />
              </button>
              <table className="group-table">
                <thead>
                  <tr>
                    <th>#</th>
                    <th>球队</th>
                    <th>赛</th>
                    <th>胜</th>
                    <th>平</th>
                    <th>负</th>
                    <th>分</th>
                  </tr>
                </thead>
                <tbody>
                  {group.teams.map((gt, i) => (
                    <tr key={gt.team.id} className={i < 2 ? "advance" : i === 2 ? "third" : ""}>
                      <td className="pos">{i + 1}</td>
                      <td className="team-name">
                        <span className="team-flag">{gt.team.symbol}</span>
                        {teamDisplayName(gt.team)}
                      </td>
                      <td>{gt.played}</td>
                      <td>{gt.won}</td>
                      <td>{gt.drawn}</td>
                      <td>{gt.lost}</td>
                      <td className="pts">{gt.points}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
              {expanded && groupMatches.length > 0 && (
                <div className="group-matches">
                  <span className="group-matches-title">小组赛程</span>
                  {groupMatches.map((m) => {
                    const pred = predictionsByMatch.get(m.id);
                    return (
                      <div key={m.id} className="group-match-row">
                        <span>{teamShortName(teamsById.get(m.home_object_id))} vs {teamShortName(teamsById.get(m.away_object_id))}</span>
                        {pred && (
                          <span className="mini-pred">
                            {pct(pred.home_win_probability)} / {pct(pred.draw_probability)} / {pct(pred.away_win_probability)}
                          </span>
                        )}
                        {m.status === "finished" && m.home_score != null && (
                          <span className="score">{m.home_score}:{m.away_score}</span>
                        )}
                      </div>
                    );
                  })}
                </div>
              )}
            </article>
          );
        })}
      </div>
    </div>
  );
}

function KnockoutBracketView({
  teamsById,
  matches,
  predictionsByMatch,
  selectedMatch,
  onSelectMatch
}: {
  teamsById: Map<string, WatchObject>;
  matches: Match[];
  predictionsByMatch: Map<string, BaselinePrediction>;
  selectedMatch: Match | null;
  onSelectMatch: (matchId: string) => void;
}) {
  const knockoutMatches = matches.filter((m) =>
    m.stage !== "group_stage" && m.stage !== "pre_tournament"
  );
  const stages = ["round_of_32", "round_of_16", "quarter_final", "semi_final", "final"];

  if (knockoutMatches.length === 0) {
    return (
      <div className="knockout-bracket empty">
        <p>淘汰赛对阵将在小组赛结束后生成。当前系统中暂无淘汰赛比赛数据。</p>
        <p className="hint">运行种子数据后，会包含完整的淘汰赛对阵和预测。</p>
      </div>
    );
  }

  return (
    <div className="knockout-bracket">
      <div className="bracket-rounds">
        {stages.map((stage) => {
          const stageMatches = knockoutMatches.filter((m) => m.stage === stage);
          if (stageMatches.length === 0) return null;
          return (
            <div key={stage} className="bracket-round">
              <span className="round-label">{roundLabel(stage)}</span>
              <div className="round-matches">
                {stageMatches.map((m) => {
                  const pred = predictionsByMatch.get(m.id);
                  const home = teamsById.get(m.home_object_id);
                  const away = teamsById.get(m.away_object_id);
                  const isSelected = selectedMatch?.id === m.id;
                  return (
                    <button
                      key={m.id}
                      className={`bracket-match ${isSelected ? "selected" : ""} ${m.status === "finished" ? "finished" : ""}`}
                      onClick={() => onSelectMatch(m.id)}
                    >
                      <div className="match-team home">
                        <span className="flag">{m.home_object_id}</span>
                        <span>{teamShortName(home, m.home_object_id)}</span>
                        {m.status === "finished" && <strong>{m.home_score}</strong>}
                      </div>
                      <div className="match-vs">
                        {m.status === "finished" ? (
                          <span className="result">{m.home_score}:{m.away_score}</span>
                        ) : pred ? (
                          <span className="pred-mini">{pct(pred.home_win_probability)}</span>
                        ) : (
                          <span>vs</span>
                        )}
                      </div>
                      <div className="match-team away">
                        <span className="flag">{m.away_object_id}</span>
                        <span>{teamShortName(away, m.away_object_id)}</span>
                        {m.status === "finished" && <strong>{m.away_score}</strong>}
                      </div>
                    </button>
                  );
                })}
              </div>
            </div>
          );
        })}
      </div>
    </div>
  );
}

function ScheduleView({
  matches,
  teamsById,
  predictionsByMatch,
  selectedMatch,
  onSelectMatch
}: {
  matches: Match[];
  teamsById: Map<string, WatchObject>;
  predictionsByMatch: Map<string, BaselinePrediction>;
  selectedMatch: Match | null;
  onSelectMatch: (matchId: string) => void;
}) {
  const [filter, setFilter] = useState<string>("all");

  const stages = [...new Set(matches.map((m) => m.stage))];
  const filtered = filter === "all" ? matches : matches.filter((m) => m.stage === filter);

  return (
    <div className="schedule-view">
      <div className="schedule-filters">
        <button className={filter === "all" ? "active" : ""} onClick={() => setFilter("all")}>全部</button>
        {stages.map((s) => (
          <button key={s} className={filter === s ? "active" : ""} onClick={() => setFilter(s)}>
            {stageLabelShort(s)}
          </button>
        ))}
      </div>
      <div className="schedule-list">
        {filtered.map((m) => {
          const pred = predictionsByMatch.get(m.id);
          const home = teamsById.get(m.home_object_id);
          const away = teamsById.get(m.away_object_id);
          const isSelected = selectedMatch?.id === m.id;
          return (
            <button
              key={m.id}
              className={`schedule-row ${isSelected ? "selected" : ""} ${m.status === "finished" ? "finished" : ""}`}
              onClick={() => onSelectMatch(m.id)}
            >
              <span className="schedule-stage">{stageLabelShort(m.stage)}</span>
              <span className="schedule-group">{m.group_name}</span>
              <span className="schedule-teams">
                <span className={m.home_score != null && m.home_score > (m.away_score ?? 0) ? "winner" : ""}>
                  {teamDisplayName(home, m.home_object_id)}
                </span>
                <span className="vs">vs</span>
                <span className={m.away_score != null && m.away_score > (m.home_score ?? 0) ? "winner" : ""}>
                  {teamDisplayName(away, m.away_object_id)}
                </span>
              </span>
              {m.status === "finished" && m.home_score != null ? (
                <span className="schedule-score">{m.home_score}:{m.away_score}</span>
              ) : (
                <span className="schedule-time">{m.kickoff_time?.slice(0, 10) || "待定"}</span>
              )}
              {pred && m.status !== "finished" && (
                <span className="schedule-pred">
                  主{pct(pred.home_win_probability)} 平{pct(pred.draw_probability)} 客{pct(pred.away_win_probability)}
                </span>
              )}
              <span className={`schedule-status ${m.status}`}>{matchStatusLabel(m.status)}</span>
            </button>
          );
        })}
      </div>
    </div>
  );
}

function buildGroupTables(
  teams: WatchObject[],
  matches: Match[],
  employeesById: Map<string, Employee>,
  assignmentByObject: Map<string, { employee_id: string }>
): GroupTable[] {
  const groups = new Map<string, GroupTeam[]>();

  for (const team of teams) {
    const metadata = parseTeamMetadata(team.metadata_json);
    const groupName = metadata.group || "?";
    if (!groups.has(groupName)) groups.set(groupName, []);
    const assign = assignmentByObject.get(team.id);
    groups.get(groupName)!.push({
      team,
      employee: assign ? employeesById.get(assign.employee_id) ?? null : null,
      played: 0,
      won: 0,
      drawn: 0,
      lost: 0,
      goalsFor: 0,
      goalsAgainst: 0,
      points: 0
    });
  }

  for (const match of matches) {
    if (match.stage !== "group_stage" || match.home_score == null || match.away_score == null) continue;
    const groupTeams = groups.get(match.group_name);
    if (!groupTeams) continue;
    const home = groupTeams.find((gt) => gt.team.id === match.home_object_id);
    const away = groupTeams.find((gt) => gt.team.id === match.away_object_id);
    if (!home || !away) continue;
    home.played++; away.played++;
    home.goalsFor += match.home_score; home.goalsAgainst += match.away_score;
    away.goalsFor += match.away_score; away.goalsAgainst += match.home_score;
    if (match.home_score > match.away_score) { home.won++; home.points += 3; away.lost++; }
    else if (match.home_score < match.away_score) { away.won++; away.points += 3; home.lost++; }
    else { home.drawn++; away.drawn++; home.points++; away.points++; }
  }

  const result: GroupTable[] = [];
  for (const [name, groupTeams] of groups) {
    groupTeams.sort((a, b) => b.points - a.points || (b.goalsFor - b.goalsAgainst) - (a.goalsFor - a.goalsAgainst));
    result.push({ name, teams: groupTeams });
  }
  result.sort((a, b) => a.name.localeCompare(b.name));
  return result;
}

function parseTeamMetadata(json: string): { group?: string; rank?: number } {
  if (!json || json === "{}") return {};
  try {
    const data = JSON.parse(json) as Record<string, unknown>;
    return { group: typeof data.group === "string" ? data.group : undefined, rank: typeof data.rank === "number" ? data.rank : undefined };
  } catch { return {}; }
}

function roundLabel(stage: string): string {
  const labels: Record<string, string> = {
    round_of_32: "32强",
    round_of_16: "16强",
    quarter_final: "1/4决赛",
    semi_final: "半决赛",
    final: "决赛",
    third_place: "三四名"
  };
  return labels[stage] || stage;
}

function stageLabelShort(stage: string): string {
  const labels: Record<string, string> = {
    pre_tournament: "赛前",
    group_stage: "小组赛",
    round_of_32: "32强",
    round_of_16: "16强",
    quarter_final: "8强",
    semi_final: "半决赛",
    final: "决赛",
    post_review: "赛后"
  };
  return labels[stage] || stage;
}

function matchStatusLabel(status: string): string {
  const labels: Record<string, string> = {
    scheduled: "待赛",
    analyzing: "分析中",
    predicted: "已预测",
    finished: "已完赛",
    reviewed: "已复盘"
  };
  return labels[status] || status;
}
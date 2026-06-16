using System.Text.Json;

namespace PiPiClaw.Team;

/// <summary>
/// Manual LLM upgrade for team intelligence briefs. Automatic collection should stay no-LLM by default.
/// </summary>
public static class TeamIntelligenceLlmReportService
{
    public static async Task<AutoLlmReportRunResult> RunBatchAsync(int maxTeams = 2, int maxSignals = 8)
    {
        var store = AppContext.WorldCupStore;
        var result = new AutoLlmReportRunResult
        {
            Enabled = true,
            StartedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
        };

        var teamIds = SelectTeamIdsForBatch(store, maxTeams);
        result.TeamsChecked = teamIds.Count;
        if (teamIds.Count == 0)
        {
            result.CompletedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            result.Passed = true;
            result.Notes.Add("No pending actionable team signals are available for automatic LLM reports.");
            return result;
        }

        foreach (var teamId in teamIds)
        {
            try
            {
                var teamResult = await RunAsync(teamId, maxSignals);
                result.Results.Add(teamResult);
                if (teamResult.Passed)
                {
                    result.ReportsCreated++;
                    result.EstimatedCostUsd += teamResult.LlmCall?.CostEstimate ?? 0;
                }
                else
                {
                    result.FailedReports++;
                    result.Notes.AddRange(teamResult.Notes.Select(note => $"{teamId}: {note}"));
                }
            }
            catch (Exception ex)
            {
                result.FailedReports++;
                result.Notes.Add($"{teamId}: {ex.Message}");
            }
        }

        result.CompletedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        result.Passed = result.FailedReports == 0;
        return result;
    }

    public static async Task<TeamIntelligenceLlmReportResult> RunAsync(string? objectId, int maxSignals = 8)
    {
        var store = AppContext.WorldCupStore;
        var selectedSignals = SelectSignals(store, objectId, maxSignals);
        var result = new TeamIntelligenceLlmReportResult
        {
            ObjectId = objectId ?? "",
            SignalsUsed = selectedSignals.Count
        };

        if (selectedSignals.Count == 0)
        {
            result.Notes.Add("No pending intelligence signals are available for LLM report generation.");
            return result;
        }

        var teamId = selectedSignals.First().ObjectId;
        if (string.IsNullOrWhiteSpace(teamId))
        {
            result.Notes.Add("Selected intelligence signals are not attached to a team.");
            return result;
        }

        var team = store.GetWatchObjectById(teamId);
        if (team == null)
        {
            result.Notes.Add($"Team not found: {teamId}");
            return result;
        }

        var employee = store.GetEmployeeById(store.GetPrimaryEmployeeIdForObject(team.Id) ?? "")
            ?? WorldCupWorkflowService.CreateFallbackEmployee($"emp_{team.Id}_fallback", $"{team.DisplayName} team researcher", "team researcher");
        var prompt = BuildPrompt(team, employee, selectedSignals);
        var taskId = $"wc_intel_{team.Id}_{DateTime.Now:yyyyMMddHHmmss}";
        var targetUrl = string.IsNullOrEmpty(AppContext.Config.MasterNodeUrl) ? "http://127.0.0.1:5050" : AppContext.Config.MasterNodeUrl;

        store.AddSystemEventLog(new WorldCupSystemEventLog
        {
            EventType = "llm_prompt_prepared",
            Category = "llm",
            Source = "team_intelligence_llm",
            EmployeeId = employee.Id,
            ObjectId = team.Id,
            Title = "Team intelligence LLM prompt prepared",
            Message = $"{employee.Name} prepared an LLM intelligence brief prompt for {team.DisplayName}.",
            PayloadJson = JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["task_id"] = taskId,
                ["object_id"] = team.Id,
                ["employee_name"] = employee.Name,
                ["prompt"] = prompt
            }, AppJsonContext.Default.DictionaryStringString)
        });

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var (content, call) = await AppContext.LlmGateway.RunEmployeeTaskAsync(targetUrl, employee, prompt, taskId, employee.ModelIndex, timeout.Token);
        result = store.SaveTeamIntelligenceLlmReport(team, employee, selectedSignals, content, call);
        return result;
    }

    private static List<IntelligenceSignalRecord> SelectSignals(WorldCupStore store, string? objectId, int maxSignals)
    {
        var limit = Math.Clamp(maxSignals, 1, 12);
        var signals = store.GetProductionIntelligenceSignals(status: "needs_ai_review", objectId: objectId, limit: 500)
            .Where(signal => !string.IsNullOrWhiteSpace(signal.ObjectId))
            .Where(IsActionableForLlm)
            .ToList();
        if (!string.IsNullOrWhiteSpace(objectId))
        {
            return signals
                .OrderByDescending(signal => SignalSeverityScore(signal.Severity))
                .ThenByDescending(signal => signal.CreatedAt)
                .Take(limit)
                .ToList();
        }

        return signals
            .GroupBy(signal => signal.ObjectId!)
            .OrderByDescending(group => group.Max(signal => SignalSeverityScore(signal.Severity)))
            .ThenByDescending(group => group.Count())
            .FirstOrDefault()
            ?.OrderByDescending(signal => SignalSeverityScore(signal.Severity))
            .ThenByDescending(signal => signal.CreatedAt)
            .Take(limit)
            .ToList()
            ?? [];
    }

    private static List<string> SelectTeamIdsForBatch(WorldCupStore store, int maxTeams)
    {
        return store.GetProductionIntelligenceSignals(status: "needs_ai_review", limit: 500)
            .Where(signal => !string.IsNullOrWhiteSpace(signal.ObjectId))
            .Where(IsActionableForLlm)
            .GroupBy(signal => signal.ObjectId!)
            .OrderByDescending(group => group.Max(signal => SignalSeverityScore(signal.Severity)))
            .ThenByDescending(group => group.Count())
            .Take(Math.Clamp(maxTeams, 1, 8))
            .Select(group => group.Key)
            .ToList();
    }

    private static string BuildPrompt(WorldCupWatchObject team, WorldCupEmployee employee, IReadOnlyList<IntelligenceSignalRecord> signals)
    {
        var signalLines = string.Join("\n", signals.Select(signal =>
            $"- id: {signal.Id}\n  type: {signal.SignalType}\n  severity: {signal.Severity}\n  confidence: {signal.Confidence:0.00}\n  summary: {signal.Summary}\n  evidence: {signal.EvidenceJson}"));

        return $"""
            你是世界杯 AI 公司中负责 {team.DisplayName} 的球队研究员：{employee.Name}。

            任务：把下面的结构化情报信号升级成一份给 CEO 阅读的中文 Markdown 简报。

            约束：
            1. 只基于输入信号写结论，不要声称你实时浏览了网页。
            2. 明确区分事实、推断、不确定性和下一步核验动作。
            3. 不要给赌博下注建议。
            4. 输出固定章节：# 核心判断、# 关键证据、# 不确定性、# 建议动作。
            5. 必须写明：本报告仅用于赛事情报研究，不构成投注建议，也不保证比赛结果。
            6. 全文控制在 700 字以内。

            球队：{team.DisplayName} ({team.Symbol})
            员工：{employee.Name} / {employee.Role}

            情报信号：
            {signalLines}
            """;
    }

    private static int SignalSeverityScore(string severity)
    {
        return severity.ToLowerInvariant() switch
        {
            "critical" => 4,
            "high" => 3,
            "medium" => 2,
            _ => 1
        };
    }

    private static bool IsActionableForLlm(IntelligenceSignalRecord signal)
    {
        return signal.SignalType is "injury_risk" or "lineup_news";
    }
}

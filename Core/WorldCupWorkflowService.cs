using System.Text.Json;

namespace PiPiClaw.Team;

/// <summary>
/// Real LLM-driven World Cup prediction workflow service.
/// </summary>
public static class WorldCupWorkflowService
{
    public static async Task<MatchWorkflowResult> RunRealWorldCupWorkflowAsync(string matchId)
    {
        var store = AppContext.WorldCupStore;
        var config = AppContext.Config;
        var llmGateway = AppContext.LlmGateway;

        var match = store.GetMatchById(matchId) ?? throw new InvalidOperationException($"Match not found: {matchId}");
        var home = store.GetWatchObjectById(match.HomeObjectId) ?? throw new InvalidOperationException($"Home team not found: {match.HomeObjectId}");
        var away = store.GetWatchObjectById(match.AwayObjectId) ?? throw new InvalidOperationException($"Away team not found: {match.AwayObjectId}");
        var baseline = store.CreateBaselinePrediction(matchId);

        var homeEmployee = store.GetEmployeeById(store.GetPrimaryEmployeeIdForObject(home.Id) ?? "") ?? CreateFallbackEmployee("emp_home_fallback", $"{home.DisplayName}队研究员", "球队研究员");
        var awayEmployee = store.GetEmployeeById(store.GetPrimaryEmployeeIdForObject(away.Id) ?? "") ?? CreateFallbackEmployee("emp_away_fallback", $"{away.DisplayName}队研究员", "球队研究员");
        var dataEmployee = store.GetEmployeeById("emp_data") ?? CreateFallbackEmployee("emp_data", "数据分析师", "数据分析师");
        var riskEmployee = store.GetEmployeeById("emp_risk") ?? CreateFallbackEmployee("emp_risk", "风险官", "风险官");
        var ceoEmployee = store.GetEmployeeById("emp_ceo") ?? CreateFallbackEmployee("emp_ceo", "CEO", "CEO");

        var stepInputs = new[]
        {
            ("team_report_home", homeEmployee, BuildWorldCupPrompt("主队研究报告", match, home, away, baseline, homeEmployee)),
            ("team_report_away", awayEmployee, BuildWorldCupPrompt("客队研究报告", match, away, home, baseline, awayEmployee)),
            ("data_analysis", dataEmployee, BuildWorldCupPrompt("数据分析报告", match, home, away, baseline, dataEmployee)),
            ("risk_review", riskEmployee, BuildWorldCupPrompt("风险审查报告", match, home, away, baseline, riskEmployee)),
            ("ceo_summary", ceoEmployee, BuildWorldCupPrompt("CEO最终汇总", match, home, away, baseline, ceoEmployee))
        };

        var outputs = new List<StepOutputDraft>();
        var llmCalls = new List<LlmCallRecord>();
        foreach (var (stepType, employee, prompt) in stepInputs)
        {
            var taskId = $"wc_{match.Id}_{stepType}";
            var targetUrl = string.IsNullOrEmpty(config.MasterNodeUrl) ? "http://127.0.0.1:5050" : config.MasterNodeUrl;

            store.AddSystemEventLog(new WorldCupSystemEventLog
            {
                EventType = "llm_prompt_prepared",
                Category = "llm",
                Source = "worldcup_workflow",
                EmployeeId = employee.Id,
                MatchId = match.Id,
                Title = "LLM prompt prepared",
                Message = $"{employee.Name} prepared prompt for {stepType}.",
                PayloadJson = JsonSerializer.Serialize(new Dictionary<string, string>
                {
                    ["task_id"] = taskId,
                    ["step_type"] = stepType,
                    ["employee_name"] = employee.Name,
                    ["prompt"] = prompt
                }, AppJsonContext.Default.DictionaryStringString)
            });

            var (content, call) = await llmGateway.RunEmployeeTaskAsync(targetUrl, employee, prompt, taskId, employee.ModelIndex);
            llmCalls.Add(call);

            store.AddSystemEventLog(new WorldCupSystemEventLog
            {
                EventType = "llm_response_received",
                Category = "llm",
                Severity = call.Status == "success" ? "info" : "warning",
                Source = call.Provider,
                EmployeeId = employee.Id,
                MatchId = match.Id,
                LlmCallId = call.Id,
                Title = "LLM response received",
                Message = $"{employee.Name} returned {call.Status} for {stepType}.",
                PayloadJson = JsonSerializer.Serialize(new Dictionary<string, string>
                {
                    ["task_id"] = taskId,
                    ["step_type"] = stepType,
                    ["employee_name"] = employee.Name,
                    ["status"] = call.Status,
                    ["error_message"] = call.ErrorMessage ?? "",
                    ["content"] = content
                }, AppJsonContext.Default.DictionaryStringString)
            });

            outputs.Add(new StepOutputDraft
            {
                StepType = stepType,
                EmployeeId = employee.Id,
                Content = content,
                Source = call.Status == "success" ? "llm" : "fallback",
                LlmCallId = call.Id
            });
        }

        var result = store.SaveMatchPredictionWorkflow(matchId, outputs, "llm", llmCalls);
        store.AddWorkflowMemories(result, outputs);
        return result;
    }

    public static WorldCupEmployee CreateFallbackEmployee(string id, string name, string role)
    {
        return new WorldCupEmployee { Id = id, Name = name, Role = role, Specialty = role, PromptProfile = role };
    }

    public static string BuildWorldCupPrompt(
        string reportType,
        WorldCupMatch match,
        WorldCupWatchObject focus,
        WorldCupWatchObject opponent,
        BaselinePredictionRecord baseline,
        WorldCupEmployee employee)
    {
        var store = AppContext.WorldCupStore;
        var memoryContext = store.BuildMemoryContext(employee.Id, focus.Id, match.Id);
        var dataSnapshotContext = store.BuildDataSnapshotContext(match.Id, focus.Id);
        return $"""
            你是世界杯 AI 公司员工：{employee.Name}
            岗位：{employee.Role}
            专长：{employee.Specialty}

            当前任务：输出《{reportType}》。

            比赛信息：
            - match_id: {match.Id}
            - 阶段: {match.Stage}
            - 时间: {match.KickoffTime}
            - 场地: {match.Venue}
            - 关注球队: {focus.DisplayName} ({focus.Symbol})
            - 对手: {opponent.DisplayName} ({opponent.Symbol})

            已通过基线策略得到的客观概率：
            - 主队胜: {baseline.HomeWinProbability:P1}
            - 平局: {baseline.DrawProbability:P1}
            - 客队胜: {baseline.AwayWinProbability:P1}
            - 策略版本: {baseline.StrategyVersion}
            - 说明: {baseline.Explanation}

            输出要求：
            1. 不要声称已经抓取实时新闻；只能基于当前输入、记忆和结构化快照做克制分析。
            2. 不要直接下注或诱导赌博。
            3. 明确区分事实、推断和风险，不确定的地方必须写成“不确定”。
            4. 必须使用 Markdown，并输出这些固定小节：## 核心判断、## 关键证据、## 主要风险、## 给 CEO 的建议。
            5. “关键证据”必须列出 3 条以内证据；“主要风险”必须列出 2 条以内风险；最后给一句可执行建议。
            6. 文字控制在 500 字以内。

            Relevant long-term memory:
            {memoryContext}

            Structured data snapshots:
            {dataSnapshotContext}
            """;
    }
}

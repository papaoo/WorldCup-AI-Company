using System.Text.Json;
using System.Text.Json.Nodes;

namespace PiPiClaw.Team;

public static class TeamContextLlmReviewService
{
    public static async Task<TeamContextLlmReviewResult> RunAsync(string objectId, int maxEvidence = 12)
    {
        var store = AppContext.WorldCupStore;
        var context = store.BuildTeamIntelligenceContextPack(objectId, maxEvidence);
        var result = new TeamContextLlmReviewResult { ContextPack = context };
        if (!context.Passed)
        {
            result.Notes.Add("Context pack is not ready for LLM review.");
            return result;
        }
        if (context.EstimatedTokens > 3500)
        {
            result.Notes.Add($"Context pack is too large for cost-controlled LLM review: {context.EstimatedTokens} estimated tokens. Reduce max_evidence or summarize first.");
            return result;
        }

        var employee = !string.IsNullOrWhiteSpace(context.EmployeeId)
            ? store.GetEmployeeById(context.EmployeeId)
            : null;
        employee ??= store.GetEmployeeById("emp_risk")
            ?? WorldCupWorkflowService.CreateFallbackEmployee("emp_risk", "风险官", "risk_officer");

        var prompt = BuildPrompt(context);
        var taskId = $"wc_team_context_review_{context.ObjectId}_{DateTime.Now:yyyyMMddHHmmss}";
        var targetUrl = string.IsNullOrWhiteSpace(AppContext.Config.MasterNodeUrl)
            ? "http://127.0.0.1:5050"
            : AppContext.Config.MasterNodeUrl;

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var (content, call) = await AppContext.LlmGateway.RunEmployeeTaskAsync(
            targetUrl,
            employee,
            prompt,
            taskId,
            employee.ModelIndex,
            timeout.Token);

        return store.SaveTeamContextLlmReview(context, employee, content, call);
    }

    private static string BuildPrompt(TeamIntelligenceContextPack context)
    {
        var compactJson = JsonSerializer.Serialize(context, AppJsonContext.Default.TeamIntelligenceContextPack);
        return $$"""
            你是世界杯 AI 公司中负责球队研究的审查员。

            任务：基于下面的压缩上下文包，写一份给 CEO 的中文球队研判。这个上下文包已经经过规则去重、证据压缩和来源评分，请不要要求读取全库。

            约束：
            1. 只基于输入 JSON，不要声称实时浏览网页。
            2. 区分事实、弱信号、推断、待核验事项。
            3. 如果证据主要来自赛程或 RSS，要明确说明不能直接支撑强预测。
            4. 不给投注建议，不承诺准确率。
            5. 输出固定章节：# 当前结论、# 关键证据、# 风险与不确定性、# 对预测模型的影响、# 下一步采集建议。
            6. 控制在 800 字以内。

            压缩上下文包 JSON：
            {{compactJson}}
            """;
    }
}

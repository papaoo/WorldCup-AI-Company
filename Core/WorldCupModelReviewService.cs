using System.Text.Json;
using System.Text.Json.Nodes;

namespace PiPiClaw.Team;

public static class WorldCupModelReviewService
{
    public static async Task<WorldCupModelReviewResult> RunAsync()
    {
        var store = AppContext.WorldCupStore;
        var audit = store.AuditWorldCupDataReadiness();
        var employee = store.GetEmployeeById("emp_risk")
            ?? WorldCupWorkflowService.CreateFallbackEmployee("emp_risk", "风险官", "risk_officer");
        var prompt = BuildPrompt(audit);
        var taskId = $"wc_model_review_{DateTime.Now:yyyyMMddHHmmss}";
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

        return store.SaveWorldCupModelReview(audit, employee, content, call);
    }

    private static string BuildPrompt(WorldCupDataReadinessAuditResult audit)
    {
        var compactAudit = JsonSerializer.Serialize(audit, AppJsonContext.Default.WorldCupDataReadinessAuditResult);
        return $$"""
            你是世界杯 AI 公司里的模型审查员和风险官。

            任务：基于下面的系统数据审计 JSON，写一份中文模型审查报告，帮助产品负责人判断当前系统是否能用于赛事分析与预测。

            严格约束：
            1. 只基于输入 JSON 进行审查，不要声称你实时访问了网页。
            2. 不要美化系统现状；必须指出数据源、预测模型、情报归因、UI 展示中的风险。
            3. 不要给投注建议，不要承诺预测准确。
            4. 输出固定章节：# 结论、# 数据源可信度、# 预测可用性、# LLM 应该承担的角色、# 必须优先修复的问题、# 下一阶段开发建议。
            5. 全文控制在 1000 字以内。

            系统数据审计 JSON：
            {{compactAudit}}
            """;
    }
}

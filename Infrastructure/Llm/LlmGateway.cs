using System.Security.Cryptography;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;

namespace PiPiClaw.Team;

public sealed class LlmGateway
{
    private const double DefaultDeepSeekChatInputUsdPerMillionTokens = 0.28;
    private const double DefaultDeepSeekChatOutputUsdPerMillionTokens = 0.42;
    private readonly HttpClient _httpClient;

    public LlmGateway(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<WorldCupModelGatewayHealthResult> CheckHealthAsync(
        string baseUrl,
        CancellationToken cancellationToken = default)
    {
        var target = string.IsNullOrWhiteSpace(baseUrl)
            ? "http://127.0.0.1:5050"
            : baseUrl.TrimEnd('/');
        var result = new WorldCupModelGatewayHealthResult
        {
            TargetUrl = target,
            CheckedEndpoint = target + "/api/agent_task",
            TokenCost = 0
        };

        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Options, result.CheckedEndpoint);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            stopwatch.Stop();
            result.StatusCode = (int)response.StatusCode;
            result.LatencyMs = stopwatch.ElapsedMilliseconds;
            result.Online = (int)response.StatusCode < 500;
            result.Message = result.Online
                ? "模型网关可连接；该检查不会触发生成，也不会消耗 token。"
                : "模型网关返回服务端错误；暂不建议触发模型审查。";
            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            result.Online = false;
            result.LatencyMs = stopwatch.ElapsedMilliseconds;
            result.Message = "模型网关不可连接；请先启动 PiPiClaw 主节点或检查 MasterNodeUrl。";
            result.Notes.Add(ex.Message);
            return result;
        }
    }

    public async Task<(string Content, LlmCallRecord Call)> RunEmployeeTaskAsync(
        string baseUrl,
        WorldCupEmployee employee,
        string prompt,
        string taskId,
        int modelIndex,
        CancellationToken cancellationToken = default)
    {
        var call = new LlmCallRecord
        {
            Id = $"llm_{taskId}",
            AgentTaskId = taskId,
            EmployeeId = employee.Id,
            ModelName = $"modelIndex:{modelIndex}",
            Provider = "PiPiClaw",
            PromptVersion = "worldcup_v0",
            RequestHash = Sha256(prompt),
            PromptTokens = EstimateTokens(prompt)
        };

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, baseUrl.TrimEnd('/') + "/api/agent_task");
            request.Headers.Add("X-Username", Uri.EscapeDataString(employee.Name));
            request.Content = new StringContent(
                JsonSerializer.Serialize(new ChatRequest
                {
                    message = prompt,
                    modelIndex = modelIndex,
                    caller = "worldcup-ceo",
                    taskId = taskId,
                    sop = "世界杯 AI 公司工作流：基于事实、基线概率和角色职责输出克制、可复盘的分析。"
                }, typeof(ChatRequest), AppJsonContext.Default),
                Encoding.UTF8,
                "application/json");

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            response.EnsureSuccessStatusCode();

            content = CleanAgentResponse(content, taskId);
            call.Status = "success";
            call.ResponseHash = Sha256(content);
            call.CompletionTokens = EstimateTokens(content);
            call.CostEstimate = EstimateDeepSeekChatCostUsd(call.PromptTokens, call.CompletionTokens);
            return (content, call);
        }
        catch (Exception ex)
        {
            call.Status = "failed";
            call.ErrorMessage = ex.Message;
            var fallback = $"[模型调用失败，已降级为占位报告]\n员工：{employee.Name}\n原因：{ex.Message}";
            call.ResponseHash = Sha256(fallback);
            call.CompletionTokens = EstimateTokens(fallback);
            call.CostEstimate = EstimateDeepSeekChatCostUsd(call.PromptTokens, call.CompletionTokens);
            return (fallback, call);
        }
    }

    private static string CleanAgentResponse(string content, string taskId)
    {
        var cleaned = content.Replace("|||END|||", "", StringComparison.OrdinalIgnoreCase).Trim();
        cleaned = Regex.Replace(cleaned, $"<done>{Regex.Escape(taskId)}</done>", "", RegexOptions.IgnoreCase).Trim();
        return cleaned;
    }

    public static string Sha256(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static int EstimateTokens(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        var cjk = 0;
        var nonCjk = 0;
        foreach (var ch in text)
        {
            if (IsCjk(ch))
            {
                cjk++;
            }
            else if (!char.IsWhiteSpace(ch))
            {
                nonCjk++;
            }
        }

        return Math.Max(1, cjk + (int)Math.Ceiling(nonCjk / 4.0));
    }

    public static double EstimateDeepSeekChatCostUsd(int promptTokens, int completionTokens)
    {
        var inputRate = ReadUsdPerMillionRate("DEEPSEEK_CHAT_INPUT_USD_PER_1M", DefaultDeepSeekChatInputUsdPerMillionTokens);
        var outputRate = ReadUsdPerMillionRate("DEEPSEEK_CHAT_OUTPUT_USD_PER_1M", DefaultDeepSeekChatOutputUsdPerMillionTokens);
        var cost = promptTokens / 1_000_000.0 * inputRate
            + completionTokens / 1_000_000.0 * outputRate;
        return Math.Round(cost, 8);
    }

    private static bool IsCjk(char ch)
    {
        return (ch >= '\u4e00' && ch <= '\u9fff')
            || (ch >= '\u3400' && ch <= '\u4dbf')
            || (ch >= '\uf900' && ch <= '\ufaff');
    }

    private static double ReadUsdPerMillionRate(string name, double fallback)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return double.TryParse(value, out var parsed) && parsed >= 0
            ? parsed
            : fallback;
    }
}

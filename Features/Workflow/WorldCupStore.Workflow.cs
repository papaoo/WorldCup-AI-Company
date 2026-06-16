using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace PiPiClaw.Team;

public sealed partial class WorldCupStore
{
    public MatchWorkflowResult RunMockMatchPredictionWorkflow(string matchId)
    {
        using var connection = OpenConnection();
        var match = GetMatch(connection, matchId) ?? throw new InvalidOperationException($"Match not found: {matchId}");
        var home = GetWatchObject(connection, match.HomeObjectId) ?? throw new InvalidOperationException($"Home team not found: {match.HomeObjectId}");
        var away = GetWatchObject(connection, match.AwayObjectId) ?? throw new InvalidOperationException($"Away team not found: {match.AwayObjectId}");
        var baseline = BaselinePredictionStrategy.Predict(match, home, away, GetPredictionSnapshots(match.Id, home.Id, away.Id));

        using var transaction = connection.BeginTransaction();
        UpsertBaselinePrediction(connection, baseline);

        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        var workflow = new WorkflowRunRecord
        {
            Id = $"workflow_{match.Id}_mock_prediction",
            WorkflowType = "match_prediction",
            Status = "completed",
            MatchId = match.Id,
            StartedBy = "worldcup-harness",
            StartedAt = now,
            CompletedAt = now,
            MetadataJson = $$"""{"mode":"mock","baseline_prediction_id":"{{baseline.Id}}"}"""
        };
        UpsertWorkflowRun(connection, transaction, workflow);

        var reportMarkdown = BuildMockReport(match, home, away, baseline);
        var artifact = SaveArtifact(connection, transaction, new ArtifactRecord
        {
            Id = $"artifact_{workflow.Id}_report",
            Type = "markdown",
            Title = $"{home.DisplayName} vs {away.DisplayName} \u8d5b\u524d\u9884\u6d4b\u62a5\u544a",
            WorkflowRunId = workflow.Id,
            FilePath = Path.Combine("artifacts", $"{workflow.Id}.md"),
            Summary = $"\u57fa\u7ebf\u9884\u6d4b\uff1a{home.DisplayName}\u80dc {baseline.HomeWinProbability:P1}\uff0c\u5e73 {baseline.DrawProbability:P1}\uff0c{away.DisplayName}\u80dc {baseline.AwayWinProbability:P1}",
            MetadataJson = $$"""{"match_id":"{{match.Id}}","baseline_prediction_id":"{{baseline.Id}}"}"""
        }, reportMarkdown);

        var steps = new List<WorkflowStepRecord>
        {
            MakeCompletedStep(workflow.Id, "team_report_home", FindPrimaryEmployeeId(connection, home.Id), artifact.Id, $$"""{"team":"{{home.DisplayName}}","summary":"mock home team report"}"""),
            MakeCompletedStep(workflow.Id, "team_report_away", FindPrimaryEmployeeId(connection, away.Id), artifact.Id, $$"""{"team":"{{away.DisplayName}}","summary":"mock away team report"}"""),
            MakeCompletedStep(workflow.Id, "data_analysis", "emp_data", artifact.Id, $$"""{"baseline_prediction_id":"{{baseline.Id}}"}"""),
            MakeCompletedStep(workflow.Id, "risk_review", "emp_risk", artifact.Id, """{"risk":"mock risk review completed"}"""),
            MakeCompletedStep(workflow.Id, "ceo_summary", "emp_ceo", artifact.Id, """{"decision":"mock CEO summary completed"}""")
        };

        foreach (var step in steps)
        {
            UpsertWorkflowStep(connection, transaction, step);
            SaveSystemEventLog(connection, transaction, new WorldCupSystemEventLog
            {
                EventType = "workflow_step_completed",
                Category = "employee",
                Source = "mock",
                EmployeeId = step.AssigneeEmployeeId,
                MatchId = match.Id,
                WorkflowRunId = workflow.Id,
                ArtifactId = artifact.Id,
                Title = $"Workflow step {step.StepType}",
                Message = $"{step.StepType} completed by {step.AssigneeEmployeeId ?? "unassigned"} with status {step.Status}.",
                PayloadJson = new JsonObject
                {
                    ["step_id"] = step.Id,
                    ["step_type"] = step.StepType,
                    ["status"] = step.Status,
                    ["output_json"] = step.OutputJson
                }.ToJsonString()
            });
        }

        SaveSystemEventLog(connection, transaction, new WorldCupSystemEventLog
        {
            EventType = "workflow_completed",
            Category = "workflow",
            Source = "mock",
            MatchId = match.Id,
            WorkflowRunId = workflow.Id,
            ArtifactId = artifact.Id,
            Title = "Mock match prediction workflow completed",
            Message = $"{workflow.Id} completed with {steps.Count} steps.",
            PayloadJson = new JsonObject
            {
                ["workflow_type"] = workflow.WorkflowType,
                ["status"] = workflow.Status,
                ["step_count"] = steps.Count,
                ["artifact_id"] = artifact.Id,
                ["baseline_prediction_id"] = baseline.Id
            }.ToJsonString()
        });

        transaction.Commit();
        return new MatchWorkflowResult
        {
            WorkflowRun = workflow,
            Steps = steps,
            Artifact = artifact,
            BaselinePrediction = baseline
        };
    }

    public MatchWorkflowResult SaveMatchPredictionWorkflow(string matchId, IReadOnlyList<StepOutputDraft> outputs, string mode, IReadOnlyList<LlmCallRecord>? llmCalls = null)
    {
        using var connection = OpenConnection();
        var match = GetMatch(connection, matchId) ?? throw new InvalidOperationException($"Match not found: {matchId}");
        var home = GetWatchObject(connection, match.HomeObjectId) ?? throw new InvalidOperationException($"Home team not found: {match.HomeObjectId}");
        var away = GetWatchObject(connection, match.AwayObjectId) ?? throw new InvalidOperationException($"Away team not found: {match.AwayObjectId}");
        var baseline = BaselinePredictionStrategy.Predict(match, home, away, GetPredictionSnapshots(match.Id, home.Id, away.Id));

        using var transaction = connection.BeginTransaction();
        UpsertBaselinePrediction(connection, baseline);

        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        var expectedStepCount = mode == "llm" ? 5 : outputs.Count;
        var hasStepFailure = outputs.Any(output => output.Source != "llm" && output.Source != "mock");
        var missingSteps = outputs.Count < expectedStepCount;
        var workflowStatus = hasStepFailure || missingSteps ? "needs_review" : "completed";
        var workflow = new WorkflowRunRecord
        {
            Id = $"workflow_{match.Id}_{mode}_prediction",
            WorkflowType = "match_prediction",
            Status = workflowStatus,
            MatchId = match.Id,
            StartedBy = mode,
            StartedAt = now,
            CompletedAt = now,
            ErrorMessage = workflowStatus == "completed" ? null : "One or more workflow steps used fallback output or were missing.",
            MetadataJson = $$"""{"mode":"{{mode}}","baseline_prediction_id":"{{baseline.Id}}"}"""
        };
        UpsertWorkflowRun(connection, transaction, workflow);

        var evidenceSnapshots = GetDataSnapshots(match.Id)
            .OrderByDescending(snapshot => snapshot.CapturedAt)
            .Take(8)
            .ToList();
        var reportMarkdown = BuildReport(match, home, away, baseline, outputs, evidenceSnapshots);
        var artifact = SaveArtifact(connection, transaction, new ArtifactRecord
        {
            Id = $"artifact_{workflow.Id}_report",
            Type = "markdown",
            Title = $"{home.DisplayName} vs {away.DisplayName} \u8d5b\u524d\u9884\u6d4b\u62a5\u544a",
            WorkflowRunId = workflow.Id,
            FilePath = Path.Combine("artifacts", $"{workflow.Id}.md"),
            Summary = $"\u57fa\u7ebf\u9884\u6d4b\uff1a{home.DisplayName}\u80dc {baseline.HomeWinProbability:P1}\uff0c\u5e73 {baseline.DrawProbability:P1}\uff0c{away.DisplayName}\u80dc {baseline.AwayWinProbability:P1}",
            MetadataJson = $$"""{"match_id":"{{match.Id}}","baseline_prediction_id":"{{baseline.Id}}","mode":"{{mode}}"}"""
        }, reportMarkdown);

        var steps = outputs.Select(output =>
        {
            var step = MakeCompletedStep(
                workflow.Id,
                output.StepType,
                output.EmployeeId,
                artifact.Id,
                $$"""{"source":"{{EscapeJson(output.Source)}}","content":"{{EscapeJson(output.Content)}}","llm_call_id":"{{EscapeJson(output.LlmCallId ?? "")}}"}""");
            if (output.Source != "llm" && output.Source != "mock")
            {
                step.Status = "needs_review";
                step.ErrorMessage = "Step used fallback output because the LLM call did not complete successfully.";
            }
            return step;
        }).ToList();

        foreach (var step in steps)
        {
            UpsertWorkflowStep(connection, transaction, step);
            SaveSystemEventLog(connection, transaction, new WorldCupSystemEventLog
            {
                EventType = "workflow_step_completed",
                Category = "employee",
                Severity = step.Status == "completed" ? "info" : "warning",
                Source = mode,
                EmployeeId = step.AssigneeEmployeeId,
                MatchId = match.Id,
                WorkflowRunId = workflow.Id,
                ArtifactId = artifact.Id,
                Title = $"Workflow step {step.StepType}",
                Message = $"{step.StepType} completed by {step.AssigneeEmployeeId ?? "unassigned"} with status {step.Status}.",
                PayloadJson = new JsonObject
                {
                    ["step_id"] = step.Id,
                    ["step_type"] = step.StepType,
                    ["status"] = step.Status,
                    ["input_json"] = step.InputJson,
                    ["output_json"] = step.OutputJson,
                    ["error_message"] = step.ErrorMessage
                }.ToJsonString()
            });
        }

        if (llmCalls != null)
        {
            foreach (var llmCall in llmCalls)
            {
                UpsertLlmCall(connection, transaction, llmCall);
                SaveSystemEventLog(connection, transaction, new WorldCupSystemEventLog
                {
                    EventType = "llm_call_saved",
                    Category = "llm",
                    Severity = llmCall.Status == "success" ? "info" : "warning",
                    Source = llmCall.Provider,
                    EmployeeId = llmCall.EmployeeId,
                    MatchId = match.Id,
                    WorkflowRunId = workflow.Id,
                    LlmCallId = llmCall.Id,
                    Title = "LLM call recorded",
                    Message = $"{llmCall.EmployeeId ?? "unknown"} call {llmCall.Status}; prompt={llmCall.PromptTokens}, completion={llmCall.CompletionTokens}.",
                    PayloadJson = new JsonObject
                    {
                        ["agent_task_id"] = llmCall.AgentTaskId,
                        ["model_name"] = llmCall.ModelName,
                        ["prompt_version"] = llmCall.PromptVersion,
                        ["prompt_tokens"] = llmCall.PromptTokens,
                        ["completion_tokens"] = llmCall.CompletionTokens,
                        ["cost_estimate"] = llmCall.CostEstimate,
                        ["request_hash"] = llmCall.RequestHash,
                        ["response_hash"] = llmCall.ResponseHash,
                        ["status"] = llmCall.Status,
                        ["error_message"] = llmCall.ErrorMessage
                    }.ToJsonString()
                });
            }
        }

        SaveSystemEventLog(connection, transaction, new WorldCupSystemEventLog
        {
            EventType = "workflow_completed",
            Category = "workflow",
            Severity = workflow.Status == "completed" ? "info" : "warning",
            Source = mode,
            MatchId = match.Id,
            WorkflowRunId = workflow.Id,
            ArtifactId = artifact.Id,
            Title = "Match prediction workflow completed",
            Message = $"{workflow.Id} completed with status {workflow.Status}.",
            PayloadJson = new JsonObject
            {
                ["workflow_type"] = workflow.WorkflowType,
                ["status"] = workflow.Status,
                ["step_count"] = steps.Count,
                ["artifact_id"] = artifact.Id,
                ["baseline_prediction_id"] = baseline.Id,
                ["metadata_json"] = workflow.MetadataJson
            }.ToJsonString()
        });

        transaction.Commit();
        return new MatchWorkflowResult
        {
            WorkflowRun = workflow,
            Steps = steps,
            Artifact = artifact,
            BaselinePrediction = baseline
        };
    }

    public void SaveLlmCall(LlmCallRecord item)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        UpsertLlmCall(connection, transaction, item);
        transaction.Commit();
    }

    private static void UpsertLlmCall(SqliteConnection connection, SqliteTransaction transaction, LlmCallRecord item)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO llm_calls (id, agent_task_id, employee_id, model_name, provider, prompt_version,
                prompt_tokens, completion_tokens, cost_estimate, request_hash, response_hash, status, error_message, created_at)
            VALUES ($id, $agent_task_id, $employee_id, $model_name, $provider, $prompt_version,
                $prompt_tokens, $completion_tokens, $cost_estimate, $request_hash, $response_hash, $status, $error_message, $created_at)
            ON CONFLICT(id) DO UPDATE SET
                agent_task_id = excluded.agent_task_id,
                employee_id = excluded.employee_id,
                model_name = excluded.model_name,
                provider = excluded.provider,
                prompt_version = excluded.prompt_version,
                prompt_tokens = excluded.prompt_tokens,
                completion_tokens = excluded.completion_tokens,
                cost_estimate = excluded.cost_estimate,
                request_hash = excluded.request_hash,
                response_hash = excluded.response_hash,
                status = excluded.status,
                error_message = excluded.error_message,
                created_at = excluded.created_at
            """;
        Add(command, "$id", item.Id);
        Add(command, "$agent_task_id", item.AgentTaskId);
        Add(command, "$employee_id", item.EmployeeId);
        Add(command, "$model_name", item.ModelName);
        Add(command, "$provider", item.Provider);
        Add(command, "$prompt_version", item.PromptVersion);
        Add(command, "$prompt_tokens", item.PromptTokens);
        Add(command, "$completion_tokens", item.CompletionTokens);
        Add(command, "$cost_estimate", item.CostEstimate);
        Add(command, "$request_hash", item.RequestHash);
        Add(command, "$response_hash", item.ResponseHash);
        Add(command, "$status", item.Status);
        Add(command, "$error_message", item.ErrorMessage);
        Add(command, "$created_at", item.CreatedAt);
        command.ExecuteNonQuery();
    }

    public WorkflowHarnessResult RunWorkflowHarness(string? matchId = null)
    {
        var targetMatchId = string.IsNullOrWhiteSpace(matchId)
            ? GetMatches().FirstOrDefault()?.Id
            : matchId;

        var result = new WorkflowHarnessResult { MatchId = targetMatchId ?? "" };
        if (string.IsNullOrWhiteSpace(targetMatchId))
        {
            result.Notes.Add("No matches available. Run /api/worldcup/seed first.");
            return result;
        }

        var workflow = RunMockMatchPredictionWorkflow(targetMatchId);
        result.WorkflowId = workflow.WorkflowRun.Id;
        result.StepCount = workflow.Steps.Count;
        result.ArtifactCreated = !string.IsNullOrWhiteSpace(workflow.Artifact.FilePath) && File.Exists(Path.Combine(System.AppContext.BaseDirectory, workflow.Artifact.FilePath));
        result.BaselinePredictionFound = !string.IsNullOrWhiteSpace(workflow.BaselinePrediction.Id);
        result.WorkflowCompleted = workflow.WorkflowRun.Status == "completed";

        if (result.StepCount != 5) result.Notes.Add($"Expected 5 steps, got {result.StepCount}.");
        if (!result.ArtifactCreated) result.Notes.Add("Artifact file was not created.");
        if (!result.BaselinePredictionFound) result.Notes.Add("Baseline prediction missing.");
        if (!result.WorkflowCompleted) result.Notes.Add("Workflow did not complete.");

        result.Passed = result.StepCount == 5 && result.ArtifactCreated && result.BaselinePredictionFound && result.WorkflowCompleted;
        return result;
    }

    public ReportQualityHarnessResult RunReportQualityHarness()
    {
        SeedDemoWorldCupCompany();
        SeedDemoDataSnapshots();
        var outputs = new List<StepOutputDraft>
        {
            new() { StepType = "team_report_home", EmployeeId = "emp_arg", Source = "mock", Content = "## 核心判断\n阿根廷具备控球和前场压迫优势。\n## 关键证据\n- 排名与稳定性更好。\n- 进攻端选择更多。\n## 主要风险\n- 热门压力可能放大失误。\n## 给 CEO 的建议\n维持谨慎看好。" },
            new() { StepType = "team_report_away", EmployeeId = "emp_jpn", Source = "mock", Content = "## 核心判断\n日本需要依靠转换速度制造威胁。\n## 关键证据\n- 反击效率是主要变量。\n## 主要风险\n- 控球时间不足会增加防守压力。\n## 给 CEO 的建议\n关注爆冷保护。" },
            new() { StepType = "data_analysis", EmployeeId = "emp_data", Source = "mock", Content = "## 核心判断\n快照感知策略倾向主队。\n## 关键证据\n- snapshot_aware_v1 已给出概率分布。\n## 主要风险\n- 样本有限。\n## 给 CEO 的建议\n不要把概率解释为确定性。" },
            new() { StepType = "risk_review", EmployeeId = "emp_risk", Source = "mock", Content = "## 核心判断\n最大风险来自临场阵容和红牌事件。\n## 关键证据\n- 淘汰赛偶然性更高。\n## 主要风险\n- 伤病、轮换、天气。\n## 给 CEO 的建议\n赛前二次复核。" },
            new() { StepType = "ceo_summary", EmployeeId = "emp_ceo", Source = "mock", Content = "CEO 结论：以主胜为第一判断，但保留爆冷风险；报告只用于赛前研究，不构成投注建议。" }
        };

        var workflow = SaveMatchPredictionWorkflow("match_arg_jpn", outputs, "quality");
        var content = GetArtifactContent(workflow.Artifact.Id)?.Content ?? "";
        var result = new ReportQualityHarnessResult
        {
            WorkflowId = workflow.WorkflowRun.Id,
            ArtifactId = workflow.Artifact.Id,
            ContainsExecutiveSummary = content.Contains("## 执行摘要", StringComparison.Ordinal),
            ContainsProbabilitySection = content.Contains("## 客观概率", StringComparison.Ordinal) && content.Contains(BaselinePredictionStrategy.Version, StringComparison.Ordinal),
            ContainsEvidenceSection = content.Contains("## 关键证据", StringComparison.Ordinal),
            ContainsEvidenceTrace = content.Contains("## 证据链引用", StringComparison.Ordinal)
                && content.Contains("snapshot_", StringComparison.Ordinal)
                && content.Contains("source `", StringComparison.Ordinal)
                && content.Contains("hash `", StringComparison.Ordinal),
            ContainsRiskSection = content.Contains("## 主要风险", StringComparison.Ordinal),
            ContainsEmployeeSections = content.Contains("## 员工协作输出", StringComparison.Ordinal)
                && content.Contains("### 主队研究员", StringComparison.Ordinal)
                && content.Contains("### 数据分析师", StringComparison.Ordinal)
                && content.Contains("### 风险官", StringComparison.Ordinal),
            ContainsCeoConclusion = content.Contains("## CEO 结论", StringComparison.Ordinal)
                && content.Contains("CEO 结论", StringComparison.Ordinal),
            ContainsDisclaimer = content.Contains("不构成投注建议", StringComparison.Ordinal)
                || content.Contains("不提供投注", StringComparison.Ordinal)
        };

        if (!result.ContainsExecutiveSummary) result.Notes.Add("Report missing executive summary.");
        if (!result.ContainsProbabilitySection) result.Notes.Add("Report missing objective probability section.");
        if (!result.ContainsEvidenceSection) result.Notes.Add("Report missing evidence section.");
        if (!result.ContainsEvidenceTrace) result.Notes.Add("Report missing structured evidence trace references.");
        if (!result.ContainsRiskSection) result.Notes.Add("Report missing risk section.");
        if (!result.ContainsEmployeeSections) result.Notes.Add("Report missing employee collaboration sections.");
        if (!result.ContainsCeoConclusion) result.Notes.Add("Report missing CEO conclusion.");
        if (!result.ContainsDisclaimer) result.Notes.Add("Report missing non-betting disclaimer.");
        result.Passed = result.ContainsExecutiveSummary
            && result.ContainsProbabilitySection
            && result.ContainsEvidenceSection
            && result.ContainsEvidenceTrace
            && result.ContainsRiskSection
            && result.ContainsEmployeeSections
            && result.ContainsCeoConclusion
            && result.ContainsDisclaimer;
        return result;
    }

    public EngineeringGuardrailHarnessResult RunEngineeringGuardrailHarness()
    {
        SeedDemoWorldCupCompany();
        var result = new EngineeringGuardrailHarnessResult();

        using (var connection = OpenConnection())
        {
            using var foreignKeyCommand = connection.CreateCommand();
            foreignKeyCommand.CommandText = "PRAGMA foreign_keys;";
            result.ForeignKeysEnabled = Convert.ToInt32(foreignKeyCommand.ExecuteScalar()) == 1;
            var indexes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using var indexCommand = connection.CreateCommand();
            indexCommand.CommandText = "SELECT name FROM sqlite_master WHERE type = 'index'";
            using var reader = indexCommand.ExecuteReader();
            while (reader.Read())
            {
                indexes.Add(reader.GetString(0));
            }

            result.RequiredIndexesPresent = indexes.Contains("idx_matches_home")
                && indexes.Contains("idx_matches_away")
                && indexes.Contains("idx_baseline_predictions_match");
        }

        try
        {
            RecordMatchResult(new MatchResultRequest
            {
                MatchId = "match_arg_jpn",
                HomeScore = -1,
                AwayScore = 0
            });
        }
        catch (ArgumentException)
        {
            result.InvalidScoreRejected = true;
        }

        var outputs = new List<StepOutputDraft>
        {
            new() { StepType = "team_report_home", EmployeeId = "emp_arg", Source = "llm", Content = "home report" },
            new() { StepType = "team_report_away", EmployeeId = "emp_jpn", Source = "fallback", Content = "fallback away report", LlmCallId = "llm_guardrail_away" },
            new() { StepType = "data_analysis", EmployeeId = "emp_data", Source = "llm", Content = "data analysis" },
            new() { StepType = "risk_review", EmployeeId = "emp_risk", Source = "llm", Content = "risk review" },
            new() { StepType = "ceo_summary", EmployeeId = "emp_ceo", Source = "llm", Content = "ceo summary" }
        };
        var llmCalls = new List<LlmCallRecord>
        {
            new()
            {
                Id = "llm_guardrail_away",
                AgentTaskId = "guardrail_team_report_away",
                EmployeeId = "emp_jpn",
                ModelName = "guardrail-model",
                Provider = "harness",
                PromptVersion = "worldcup_guardrail_v1",
                PromptTokens = 12,
                CompletionTokens = 8,
                CostEstimate = LlmGateway.EstimateDeepSeekChatCostUsd(12, 8),
                Status = "error",
                ErrorMessage = "Synthetic fallback call for guardrail harness."
            }
        };
        var workflow = SaveMatchPredictionWorkflow("match_arg_jpn", outputs, "llm_guardrail", llmCalls);
        result.FallbackWorkflowNeedsReview = workflow.WorkflowRun.Status == "needs_review";
        result.FallbackStepNeedsReview = workflow.Steps.Any(step => step.StepType == "team_report_away" && step.Status == "needs_review");
        result.LlmCallsSavedWithWorkflow = CountLlmCallsByIds(llmCalls.Select(call => call.Id)) == llmCalls.Count;
        result.StepLlmCallRefsValid = workflow.Steps.Any(step =>
            step.StepType == "team_report_away"
            && step.OutputJson.Contains("llm_guardrail_away", StringComparison.Ordinal));
        var samplePromptTokens = LlmGateway.EstimateTokens("阿根廷 vs 日本 tactical preview with risk review");
        var sampleCompletionTokens = LlmGateway.EstimateTokens("核心判断：阿根廷略占优势，但需要防范转换进攻。");
        var sampleCost = LlmGateway.EstimateDeepSeekChatCostUsd(samplePromptTokens, sampleCompletionTokens);
        result.TokenEstimateValid = samplePromptTokens > 0 && sampleCompletionTokens > 0;
        result.CostEstimateValid = sampleCost > 0 && llmCalls.All(call => call.CostEstimate > 0);

        AddWorkflowMemories(workflow, outputs);
        var memories = GetMemories();
        result.TeamReportMemoryExpires = memories.Any(memory =>
            memory.SourceId == workflow.WorkflowRun.Id
            && memory.Summary.Contains("team_report_home", StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(memory.ExpiresAt));
        result.RiskMemoryPersistent = memories.Any(memory =>
            memory.SourceId == workflow.WorkflowRun.Id
            && memory.Summary.Contains("risk_review", StringComparison.Ordinal)
            && string.IsNullOrWhiteSpace(memory.ExpiresAt));

        if (!result.ForeignKeysEnabled) result.Notes.Add("SQLite foreign_keys pragma is not enabled.");
        if (!result.RequiredIndexesPresent) result.Notes.Add("One or more required indexes are missing.");
        if (!result.InvalidScoreRejected) result.Notes.Add("Invalid match score was not rejected.");
        if (!result.FallbackWorkflowNeedsReview) result.Notes.Add("Fallback workflow was not marked needs_review.");
        if (!result.FallbackStepNeedsReview) result.Notes.Add("Fallback step was not marked needs_review.");
        if (!result.TeamReportMemoryExpires) result.Notes.Add("Short-lived team report memory has no expiry.");
        if (!result.RiskMemoryPersistent) result.Notes.Add("Risk review memory should remain persistent.");
        if (!result.LlmCallsSavedWithWorkflow) result.Notes.Add("LLM calls were not saved with workflow transaction.");
        if (!result.StepLlmCallRefsValid) result.Notes.Add("Workflow step does not reference its LLM call id.");
        if (!result.TokenEstimateValid) result.Notes.Add("Token estimate is invalid.");
        if (!result.CostEstimateValid) result.Notes.Add("Cost estimate is invalid.");

        result.Passed = result.Notes.Count == 0;
        return result;
    }
}

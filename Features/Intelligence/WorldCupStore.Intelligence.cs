using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace PiPiClaw.Team;

public sealed partial class WorldCupStore
{
    private static readonly string[] InjuryKeywords = ["injury", "injured", "hamstring", "doubtful", "fitness", "sideline", "sidelined", "伤病", "受伤", "缺阵"];
    private static readonly string[] LineupKeywords = ["lineup", "squad", "roster", "starting xi", "starter", "selection", "captain", "名单", "首发", "阵容"];
    private static readonly string[] WorldCupKeywords = ["world cup", "fifa", "2026", "usmnt", "argentina", "brazil", "england", "france", "germany", "spain", "世界杯"];
    private const int MaxNewsArticleTeamAssignments = 3;

    public List<IntelligenceSignalRecord> GetIntelligenceSignals(
        string? status = null,
        string? objectId = null,
        string? matchId = null,
        string? signalType = null,
        int limit = 200)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        var filters = new List<string> { "1 = 1" };
        if (!string.IsNullOrWhiteSpace(status))
        {
            filters.Add("status = $status");
            Add(command, "$status", status);
        }
        if (!string.IsNullOrWhiteSpace(objectId))
        {
            filters.Add("object_id = $object_id");
            Add(command, "$object_id", objectId);
        }
        if (!string.IsNullOrWhiteSpace(matchId))
        {
            filters.Add("match_id = $match_id");
            Add(command, "$match_id", matchId);
        }
        if (!string.IsNullOrWhiteSpace(signalType))
        {
            filters.Add("signal_type = $signal_type");
            Add(command, "$signal_type", signalType);
        }

        command.CommandText = $"""
            SELECT id, source_snapshot_id, signal_type, severity, confidence, object_id, match_id,
                   title, summary, evidence_json, status, content_hash, created_at, updated_at
            FROM intelligence_signals
            WHERE {string.Join(" AND ", filters)}
            ORDER BY created_at DESC, id DESC
            LIMIT $limit
            """;
        Add(command, "$limit", Math.Clamp(limit, 1, 1000));
        using var reader = command.ExecuteReader();
        var rows = new List<IntelligenceSignalRecord>();
        while (reader.Read())
        {
            rows.Add(ReadIntelligenceSignal(reader));
        }
        return rows;
    }

    public IntelligenceTriageResult RunIntelligenceTriage(int snapshotLimit = 500, bool includeTestData = false)
    {
        var snapshots = (includeTestData ? GetDataSnapshots() : GetProductionDataSnapshots()).Take(Math.Clamp(snapshotLimit, 1, 2000)).ToList();
        var teams = (includeTestData ? GetWatchObjects() : GetProductionWatchObjects()).Where(item => item.Type == "football_team").ToList();
        var result = new IntelligenceTriageResult { SnapshotsChecked = snapshots.Count };
        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        var workflow = new WorkflowRunRecord
        {
            Id = $"workflow_intelligence_triage_{DateTime.Now:yyyyMMddHHmmssfff}",
            WorkflowType = "intelligence_triage",
            Status = "completed",
            StartedBy = "triage",
            StartedAt = now,
            CompletedAt = now,
            MetadataJson = new JsonObject
            {
                ["snapshot_limit"] = Math.Clamp(snapshotLimit, 1, 2000),
                ["snapshot_count"] = snapshots.Count
            }.ToJsonString()
        };

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        UpsertWorkflowRun(connection, transaction, workflow);
        foreach (var snapshot in snapshots)
        {
            foreach (var signal in BuildSignalsFromSnapshot(snapshot, teams))
            {
                if (FindIntelligenceSignalByHash(connection, transaction, signal.ContentHash) != null)
                {
                    result.DuplicatesSkipped++;
                    continue;
                }
                SaveIntelligenceSignal(connection, transaction, signal);
                result.SignalsCreated++;
                result.Signals.Add(signal);
                if (signal.Status == "needs_ai_review") result.NeedsAiReview++;
                SaveSystemEventLog(connection, transaction, new WorldCupSystemEventLog
                {
                    EventType = "intelligence_signal_created",
                    Category = "intelligence",
                    Severity = signal.Severity == "high" ? "warning" : "info",
                    Source = "triage",
                    ObjectId = signal.ObjectId,
                    MatchId = signal.MatchId,
                    WorkflowRunId = workflow.Id,
                    SnapshotId = signal.SourceSnapshotId,
                    Title = signal.Title,
                    Message = signal.Summary,
                    PayloadJson = JsonSerializer.Serialize(signal, AppJsonContext.Default.IntelligenceSignalRecord)
                });
            }
        }
        workflow.Status = snapshots.Count > 0 ? "completed" : "needs_review";
        workflow.ErrorMessage = snapshots.Count > 0 ? null : "No snapshots were available for intelligence triage.";
        UpsertWorkflowRun(connection, transaction, workflow);
        var step = new WorkflowStepRecord
        {
            Id = $"step_{workflow.Id}_intelligence_triage",
            WorkflowRunId = workflow.Id,
            StepType = "intelligence_triage",
            Status = workflow.Status,
            StartedAt = now,
            CompletedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            InputJson = new JsonObject
            {
                ["snapshot_limit"] = Math.Clamp(snapshotLimit, 1, 2000),
                ["snapshots_checked"] = snapshots.Count,
                ["snapshot_ids"] = JsonSerializer.SerializeToNode(snapshots.Take(80).Select(snapshot => snapshot.Id).ToList(), AppJsonContext.Default.ListString)
            }.ToJsonString(),
            OutputJson = new JsonObject
            {
                ["signals_created"] = result.SignalsCreated,
                ["duplicates_skipped"] = result.DuplicatesSkipped,
                ["needs_ai_review"] = result.NeedsAiReview,
                ["signal_ids"] = JsonSerializer.SerializeToNode(result.Signals.Select(signal => signal.Id).ToList(), AppJsonContext.Default.ListString)
            }.ToJsonString(),
            ErrorMessage = workflow.ErrorMessage
        };
        UpsertWorkflowStep(connection, transaction, step);
        transaction.Commit();
        ApproveNonActionablePendingSignals();

        result.Passed = result.SnapshotsChecked > 0;
        if (result.SnapshotsChecked == 0) result.Notes.Add("No snapshots were available for intelligence triage.");
        if (result.SignalsCreated == 0) result.Notes.Add("No new signals were created; existing signals may already cover current snapshots.");
        return result;
    }

    public EmployeeReportTriggerResult TriggerEmployeeReportsFromSignals(int maxTeams = 8, bool includeTestData = false)
    {
        ApproveNonActionablePendingSignals();
        var signals = (includeTestData
                ? GetIntelligenceSignals(status: "needs_ai_review", limit: 500)
                : GetProductionIntelligenceSignals(status: "needs_ai_review", limit: 500))
            .Where(signal => !string.IsNullOrWhiteSpace(signal.ObjectId))
            .Where(IsActionableForLlm)
            .ToList();
        var result = new EmployeeReportTriggerResult { SignalsConsidered = signals.Count };
        var grouped = signals
            .GroupBy(signal => signal.ObjectId!)
            .OrderByDescending(group => group.Max(signal => SignalSeverityScore(signal.Severity)))
            .ThenByDescending(group => group.Count())
            .Take(Math.Clamp(maxTeams, 1, 48))
            .ToList();

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        foreach (var group in grouped)
        {
            var team = GetWatchObject(connection, group.Key);
            if (team == null)
            {
                result.Notes.Add($"Team not found for signal object: {group.Key}");
                continue;
            }
            var employeeId = FindPrimaryEmployeeId(connection, team.Id);
            var employee = string.IsNullOrWhiteSpace(employeeId) ? null : GetEmployee(connection, employeeId);
            var reportSignals = group.Take(8).ToList();
            var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            var workflow = new WorkflowRunRecord
            {
                Id = $"workflow_{team.Id}_intelligence_report_{DateTime.Now:yyyyMMddHHmmss}",
                WorkflowType = "team_intelligence_report",
                Status = "completed",
                ObjectId = team.Id,
                StartedBy = "signal_trigger",
                StartedAt = now,
                CompletedAt = now,
                MetadataJson = new JsonObject
                {
                    ["signal_count"] = reportSignals.Count,
                    ["mode"] = "structured_no_llm"
                }.ToJsonString()
            };
            UpsertWorkflowRun(connection, transaction, workflow);

            var content = BuildTeamIntelligenceReport(team, employee, reportSignals);
            var artifact = SaveArtifact(connection, transaction, new ArtifactRecord
            {
                Id = $"artifact_{workflow.Id}",
                Type = "markdown",
                Title = $"{team.DisplayName} intelligence brief",
                OwnerEmployeeId = employee?.Id,
                ObjectId = team.Id,
                WorkflowRunId = workflow.Id,
                FilePath = Path.Combine("artifacts", $"{workflow.Id}.md"),
                Summary = $"{team.DisplayName}: {reportSignals.Count} intelligence signals require review.",
                MetadataJson = new JsonObject
                {
                    ["object_id"] = team.Id,
                    ["employee_id"] = employee?.Id,
                    ["signal_ids"] = JsonSerializer.SerializeToNode(reportSignals.Select(signal => signal.Id).ToList(), AppJsonContext.Default.ListString)
                }.ToJsonString()
            }, content);
            result.Artifacts.Add(artifact);
            result.ReportsCreated++;
            result.TeamsTriggered++;

            var step = MakeCompletedStep(
                workflow.Id,
                "team_intelligence_report",
                employee?.Id,
                artifact.Id,
                new JsonObject
                {
                    ["source"] = "structured_no_llm",
                    ["artifact_id"] = artifact.Id,
                    ["summary"] = artifact.Summary,
                    ["signal_count"] = reportSignals.Count,
                    ["signal_ids"] = JsonSerializer.SerializeToNode(reportSignals.Select(signal => signal.Id).ToList(), AppJsonContext.Default.ListString)
                }.ToJsonString());
            var signalInputArray = new JsonArray();
            foreach (var signal in reportSignals)
            {
                signalInputArray.Add((JsonNode)new JsonObject
                {
                    ["id"] = signal.Id,
                    ["type"] = signal.SignalType,
                    ["severity"] = signal.Severity,
                    ["summary"] = signal.Summary
                });
            }
            step.InputJson = new JsonObject
            {
                ["object_id"] = team.Id,
                ["employee_id"] = employee?.Id,
                ["mode"] = "structured_no_llm",
                ["signal_count"] = reportSignals.Count,
                ["signals"] = signalInputArray
            }.ToJsonString();
            UpsertWorkflowStep(connection, transaction, step);

            foreach (var signal in reportSignals)
            {
                UpdateIntelligenceSignalStatus(connection, transaction, signal.Id, "report_triggered");
            }

            SaveSystemEventLog(connection, transaction, new WorldCupSystemEventLog
            {
                EventType = "employee_report_triggered",
                Category = "employee",
                Source = "signal_trigger",
                EmployeeId = employee?.Id,
                ObjectId = team.Id,
                WorkflowRunId = workflow.Id,
                ArtifactId = artifact.Id,
                Title = $"{team.DisplayName} intelligence brief triggered",
                Message = $"{employee?.Name ?? "Primary researcher"} created a structured brief from {reportSignals.Count} signals.",
                PayloadJson = new JsonObject
                {
                    ["signal_count"] = reportSignals.Count,
                    ["signal_ids"] = JsonSerializer.SerializeToNode(reportSignals.Select(signal => signal.Id).ToList(), AppJsonContext.Default.ListString),
                    ["step_id"] = step.Id,
                    ["step_type"] = step.StepType,
                    ["mode"] = "structured_no_llm"
                }.ToJsonString()
            });
        }
        transaction.Commit();

        result.Passed = result.SignalsConsidered == 0 || result.ReportsCreated > 0;
        if (result.SignalsConsidered == 0) result.Notes.Add("No signals needed employee report triggering.");
        return result;
    }

    public EmployeeReportBudgetEstimate EstimateEmployeeReportBudget(int maxTeams = 8)
    {
        var signals = GetIntelligenceSignals(status: "needs_ai_review", limit: 500)
            .Where(signal => !string.IsNullOrWhiteSpace(signal.ObjectId))
            .Where(IsActionableForLlm)
            .ToList();
        var groups = signals
            .GroupBy(signal => signal.ObjectId!)
            .OrderByDescending(group => group.Max(signal => SignalSeverityScore(signal.Severity)))
            .ThenByDescending(group => group.Count())
            .Take(Math.Clamp(maxTeams, 1, 48))
            .ToList();

        var result = new EmployeeReportBudgetEstimate
        {
            SignalsConsidered = signals.Count,
            TeamsConsidered = groups.Count,
            MaxTeams = Math.Clamp(maxTeams, 1, 48)
        };

        foreach (var group in groups)
        {
            var team = GetWatchObjectById(group.Key);
            if (team == null)
            {
                result.Notes.Add($"Team not found for signal object: {group.Key}");
                continue;
            }

            var employee = GetEmployeeById(GetPrimaryEmployeeIdForObject(team.Id) ?? "");
            var structuredContext = BuildTeamIntelligenceReport(team, employee, group.Take(8).ToList());
            var prompt = $"""
                You are the assigned football research employee for {team.DisplayName}.
                Convert the structured intelligence brief into a concise Chinese Markdown report for the CEO.
                Keep facts, inference, uncertainty, and next action clearly separated.

                {structuredContext}
                """;
            result.EstimatedPromptTokens += LlmGateway.EstimateTokens(prompt);
            result.EstimatedCompletionTokens += 900;
        }

        result.EstimatedCostUsd = LlmGateway.EstimateDeepSeekChatCostUsd(result.EstimatedPromptTokens, result.EstimatedCompletionTokens);
        result.Passed = result.Notes.Count == 0;
        if (result.SignalsConsidered == 0) result.Notes.Add("No pending signals would trigger LLM report generation.");
        return result;
    }

    public TeamIntelligenceLlmReportResult SaveTeamIntelligenceLlmReport(
        WorldCupWatchObject team,
        WorldCupEmployee employee,
        IReadOnlyList<IntelligenceSignalRecord> signals,
        string content,
        LlmCallRecord llmCall)
    {
        var result = new TeamIntelligenceLlmReportResult
        {
            ObjectId = team.Id,
            EmployeeId = employee.Id,
            SignalsUsed = signals.Count,
            LlmCall = llmCall
        };
        if (signals.Count == 0)
        {
            result.Notes.Add("No signals were supplied.");
            return result;
        }

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        var workflow = new WorkflowRunRecord
        {
            Id = $"workflow_{team.Id}_llm_intelligence_report_{DateTime.Now:yyyyMMddHHmmss}",
            WorkflowType = "team_intelligence_llm_report",
            Status = llmCall.Status == "success" ? "completed" : "needs_review",
            ObjectId = team.Id,
            StartedBy = "manual_llm_upgrade",
            StartedAt = now,
            CompletedAt = now,
            ErrorMessage = llmCall.Status == "success" ? null : llmCall.ErrorMessage,
            MetadataJson = new JsonObject
            {
                ["mode"] = "manual_llm",
                ["signal_count"] = signals.Count,
                ["llm_call_id"] = llmCall.Id
            }.ToJsonString()
        };
        UpsertWorkflowRun(connection, transaction, workflow);
        UpsertLlmCall(connection, transaction, llmCall);

        var artifact = SaveArtifact(connection, transaction, new ArtifactRecord
        {
            Id = $"artifact_{workflow.Id}",
            Type = "markdown",
            Title = $"{team.DisplayName} DeepSeek intelligence brief",
            OwnerEmployeeId = employee.Id,
            ObjectId = team.Id,
            WorkflowRunId = workflow.Id,
            FilePath = Path.Combine("artifacts", $"{workflow.Id}.md"),
            Summary = $"{team.DisplayName}: LLM-enhanced brief from {signals.Count} intelligence signals.",
            MetadataJson = new JsonObject
            {
                ["object_id"] = team.Id,
                ["employee_id"] = employee.Id,
                ["llm_call_id"] = llmCall.Id,
                ["signal_ids"] = JsonSerializer.SerializeToNode(signals.Select(signal => signal.Id).ToList(), AppJsonContext.Default.ListString)
            }.ToJsonString()
        }, content);

        var step = MakeCompletedStep(
            workflow.Id,
            "team_intelligence_llm_report",
            employee.Id,
            artifact.Id,
            new JsonObject
            {
                ["source"] = llmCall.Status == "success" ? "llm" : "fallback",
                ["content"] = content,
                ["llm_call_id"] = llmCall.Id
            }.ToJsonString());
        if (llmCall.Status != "success")
        {
            step.Status = "needs_review";
            step.ErrorMessage = llmCall.ErrorMessage;
        }
        UpsertWorkflowStep(connection, transaction, step);

        foreach (var signal in signals)
        {
            UpdateIntelligenceSignalStatus(connection, transaction, signal.Id, "llm_report_triggered");
        }

        SaveSystemEventLog(connection, transaction, new WorldCupSystemEventLog
        {
            EventType = "llm_call_saved",
            Category = "llm",
            Severity = llmCall.Status == "success" ? "info" : "warning",
            Source = llmCall.Provider,
            EmployeeId = employee.Id,
            ObjectId = team.Id,
            WorkflowRunId = workflow.Id,
            LlmCallId = llmCall.Id,
            ArtifactId = artifact.Id,
            Title = "Team intelligence LLM call recorded",
            Message = $"{employee.Name} call {llmCall.Status}; prompt={llmCall.PromptTokens}, completion={llmCall.CompletionTokens}, cost=${llmCall.CostEstimate:0.########}.",
            PayloadJson = new JsonObject
            {
                ["agent_task_id"] = llmCall.AgentTaskId,
                ["model_name"] = llmCall.ModelName,
                ["prompt_tokens"] = llmCall.PromptTokens,
                ["completion_tokens"] = llmCall.CompletionTokens,
                ["cost_estimate"] = llmCall.CostEstimate,
                ["status"] = llmCall.Status,
                ["error_message"] = llmCall.ErrorMessage
            }.ToJsonString()
        });
        SaveSystemEventLog(connection, transaction, new WorldCupSystemEventLog
        {
            EventType = "employee_llm_report_created",
            Category = "employee",
            Severity = workflow.Status == "completed" ? "info" : "warning",
            Source = "manual_llm_upgrade",
            EmployeeId = employee.Id,
            ObjectId = team.Id,
            WorkflowRunId = workflow.Id,
            LlmCallId = llmCall.Id,
            ArtifactId = artifact.Id,
            Title = $"{team.DisplayName} DeepSeek intelligence brief created",
            Message = $"{employee.Name} created an LLM-enhanced brief from {signals.Count} signals.",
            PayloadJson = new JsonObject
            {
                ["workflow_status"] = workflow.Status,
                ["signal_count"] = signals.Count,
                ["artifact_id"] = artifact.Id,
                ["llm_call_id"] = llmCall.Id
            }.ToJsonString()
        });

        transaction.Commit();
        result.WorkflowRun = workflow;
        result.Artifact = artifact;
        result.Passed = llmCall.Status == "success";
        if (!result.Passed) result.Notes.Add("LLM call failed; fallback content was persisted for review.");
        return result;
    }

    public TeamWorkbenchResult GetTeamWorkbench(string objectId, int limit = 20, bool includeTestData = false)
    {
        var cappedLimit = Math.Clamp(limit, 1, 100);
        var result = new TeamWorkbenchResult { ObjectId = objectId };
        var team = GetWatchObjectById(objectId);
        if (team == null)
        {
            result.Notes.Add($"Team not found: {objectId}");
            return result;
        }

        result.Team = team;
        var employeeId = GetPrimaryEmployeeIdForObject(objectId);
        if (!string.IsNullOrWhiteSpace(employeeId))
        {
            result.Employee = GetEmployeeById(employeeId);
        }

        result.Signals = includeTestData
            ? GetIntelligenceSignals(objectId: objectId, limit: cappedLimit)
            : GetProductionIntelligenceSignals(objectId: objectId, limit: cappedLimit);
        result.Snapshots = (includeTestData
                ? GetDataSnapshots(objectId: objectId)
                : GetProductionDataSnapshots(objectId: objectId))
            .Take(cappedLimit)
            .ToList();
        result.Logs = GetSystemEventLogs(objectId: objectId, limit: cappedLimit);
        result.Workflows = GetWorkflowRuns()
            .Where(workflow => string.Equals(workflow.ObjectId, objectId, StringComparison.OrdinalIgnoreCase))
            .Take(cappedLimit)
            .ToList();
        result.Artifacts = GetArtifacts()
            .Where(artifact => string.Equals(artifact.ObjectId, objectId, StringComparison.OrdinalIgnoreCase))
            .Take(cappedLimit)
            .ToList();
        var latestReport = result.Artifacts
            .FirstOrDefault(artifact => artifact.Type == "markdown"
                && artifact.Title.Contains("intelligence brief", StringComparison.OrdinalIgnoreCase));
        if (latestReport != null)
        {
            try
            {
                result.LatestReport = GetArtifactContent(latestReport.Id);
            }
            catch (Exception ex)
            {
                result.Notes.Add($"Latest report content could not be loaded: {ex.Message}");
            }
        }

        result.LlmBudget = EstimateTeamEmployeeReportBudget(objectId, 8);
        result.PendingActionableSignals = result.Signals.Count(IsActionableForLlm);
        result.LlmReportsCreated = result.Artifacts.Count(artifact =>
            artifact.Title.Contains("DeepSeek intelligence brief", StringComparison.OrdinalIgnoreCase));
        result.Passed = result.Team != null && result.Employee != null;
        if (result.Employee == null) result.Notes.Add($"Primary employee not found for team: {objectId}");
        return result;
    }

    public EmployeeReportBudgetEstimate EstimateTeamEmployeeReportBudget(string objectId, int maxSignals = 8)
    {
        var signals = GetIntelligenceSignals(status: "needs_ai_review", objectId: objectId, limit: 500)
            .Where(signal => !string.IsNullOrWhiteSpace(signal.ObjectId))
            .Where(IsActionableForLlm)
            .OrderByDescending(signal => SignalSeverityScore(signal.Severity))
            .ThenByDescending(signal => signal.CreatedAt)
            .Take(Math.Clamp(maxSignals, 1, 12))
            .ToList();
        var result = new EmployeeReportBudgetEstimate
        {
            SignalsConsidered = signals.Count,
            TeamsConsidered = signals.Count > 0 ? 1 : 0,
            MaxTeams = 1
        };
        if (signals.Count == 0)
        {
            result.Passed = true;
            result.Notes.Add("No pending actionable signals would trigger LLM report generation for this team.");
            return result;
        }

        var team = GetWatchObjectById(objectId);
        if (team == null)
        {
            result.Notes.Add($"Team not found: {objectId}");
            return result;
        }
        var employee = GetEmployeeById(GetPrimaryEmployeeIdForObject(team.Id) ?? "");
        var structuredContext = BuildTeamIntelligenceReport(team, employee, signals);
        var prompt = $"""
            You are the assigned football research employee for {team.DisplayName}.
            Convert the structured intelligence brief into a concise Chinese Markdown report for the CEO.
            Keep facts, inference, uncertainty, and next action clearly separated.

            {structuredContext}
            """;
        result.EstimatedPromptTokens = LlmGateway.EstimateTokens(prompt);
        result.EstimatedCompletionTokens = 900;
        result.EstimatedCostUsd = LlmGateway.EstimateDeepSeekChatCostUsd(result.EstimatedPromptTokens, result.EstimatedCompletionTokens);
        result.Passed = result.Notes.Count == 0;
        return result;
    }

    public TeamWorkbenchHarnessResult RunTeamWorkbenchHarness()
    {
        SeedDemoWorldCupCompany();
        var marker = $"team_workbench_harness_{Guid.NewGuid():N}";
        ImportDataSnapshots(new DataSnapshotBatchImportRequest
        {
            Source = "team_workbench_harness",
            Items =
            [
                new DataSnapshotCreateRequest
                {
                    Source = "team_workbench_harness",
                    SnapshotType = "news_intel",
                    ObjectId = "team_can",
                    ContentJson = $$"""{"title":"Canada lineup watch {{marker}}","description":"Canada has a squad selection update before the 2026 World Cup opener.","url":"https://example.com/{{marker}}"}"""
                }
            ]
        });
        RunIntelligenceTriage(100, includeTestData: true);
        TriggerEmployeeReportsFromSignals(4, includeTestData: true);

        var workbench = GetTeamWorkbench("team_can", 50, includeTestData: true);
        var result = new TeamWorkbenchHarnessResult
        {
            TeamFound = workbench.Team?.Id == "team_can",
            EmployeeFound = workbench.Employee?.Id == "emp_can",
            SignalsLoaded = workbench.Signals.Count > 0,
            SnapshotsLoaded = workbench.Snapshots.Any(snapshot => snapshot.ContentJson.Contains(marker, StringComparison.Ordinal)),
            LogsLoaded = workbench.Logs.Count > 0,
            ArtifactsLoaded = workbench.Artifacts.Count > 0,
            LatestReportLoaded = workbench.LatestReport != null && !string.IsNullOrWhiteSpace(workbench.LatestReport.Content),
            BudgetLoaded = workbench.LlmBudget != null
        };
        if (!result.TeamFound) result.Notes.Add("Team was not loaded.");
        if (!result.EmployeeFound) result.Notes.Add("Primary employee was not loaded.");
        if (!result.SignalsLoaded) result.Notes.Add("Signals were not loaded.");
        if (!result.SnapshotsLoaded) result.Notes.Add("Expected harness snapshot was not loaded.");
        if (!result.LogsLoaded) result.Notes.Add("Logs were not loaded.");
        if (!result.ArtifactsLoaded) result.Notes.Add("Artifacts were not loaded.");
        if (!result.LatestReportLoaded) result.Notes.Add("Latest report content was not loaded.");
        if (!result.BudgetLoaded) result.Notes.Add("Budget was not loaded.");
        result.Passed = result.TeamFound
            && result.EmployeeFound
            && result.SignalsLoaded
            && result.SnapshotsLoaded
            && result.LogsLoaded
            && result.ArtifactsLoaded
            && result.LatestReportLoaded
            && result.BudgetLoaded;
        return result;
    }

    public IntelligenceContentQualityResult AuditIntelligenceReportContent(string artifactId)
    {
        var content = GetArtifactContent(artifactId);
        var result = new IntelligenceContentQualityResult { ArtifactId = artifactId };
        if (content == null)
        {
            result.Notes.Add($"Artifact not found: {artifactId}");
            return result;
        }

        result.ObjectId = content.Artifact.ObjectId ?? "";
        result.ContentChars = content.Content.Length;
        result.ContainsCoreJudgment = ContainsAny(content.Content, ["# 核心判断", "## 核心判断", "核心判断"]);
        result.ContainsEvidence = ContainsAny(content.Content, ["# 关键证据", "## 关键证据", "关键证据", "evidence snapshot"]);
        result.ContainsUncertaintyOrRisk = ContainsAny(content.Content, ["# 不确定性", "## 不确定性", "不确定性", "主要风险", "风险"]);
        result.ContainsAction = ContainsAny(content.Content, ["# 建议动作", "## 建议动作", "建议动作", "给 CEO 的建议", "Next Action"]);
        result.ContainsSignalTrace = content.Content.Contains("signal_", StringComparison.OrdinalIgnoreCase)
            && (content.Content.Contains("snapshot_", StringComparison.OrdinalIgnoreCase)
                || content.Artifact.MetadataJson.Contains("signal_", StringComparison.OrdinalIgnoreCase));
        result.ContainsNoBettingGuardrail = ContainsAny(content.Content, ["不构成投注建议", "不提供投注", "不要给赌博下注建议", "非投注说明"]);
        result.AvoidsForbiddenClaims = !ContainsAny(content.Content, [
            "保证获胜",
            "稳赚",
            "必胜",
            "下注建议",
            "建议下注",
            "我已经实时浏览",
            "我刚刚访问了网页",
            "100%胜率"
        ]);

        if (result.ContentChars < 120) result.Notes.Add("Report content is too short to be useful.");
        if (!result.ContainsCoreJudgment) result.Notes.Add("Report missing core judgment section.");
        if (!result.ContainsEvidence) result.Notes.Add("Report missing evidence section.");
        if (!result.ContainsUncertaintyOrRisk) result.Notes.Add("Report missing uncertainty or risk section.");
        if (!result.ContainsAction) result.Notes.Add("Report missing action recommendation section.");
        if (!result.ContainsSignalTrace) result.Notes.Add("Report missing signal or snapshot trace.");
        if (!result.ContainsNoBettingGuardrail) result.Notes.Add("Report missing non-betting guardrail.");
        if (!result.AvoidsForbiddenClaims) result.Notes.Add("Report contains forbidden or overconfident claims.");
        result.Passed = result.ContentChars >= 120
            && result.ContainsCoreJudgment
            && result.ContainsEvidence
            && result.ContainsUncertaintyOrRisk
            && result.ContainsAction
            && result.ContainsSignalTrace
            && result.ContainsNoBettingGuardrail
            && result.AvoidsForbiddenClaims;
        return result;
    }

    public IntelligenceContentQualityHarnessResult RunIntelligenceContentQualityHarness()
    {
        SeedDemoWorldCupCompany();
        var marker = $"content_quality_{Guid.NewGuid():N}";
        ImportDataSnapshots(new DataSnapshotBatchImportRequest
        {
            Source = "content_quality_harness",
            Items =
            [
                new DataSnapshotCreateRequest
                {
                    Source = "content_quality_harness",
                    SnapshotType = "news_intel",
                    ObjectId = "team_can",
                    ContentJson = $$"""{"title":"Canada injury and lineup watch {{marker}}","description":"Canada has a hamstring injury concern and squad selection uncertainty before the 2026 World Cup opener.","url":"https://example.com/{{marker}}"}"""
                }
            ]
        });

        RunIntelligenceTriage(120, includeTestData: true);
        TriggerEmployeeReportsFromSignals(6, includeTestData: true);
        var structuredArtifact = GetArtifacts()
            .FirstOrDefault(artifact => artifact.ObjectId == "team_can"
                && artifact.Title.Contains("intelligence brief", StringComparison.OrdinalIgnoreCase)
                && !artifact.Title.Contains("DeepSeek", StringComparison.OrdinalIgnoreCase)
                && artifact.MetadataJson.Contains(marker, StringComparison.OrdinalIgnoreCase));
        structuredArtifact ??= GetArtifacts()
            .FirstOrDefault(artifact => artifact.ObjectId == "team_can"
                && artifact.Title.Contains("intelligence brief", StringComparison.OrdinalIgnoreCase)
                && !artifact.Title.Contains("DeepSeek", StringComparison.OrdinalIgnoreCase));

        var signal = GetIntelligenceSignals(objectId: "team_can", limit: 20)
            .FirstOrDefault(item => item.Summary.Contains(marker, StringComparison.OrdinalIgnoreCase))
            ?? GetIntelligenceSignals(objectId: "team_can", limit: 20).FirstOrDefault();
        var team = GetWatchObjectById("team_can") ?? throw new InvalidOperationException("team_can missing.");
        var employee = GetEmployeeById(GetPrimaryEmployeeIdForObject(team.Id) ?? "")
            ?? WorldCupWorkflowService.CreateFallbackEmployee("emp_can_fallback", "Canada Team Researcher", "team researcher");
        var llmContent = $"""
            # 核心判断
            加拿大当前存在一条需要复核的伤停或阵容情报，应该先视为赛前风险信号，而不是确定事实。

            # 关键证据
            - signal_id: `{signal?.Id ?? "signal_missing"}`
            - snapshot_id: `{signal?.SourceSnapshotId ?? "snapshot_missing"}`
            - 信号摘要：{signal?.Summary ?? "No signal summary available."}

            # 不确定性
            该内容来自系统入库快照，尚未完成二次来源核验；球员健康和首发信息可能在赛前变化。

            # 建议动作
            由加拿大研究员在下一次自动采集后复核来源，并把变化写入球队日志，供 CEO 汇总。

            # 非投注说明
            本报告仅用于赛事情报研究，不构成投注建议，也不保证任何比赛结果。
            """;
        var llmResult = SaveTeamIntelligenceLlmReport(team, employee, signal == null ? [] : [signal], llmContent, new LlmCallRecord
        {
            Id = $"llm_content_quality_{Guid.NewGuid():N}",
            AgentTaskId = "content_quality_harness",
            EmployeeId = employee.Id,
            ModelName = "harness-model",
            Provider = "harness",
            PromptVersion = "content_quality_v1",
            PromptTokens = 120,
            CompletionTokens = LlmGateway.EstimateTokens(llmContent),
            CostEstimate = LlmGateway.EstimateDeepSeekChatCostUsd(120, LlmGateway.EstimateTokens(llmContent)),
            Status = "success",
            RequestHash = Sha256(marker),
            ResponseHash = Sha256(llmContent)
        });

        var result = new IntelligenceContentQualityHarnessResult
        {
            StructuredReport = structuredArtifact == null ? new IntelligenceContentQualityResult() : AuditIntelligenceReportContent(structuredArtifact.Id),
            LlmReport = llmResult.Artifact == null ? new IntelligenceContentQualityResult() : AuditIntelligenceReportContent(llmResult.Artifact.Id)
        };
        if (structuredArtifact == null) result.Notes.Add("Structured intelligence artifact was not created.");
        if (llmResult.Artifact == null) result.Notes.Add("LLM intelligence artifact was not created.");
        if (!result.StructuredReport.Passed) result.Notes.AddRange(result.StructuredReport.Notes.Select(note => $"Structured: {note}"));
        if (!result.LlmReport.Passed) result.Notes.AddRange(result.LlmReport.Notes.Select(note => $"LLM: {note}"));
        result.Passed = structuredArtifact != null
            && llmResult.Artifact != null
            && result.StructuredReport.Passed
            && result.LlmReport.Passed;
        return result;
    }

    public IntelligenceQueueQualityResult AuditIntelligenceQueueQuality(int limit = 500)
    {
        var signals = GetIntelligenceSignals(limit: Math.Clamp(limit, 1, 1000));
        var result = new IntelligenceQueueQualityResult { SignalsChecked = signals.Count };
        foreach (var signal in signals)
        {
            var actionable = IsActionableForLlm(signal);
            if (actionable) result.ActionableSignals++;
            if (actionable && signal.Status == "needs_ai_review") result.PendingActionableSignals++;
            if (!actionable && signal.Status == "needs_ai_review") result.NonActionablePendingReview++;
            if (string.IsNullOrWhiteSpace(signal.ObjectId)) result.MissingObjectCount++;
            if (string.IsNullOrWhiteSpace(signal.EvidenceJson)) result.MissingEvidenceCount++;
        }

        if (result.SignalsChecked == 0) result.Notes.Add("No intelligence signals were available for queue audit.");
        if (result.NonActionablePendingReview > 0) result.Notes.Add($"Found {result.NonActionablePendingReview} non-actionable signals still waiting for AI review.");
        if (result.MissingObjectCount > 0) result.Notes.Add($"Found {result.MissingObjectCount} signals without an object_id.");
        if (result.MissingEvidenceCount > 0) result.Notes.Add($"Found {result.MissingEvidenceCount} signals without evidence_json.");
        result.Passed = result.SignalsChecked > 0
            && result.NonActionablePendingReview == 0
            && result.MissingEvidenceCount == 0;
        return result;
    }

    public IntelligenceWorkflowHarnessResult RunIntelligenceWorkflowHarness()
    {
        SeedDemoWorldCupCompany();
        var marker = $"intelligence_harness_{Guid.NewGuid():N}";
        ImportDataSnapshots(new DataSnapshotBatchImportRequest
        {
            Source = "intelligence_harness",
            Items =
            [
                new DataSnapshotCreateRequest
                {
                    Source = "intelligence_harness",
                    SnapshotType = "news_intel",
                    ObjectId = "team_arg",
                    MatchId = "match_arg_jpn",
                    ContentJson = $$"""{"title":"Argentina injury watch {{marker}}","description":"Argentina reported a hamstring injury risk and lineup uncertainty before the Japan match.","url":"https://example.com/{{marker}}"}"""
                }
            ]
        });

        var triage = RunIntelligenceTriage(800, includeTestData: true);
        var signal = GetIntelligenceSignals(objectId: "team_arg", matchId: "match_arg_jpn", signalType: "injury_risk", limit: 50)
            .FirstOrDefault(item => item.Summary.Contains(marker, StringComparison.Ordinal));
        var trigger = TriggerEmployeeReportsFromSignals(48, includeTestData: true);
        var artifactExists = trigger.Artifacts.Any(artifact => artifact.ObjectId == "team_arg");
        var eventsLogged = GetSystemEventLogs(category: "intelligence", objectId: "team_arg", limit: 50)
            .Any(item => item.Message.Contains(marker, StringComparison.Ordinal))
            && GetSystemEventLogs(eventType: "employee_report_triggered", objectId: "team_arg", limit: 50).Count > 0;

        var result = new IntelligenceWorkflowHarnessResult
        {
            SignalCreated = triage.SignalsCreated > 0,
            SignalRecalled = signal != null,
            ReportTriggered = trigger.ReportsCreated > 0,
            ArtifactCreated = artifactExists,
            EventsLogged = eventsLogged
        };
        if (!result.SignalCreated) result.Notes.Add("Triage did not create a new signal.");
        if (!result.SignalRecalled) result.Notes.Add("Expected Argentina injury signal was not recalled.");
        if (!result.ReportTriggered) result.Notes.Add("Employee report was not triggered.");
        if (!result.ArtifactCreated) result.Notes.Add("Triggered report artifact was not created.");
        if (!result.EventsLogged) result.Notes.Add("Intelligence or employee trigger events were not logged.");
        result.Passed = result.SignalCreated && result.SignalRecalled && result.ReportTriggered && result.ArtifactCreated && result.EventsLogged;
        return result;
    }

    public IntelligenceWorkflowHarnessResult RunIntelligenceTriageQualityHarness()
    {
        SeedDemoWorldCupCompany();
        var marker = $"quality_harness_{Guid.NewGuid():N}";
        ImportDataSnapshots(new DataSnapshotBatchImportRequest
        {
            Source = "intelligence_quality_harness",
            Items =
            [
                new DataSnapshotCreateRequest
                {
                    Source = "intelligence_quality_harness",
                    SnapshotType = "news_intel",
                    ContentJson = $$"""{"provider":"rss_news","articles":[{"title":"Arsenal injury update {{marker}}","description":"Arsenal reported a generic club injury unrelated to the World Cup.","url":"https://example.com/noise-{{marker}}"},{"title":"Premier League review {{marker}}","description":"A club-only article says Arsenal can improve after transfers, with no World Cup team context.","url":"https://example.com/can-noise-{{marker}}"},{"title":"Argentina generic FIFA briefing {{marker}}","description":"Argentina appears in a broad FIFA World Cup 2026 media roundup without injury or lineup detail.","url":"https://example.com/generic-{{marker}}"},{"title":"Broad squad roundup {{marker}}","description":"Argentina, Brazil, England, France, Germany and Spain all named in a broad World Cup squad roundup with no team-specific confirmation.","url":"https://example.com/broad-{{marker}}"},{"title":"Brazil injury concern {{marker}}","description":"Brazil forward Neymar has a calf injury before the 2026 World Cup.","url":"https://example.com/signal-{{marker}}"}]}"""
                },
                new DataSnapshotCreateRequest
                {
                    Source = "intelligence_quality_harness",
                    SnapshotType = "news_intel",
                    ObjectId = "team_bra",
                    ContentJson = $$"""{"provider":"rss_news","articles":[{"title":"Arsenal injury update targeted {{marker}}","description":"Arsenal reported a hamstring injury unrelated to international football.","url":"https://example.com/targeted-noise-{{marker}}"},{"title":"Brazil squad update targeted {{marker}}","description":"Brazil named a revised World Cup squad after a selection concern.","url":"https://example.com/targeted-signal-{{marker}}"}]}"""
                }
            ]
        });

        var triage = RunIntelligenceTriage(800, includeTestData: true);
        var brazilSignal = GetIntelligenceSignals(objectId: "team_bra", signalType: "injury_risk", limit: 50)
            .FirstOrDefault(item => item.Summary.Contains(marker, StringComparison.Ordinal));
        var targetedTeamSignal = GetIntelligenceSignals(objectId: "team_bra", signalType: "lineup_news", limit: 80)
            .FirstOrDefault(item =>
                SignalContains(item, $"targeted {marker}")
                || SignalContains(item, $"targeted-signal-{marker}"));
        var trigger = TriggerEmployeeReportsFromSignals(48, includeTestData: true);
        var arsenalNoise = GetIntelligenceSignals(limit: 200)
            .Any(item => item.Summary.Contains($"noisy-{marker}", StringComparison.Ordinal)
                || item.Summary.Contains($"noise-{marker}", StringComparison.Ordinal)
                || item.Summary.Contains($"can-noise-{marker}", StringComparison.Ordinal));
        var targetedFeedNoise = GetIntelligenceSignals(objectId: "team_bra", limit: 200)
            .Any(item => SignalContains(item, $"targeted-noise-{marker}"));
        var genericNewsReport = trigger.Artifacts
            .Select(artifact =>
            {
                try { return GetArtifactContent(artifact.Id)?.Content ?? ""; }
                catch { return ""; }
            })
            .Any(content => content.Contains($"generic-{marker}", StringComparison.Ordinal));
        var broadArticleSignal = GetIntelligenceSignals(limit: 300)
            .Any(item => item.Summary.Contains($"broad-{marker}", StringComparison.Ordinal));

        var result = new IntelligenceWorkflowHarnessResult
        {
            SignalCreated = triage.SignalsCreated > 0,
            SignalRecalled = brazilSignal != null,
            ReportTriggered = trigger.ReportsCreated > 0,
            ArtifactCreated = !arsenalNoise,
            NonActionableReportSkipped = !genericNewsReport,
            BroadArticleSkipped = !broadArticleSignal,
            TargetedFeedNoiseSkipped = !targetedFeedNoise && targetedTeamSignal != null,
            EventsLogged = GetSystemEventLogs(category: "intelligence", objectId: "team_bra", limit: 50)
                .Any(item => item.Message.Contains(marker, StringComparison.Ordinal))
        };
        if (!result.SignalCreated) result.Notes.Add("Quality triage did not create a signal.");
        if (!result.SignalRecalled) result.Notes.Add("Expected Brazil World Cup injury signal was not created.");
        if (!result.ReportTriggered) result.Notes.Add("Actionable Brazil signal did not trigger an employee report.");
        if (arsenalNoise) result.Notes.Add("Generic non-World-Cup club news leaked into intelligence signals.");
        if (targetedFeedNoise) result.Notes.Add("Targeted RSS feed noise was incorrectly assigned to the bound team.");
        if (targetedTeamSignal == null) result.Notes.Add("Expected Brazil targeted lineup signal was not created.");
        if (genericNewsReport) result.Notes.Add("Generic World Cup news_update leaked into employee report triggering.");
        if (broadArticleSignal) result.Notes.Add("Broad multi-team article was over-assigned to team intelligence signals.");
        if (!result.EventsLogged) result.Notes.Add("Quality triage signal was not logged.");
        result.Passed = result.SignalCreated
            && result.SignalRecalled
            && result.ReportTriggered
            && result.ArtifactCreated
            && result.NonActionableReportSkipped
            && result.BroadArticleSkipped
            && result.TargetedFeedNoiseSkipped
            && result.EventsLogged;
        return result;
    }

    private static bool SignalContains(IntelligenceSignalRecord signal, string value)
    {
        return signal.Title.Contains(value, StringComparison.Ordinal)
            || signal.Summary.Contains(value, StringComparison.Ordinal)
            || signal.EvidenceJson.Contains(value, StringComparison.Ordinal);
    }

    private IEnumerable<IntelligenceSignalRecord> BuildSignalsFromSnapshot(DataSnapshotRecord snapshot, IReadOnlyList<WorldCupWatchObject> teams)
    {
        if (snapshot.SnapshotType.Contains("news", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var signal in BuildNewsSignalsFromSnapshot(snapshot, teams))
            {
                yield return signal;
            }
            yield break;
        }

        var text = snapshot.ContentJson ?? "";
        var signalType = "";
        var severity = "low";
        var confidence = 0.45;
        var status = "approved";

        if (snapshot.SnapshotType.Contains("fixture", StringComparison.OrdinalIgnoreCase))
        {
            signalType = "fixture_update";
            severity = "low";
            confidence = 0.9;
            status = "approved";
        }
        else if (snapshot.SnapshotType.Contains("group", StringComparison.OrdinalIgnoreCase))
        {
            signalType = "group_update";
            severity = "low";
            confidence = 0.84;
            status = "approved";
        }

        if (string.IsNullOrWhiteSpace(signalType))
        {
            yield break;
        }

        var objectIds = ResolveSignalObjectIds(snapshot, text, teams);
        if (objectIds.Count == 0)
        {
            objectIds.Add(null);
        }

        foreach (var objectId in objectIds)
        {
            yield return CreateSignal(snapshot, signalType, severity, confidence, status, objectId, snapshot.MatchId, text);
        }
    }

    private IEnumerable<IntelligenceSignalRecord> BuildNewsSignalsFromSnapshot(DataSnapshotRecord snapshot, IReadOnlyList<WorldCupWatchObject> teams)
    {
        var articles = ExtractNewsArticles(snapshot.ContentJson);
        foreach (var article in articles)
        {
            var articleText = article.ToJsonString();
            if (!IsWorldCupRelevant(articleText, teams)) continue;

            var signalType = ResolveNewsSignalType(articleText);
            if (signalType == null) continue;

            var objectIds = ResolveNewsSignalObjectIds(snapshot, articleText, teams);
            if (objectIds.Count == 0) continue;
            if (string.IsNullOrWhiteSpace(snapshot.ObjectId) && objectIds.Count > MaxNewsArticleTeamAssignments) continue;

            var severity = signalType == "injury_risk" ? "high" : "medium";
            var confidence = signalType == "injury_risk" ? 0.82 : signalType == "lineup_news" ? 0.72 : 0.58;
            var status = signalType is "injury_risk" or "lineup_news" ? "needs_ai_review" : "approved";
            foreach (var objectId in objectIds)
            {
                yield return CreateSignal(snapshot, signalType, severity, confidence, status, objectId, snapshot.MatchId, articleText);
            }
        }
    }

    private static List<JsonObject> ExtractNewsArticles(string contentJson)
    {
        var rows = new List<JsonObject>();
        try
        {
            using var document = JsonDocument.Parse(contentJson);
            if (document.RootElement.TryGetProperty("articles", out var articles) && articles.ValueKind == JsonValueKind.Array)
            {
                foreach (var article in articles.EnumerateArray())
                {
                    rows.Add(JsonNode.Parse(article.GetRawText()) as JsonObject ?? []);
                }
                return rows;
            }
            rows.Add(JsonNode.Parse(document.RootElement.GetRawText()) as JsonObject ?? []);
        }
        catch
        {
            rows.Add(new JsonObject { ["raw"] = contentJson });
        }
        return rows;
    }

    private static bool IsWorldCupRelevant(string text, IReadOnlyList<WorldCupWatchObject> teams)
    {
        return ContainsAny(text, WorldCupKeywords)
            || NewsArticleHasTeamEntity(text, teams);
    }

    private static string? ResolveNewsSignalType(string text)
    {
        if (ContainsAny(text, InjuryKeywords)) return "injury_risk";
        if (ContainsAny(text, LineupKeywords)) return "lineup_news";
        if (ContainsAny(text, WorldCupKeywords)) return "news_update";
        return null;
    }

    private static IntelligenceSignalRecord CreateSignal(
        DataSnapshotRecord snapshot,
        string signalType,
        string severity,
        double confidence,
        string status,
        string? objectId,
        string? matchId,
        string evidenceText)
    {
        var evidence = new JsonObject
        {
            ["source"] = snapshot.Source,
            ["snapshot_type"] = snapshot.SnapshotType,
            ["snapshot_id"] = snapshot.Id,
            ["snapshot_hash"] = snapshot.ContentHash,
            ["captured_at"] = snapshot.CapturedAt,
            ["signal_type"] = signalType,
            ["fact_level"] = ResolveSignalFactLevel(snapshot.Source, signalType, status),
            ["entity_binding"] = string.IsNullOrWhiteSpace(objectId) ? "unbound" : "team_bound",
            ["prediction_usage"] = signalType is "injury_risk" or "lineup_news"
                ? "requires_review_before_prediction_input"
                : "context_only",
            ["matched_keywords"] = JsonSerializer.SerializeToNode(
                ResolveMatchedSignalKeywords(signalType, evidenceText),
                AppJsonContext.Default.ListString),
            ["excerpt"] = TruncateForLog(evidenceText, 900)
        }.ToJsonString();
        var title = signalType switch
        {
            "injury_risk" => "Injury risk signal",
            "lineup_news" => "Lineup or squad signal",
            "fixture_update" => "Fixture update signal",
            "group_update" => "Group standing signal",
            _ => "News intelligence signal"
        };
        var summary = $"{title} from {snapshot.Source}: {TruncateForLog(evidenceText, 260)}";
        var hash = Sha256($"{snapshot.Id}|{signalType}|{objectId}|{matchId}|{Sha256(evidenceText)}");
        return new IntelligenceSignalRecord
        {
            Id = $"signal_{hash[..32]}",
            SourceSnapshotId = snapshot.Id,
            SignalType = signalType,
            Severity = severity,
            Confidence = confidence,
            ObjectId = objectId,
            MatchId = matchId,
            Title = title,
            Summary = summary,
            EvidenceJson = evidence,
            Status = status,
            ContentHash = hash,
            CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            UpdatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
        };
    }

    private static List<string> ResolveMatchedSignalKeywords(string signalType, string text)
    {
        var keywords = signalType switch
        {
            "injury_risk" => InjuryKeywords,
            "lineup_news" => LineupKeywords,
            "news_update" => WorldCupKeywords,
            _ => []
        };

        return keywords
            .Where(keyword => text.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToList();
    }

    private static string ResolveSignalFactLevel(string source, string signalType, string status)
    {
        if (signalType is "fixture_update" or "group_update") return "structured_crosscheck";
        if (source.Contains("fifa", StringComparison.OrdinalIgnoreCase)) return "official_reference";
        if (status == "approved" && signalType == "news_update") return "context_signal";
        if (signalType is "injury_risk" or "lineup_news") return "single_source_news";
        return "unverified_signal";
    }

    private static List<string?> ResolveSignalObjectIds(DataSnapshotRecord snapshot, string text, IReadOnlyList<WorldCupWatchObject> teams)
    {
        var objectIds = new List<string?>();
        if (!string.IsNullOrWhiteSpace(snapshot.ObjectId))
        {
            objectIds.Add(snapshot.ObjectId);
            return objectIds;
        }

        foreach (var team in teams)
        {
            if ((!string.IsNullOrWhiteSpace(team.Name) && text.Contains(team.Name, StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrWhiteSpace(team.DisplayName) && text.Contains(team.DisplayName, StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrWhiteSpace(team.Symbol) && ContainsToken(text, team.Symbol)))
            {
                objectIds.Add(team.Id);
            }
        }
        return objectIds.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool ContainsToken(string text, string token)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(token)) return false;
        var trimmed = token.Trim();
        var options = RegexOptions.CultureInvariant;
        if (trimmed.Length > 3)
        {
            options |= RegexOptions.IgnoreCase;
        }
        return Regex.IsMatch(
            text,
            $@"(?<![A-Za-z0-9]){Regex.Escape(trimmed)}(?![A-Za-z0-9])",
            options);
    }

    private static bool IsActionableForLlm(IntelligenceSignalRecord signal)
    {
        return signal.SignalType is "injury_risk" or "lineup_news";
    }

    private int ApproveNonActionablePendingSignals()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE intelligence_signals
            SET status = 'approved',
                updated_at = $updated_at
            WHERE status = 'needs_ai_review'
              AND signal_type NOT IN ('injury_risk', 'lineup_news')
            """;
        Add(command, "$updated_at", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        return command.ExecuteNonQuery();
    }

    private static bool ContainsAny(string text, IEnumerable<string> keywords)
    {
        return keywords.Any(keyword => text.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }
}

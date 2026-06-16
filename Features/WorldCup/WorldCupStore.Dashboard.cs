using Microsoft.Data.Sqlite;

namespace PiPiClaw.Team;

public sealed partial class WorldCupStore
{
    public WorldCupCompanyDashboardResult GetCompanyDashboard(int activityLimit = 20, bool includeTestData = false)
    {
        var workflows = includeTestData ? GetWorkflowRuns() : GetProductionWorkflowRuns();
        var result = new WorldCupCompanyDashboardResult
        {
            Company = BuildCompanySummary(includeTestData),
            Operations = GetOperationsSummary(includeTestData),
            AutoCollection = BuildAutoCollectionSummary(),
            RecentActivity = GetActivityFeed(activityLimit, includeTestData),
            ActiveWorkflows = workflows
                .Where(workflow => workflow.Status is "pending" or "running" or "needs_review")
                .Take(20)
                .ToList()
        };

        var expectedMatches = includeTestData ? 100 : 72;
        if (result.Company.TotalTeams < 48) result.Notes.Add($"Expected 48 football teams, got {result.Company.TotalTeams}.");
        if (result.Company.MissingRoles.Count > 0) result.Notes.Add($"Missing target roles: {string.Join(", ", result.Company.MissingRoles)}.");
        if (result.Operations.Matches < expectedMatches) result.Notes.Add($"Expected at least {expectedMatches} match records for this data view, got {result.Operations.Matches}.");
        if (result.RecentActivity.Count == 0) result.Notes.Add("No recent activity logs available.");
        result.Passed = result.Company.TotalTeams >= 48
            && result.Operations.Matches >= expectedMatches
            && result.RecentActivity.Count > 0;
        return result;
    }

    public WorldCupOperationsSummary GetOperationsSummary(bool includeTestData = false)
    {
        var matches = includeTestData ? GetMatches() : GetProductionMatches();
        var workflows = includeTestData ? GetWorkflowRuns() : GetProductionWorkflowRuns();
        var artifacts = includeTestData ? GetArtifacts() : GetProductionArtifacts();
        var snapshots = includeTestData ? GetDataSnapshots() : GetProductionDataSnapshots();
        var predictions = includeTestData ? GetBaselinePredictions() : GetProductionBaselinePredictions();
        var summary = new WorldCupOperationsSummary
        {
            Matches = matches.Count,
            ScheduledMatches = matches.Count(match => match.Status == "scheduled"),
            FinishedMatches = matches.Count(match => match.Status == "finished"),
            BaselinePredictions = predictions.Count,
            DataSnapshots = snapshots.Count,
            WorkflowRuns = workflows.Count,
            CompletedWorkflows = workflows.Count(workflow => workflow.Status == "completed"),
            FailedWorkflows = workflows.Count(workflow => workflow.Status == "failed"),
            NeedsReviewWorkflows = workflows.Count(workflow => workflow.Status == "needs_review"),
            Artifacts = artifacts.Count,
            Llm = GetLlmUsageSummary(),
            SnapshotQuality = BuildOperationalSnapshotQuality(),
            IntelligenceQueueQuality = AuditIntelligenceQueueQuality(800)
        };
        return summary;
    }

    public WorldCupLlmUsageSummary GetLlmUsageSummary()
    {
        using var connection = OpenConnection();
        using var summaryCommand = connection.CreateCommand();
        summaryCommand.CommandText = """
            SELECT COUNT(*),
                   IFNULL(SUM(prompt_tokens), 0),
                   IFNULL(SUM(completion_tokens), 0),
                   IFNULL(SUM(cost_estimate), 0),
                   SUM(CASE WHEN status = 'success' THEN 1 ELSE 0 END),
                   SUM(CASE WHEN status != 'success' THEN 1 ELSE 0 END)
            FROM llm_calls
            """;
        using var reader = summaryCommand.ExecuteReader();
        var summary = new WorldCupLlmUsageSummary();
        if (reader.Read())
        {
            summary.Calls = Convert.ToInt32(reader.GetValue(0));
            summary.PromptTokens = Convert.ToInt32(reader.GetValue(1));
            summary.CompletionTokens = Convert.ToInt32(reader.GetValue(2));
            summary.EstimatedCostUsd = Convert.ToDouble(reader.GetValue(3));
            summary.SuccessfulCalls = Convert.ToInt32(reader.GetValue(4));
            summary.FailedCalls = Convert.ToInt32(reader.GetValue(5));
        }
        reader.Close();

        var employees = GetEmployees().ToDictionary(employee => employee.Id, StringComparer.OrdinalIgnoreCase);
        using var employeeCommand = connection.CreateCommand();
        employeeCommand.CommandText = """
            SELECT IFNULL(employee_id, ''),
                   COUNT(*),
                   IFNULL(SUM(prompt_tokens), 0),
                   IFNULL(SUM(completion_tokens), 0),
                   IFNULL(SUM(cost_estimate), 0)
            FROM llm_calls
            GROUP BY IFNULL(employee_id, '')
            ORDER BY IFNULL(SUM(cost_estimate), 0) DESC, COUNT(*) DESC
            """;
        using var employeeReader = employeeCommand.ExecuteReader();
        while (employeeReader.Read())
        {
            var employeeId = employeeReader.GetString(0);
            employees.TryGetValue(employeeId, out var employee);
            summary.ByEmployee.Add(new WorldCupLlmUsageEmployeeGroup
            {
                EmployeeId = string.IsNullOrWhiteSpace(employeeId) ? "unassigned" : employeeId,
                EmployeeName = employee?.Name ?? (string.IsNullOrWhiteSpace(employeeId) ? "unassigned" : employeeId),
                Role = employee == null ? "" : NormalizeEmployeeRole(employee.Role),
                Calls = Convert.ToInt32(employeeReader.GetValue(1)),
                PromptTokens = Convert.ToInt32(employeeReader.GetValue(2)),
                CompletionTokens = Convert.ToInt32(employeeReader.GetValue(3)),
                EstimatedCostUsd = Convert.ToDouble(employeeReader.GetValue(4))
            });
        }
        employeeReader.Close();

        using var dayCommand = connection.CreateCommand();
        dayCommand.CommandText = """
            SELECT substr(created_at, 1, 10),
                   COUNT(*),
                   IFNULL(SUM(prompt_tokens), 0),
                   IFNULL(SUM(completion_tokens), 0),
                   IFNULL(SUM(cost_estimate), 0)
            FROM llm_calls
            GROUP BY substr(created_at, 1, 10)
            ORDER BY substr(created_at, 1, 10) DESC
            LIMIT 30
            """;
        using var dayReader = dayCommand.ExecuteReader();
        while (dayReader.Read())
        {
            summary.ByDay.Add(new WorldCupLlmUsageDayGroup
            {
                Day = dayReader.GetString(0),
                Calls = Convert.ToInt32(dayReader.GetValue(1)),
                PromptTokens = Convert.ToInt32(dayReader.GetValue(2)),
                CompletionTokens = Convert.ToInt32(dayReader.GetValue(3)),
                EstimatedCostUsd = Convert.ToDouble(dayReader.GetValue(4))
            });
        }
        dayReader.Close();

        using var recentCommand = connection.CreateCommand();
        recentCommand.CommandText = """
            SELECT id, agent_task_id, employee_id, model_name, provider, prompt_version,
                   prompt_tokens, completion_tokens, cost_estimate, request_hash, response_hash,
                   status, error_message, created_at
            FROM llm_calls
            ORDER BY created_at DESC, id DESC
            LIMIT 20
            """;
        using var recentReader = recentCommand.ExecuteReader();
        while (recentReader.Read())
        {
            summary.RecentCalls.Add(ReadLlmCall(recentReader));
        }

        return summary;
    }

    public List<WorldCupActivityFeedItem> GetActivityFeed(int limit = 50, bool includeTestData = false)
    {
        var logs = includeTestData
            ? GetSystemEventLogs(limit: Math.Clamp(limit, 1, 200))
            : GetProductionSystemEventLogs(limit: Math.Clamp(limit, 1, 200));
        return logs
            .Select(ToActivityFeedItem)
            .ToList();
    }

    public WorldCupEmployeeStatusSummaryResult GetEmployeeStatusSummary(bool includeTestData = false)
    {
        var employees = includeTestData ? GetEmployees() : GetProductionEmployees();
        var teamsById = (includeTestData ? GetWatchObjects() : GetProductionWatchObjects())
            .Where(item => item.Type == "football_team")
            .ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
        var assignmentsByEmployee = (includeTestData ? GetAssignments() : GetProductionAssignments())
            .Where(assignment => assignment.Status == "active")
            .GroupBy(assignment => assignment.EmployeeId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var workflows = includeTestData ? GetWorkflowRuns() : GetProductionWorkflowRuns();
        var artifacts = includeTestData ? GetArtifacts() : GetProductionArtifacts();
        var memories = GetMemories();
        var llmUsageByEmployee = GetLlmUsageSummary().ByEmployee.ToDictionary(group => group.EmployeeId, StringComparer.OrdinalIgnoreCase);
        var strategyEvaluation = GetStrategyEvaluation(includeTestData);
        var signals = includeTestData
            ? GetIntelligenceSignals(status: "needs_ai_review", limit: 1000)
            : GetProductionIntelligenceSignals(status: "needs_ai_review", limit: 1000);
        var requiredRoles = new[] { "ceo", "hr", "data_analyst", "risk_officer" };
        var existingRoles = employees.Select(employee => NormalizeEmployeeRole(employee.Role)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var views = new List<WorldCupEmployeeStatusView>();

        foreach (var employee in employees)
        {
            assignmentsByEmployee.TryGetValue(employee.Id, out var assignment);
            var team = assignment != null && teamsById.TryGetValue(assignment.ObjectId, out var foundTeam) ? foundTeam : null;
            var employeeArtifacts = artifacts.Where(artifact => artifact.OwnerEmployeeId == employee.Id).ToList();
            llmUsageByEmployee.TryGetValue(employee.Id, out var llmUsage);
            var lifecycleMemory = ResolveLatestLifecycleMemory(memories, employee.Id, team?.Id);
            var assignedWorkflows = workflows.Where(workflow =>
                workflow.ObjectId == team?.Id
                || workflow.MetadataJson.Contains(employee.Id, StringComparison.OrdinalIgnoreCase)).ToList();
            views.Add(new WorldCupEmployeeStatusView
            {
                EmployeeId = employee.Id,
                Name = employee.Name,
                Role = NormalizeEmployeeRole(employee.Role),
                DisplayStatus = ResolveDisplayStatus(employee, team, workflows),
                TeamId = team?.Id,
                TeamName = team?.DisplayName,
                TeamStatus = team?.Status,
                CurrentTaskCount = assignedWorkflows.Count(workflow => workflow.Status is "pending" or "running" or "needs_review"),
                CompletedTaskCount = assignedWorkflows.Count(workflow => workflow.Status == "completed"),
                LatestReportArtifactId = employeeArtifacts.OrderByDescending(artifact => artifact.CreatedAt).FirstOrDefault()?.Id,
                PendingActionableSignals = team == null ? 0 : signals.Count(signal => signal.ObjectId == team.Id && signal.SignalType is "injury_risk" or "lineup_news"),
                LlmReportsCreated = employeeArtifacts.Count(artifact => artifact.Title.Contains("DeepSeek", StringComparison.OrdinalIgnoreCase)),
                Accuracy = team == null ? null : ResolveTeamAccuracy(strategyEvaluation, team.DisplayName),
                TokenConsumed = (llmUsage?.PromptTokens ?? 0) + (llmUsage?.CompletionTokens ?? 0),
                EstimatedCostUsd = llmUsage?.EstimatedCostUsd ?? 0,
                EliminatedAt = lifecycleMemory?.CreatedAt,
                EliminationReason = lifecycleMemory?.Summary
            });
        }

        var result = new WorldCupEmployeeStatusSummaryResult
        {
            Total = employees.Count,
            Active = views.Count(view => view.DisplayStatus == "active" || view.DisplayStatus == "working"),
            Inactive = views.Count(view => view.DisplayStatus == "inactive"),
            Standby = views.Count(view => view.DisplayStatus == "standby"),
            Hibernated = views.Count(view => view.DisplayStatus == "hibernated"),
            MissingRoles = requiredRoles.Where(role => !existingRoles.Contains(role)).ToList(),
            Employees = views.OrderBy(view => view.Role).ThenBy(view => view.Name).ToList()
        };
        if (result.Total < 51) result.Notes.Add($"Expected at least 51 implemented employees, got {result.Total}.");
        if (views.Count(view => view.TeamId != null) < 48) result.Notes.Add("Not every football team has a visible employee assignment.");
        if (result.MissingRoles.Count > 0) result.Notes.Add($"Missing target roles: {string.Join(", ", result.MissingRoles)}.");
        result.Passed = result.Total >= 51 && views.Count(view => view.TeamId != null) >= 48;
        return result;
    }

    public WorldCupPredictionAccuracyResult GetPredictionAccuracy(bool includeTestData = false)
    {
        var evaluation = GetStrategyEvaluation(includeTestData);
        var matchesById = (includeTestData ? GetMatches() : GetProductionMatches()).ToDictionary(match => match.Id, StringComparer.OrdinalIgnoreCase);
        var result = new WorldCupPredictionAccuracyResult
        {
            Overall = evaluation,
            SampleStatus = evaluation.ReviewedMatches == 0 ? "no_reviewed_matches" : "has_reviewed_matches"
        };

        result.ByStage = evaluation.Items
            .GroupBy(item => matchesById.TryGetValue(item.MatchId, out var match) ? match.Stage : "unknown")
            .Select(group => BuildAccuracyGroup(group.Key, group.Key, group))
            .ToList();
        result.ByStrategy = [BuildAccuracyGroup(evaluation.StrategyVersion, evaluation.StrategyVersion, evaluation.Items)];
        result.ByTeam = evaluation.Items
            .SelectMany(item => new[]
            {
                new { Team = item.HomeTeam, Item = item },
                new { Team = item.AwayTeam, Item = item }
            })
            .GroupBy(row => row.Team)
            .Select(group => BuildAccuracyGroup(group.Key, group.Key, group.Select(row => row.Item)))
            .OrderByDescending(group => group.Total)
            .ThenBy(group => group.Label)
            .Take(48)
            .ToList();

        var ordered = evaluation.Items.OrderBy(item => item.ReviewedAt).ThenBy(item => item.MatchId).ToList();
        for (var i = 0; i < ordered.Count; i++)
        {
            var slice = ordered.Take(i + 1).ToList();
            result.Trend.Add(new WorldCupPredictionAccuracyTrendItem
            {
                MatchId = ordered[i].MatchId,
                ReviewedAt = ordered[i].ReviewedAt,
                CumulativeAccuracy = slice.Count(item => item.Hit) / (double)slice.Count,
                CumulativeBrierScore = slice.Average(item => item.BrierScore)
            });
        }

        if (evaluation.ReviewedMatches == 0) result.Notes.Add("No finished match reviews are available yet; show empty-state copy instead of fake accuracy.");
        result.Passed = evaluation.ReviewedMatches == 0 || (evaluation.HitRate >= 0 && evaluation.HitRate <= 1 && evaluation.AverageBrierScore >= 0);
        return result;
    }

    public MemorySummaryResult GetMemorySummary(string? objectId = null, string? ownerId = null)
    {
        var memories = GetMemories(objectId, ownerId);
        var result = new MemorySummaryResult
        {
            Total = memories.Count,
            ByType = memories
                .GroupBy(memory => memory.MemoryType, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase),
            Recent = memories.OrderByDescending(memory => memory.CreatedAt).Take(12).ToList(),
            Important = memories.OrderByDescending(memory => memory.Importance).ThenByDescending(memory => memory.Confidence).Take(12).ToList(),
            Passed = true
        };
        if (result.Total == 0) result.Notes.Add("No memories matched the requested filters.");
        return result;
    }

    public WorldCupMatchBoardResult GetMatchBoard(string? stage = null, string? group = null, string? status = null, int limit = 300, bool includeTestData = false)
    {
        var matches = includeTestData ? GetMatches() : GetProductionMatches();
        var teamsById = (includeTestData ? GetWatchObjects() : GetProductionWatchObjects())
            .Where(team => team.Type == "football_team")
            .ToDictionary(team => team.Id, StringComparer.OrdinalIgnoreCase);
        var latestPredictions = (includeTestData ? GetBaselinePredictions() : GetProductionBaselinePredictions())
            .GroupBy(prediction => prediction.MatchId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(prediction => prediction.CreatedAt).First(), StringComparer.OrdinalIgnoreCase);
        var workflowsByMatch = (includeTestData ? GetWorkflowRuns() : GetProductionWorkflowRuns())
            .Where(workflow => !string.IsNullOrWhiteSpace(workflow.MatchId))
            .GroupBy(workflow => workflow.MatchId!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);
        var artifactsByMatch = (includeTestData ? GetArtifacts() : GetProductionArtifacts())
            .Where(artifact => !string.IsNullOrWhiteSpace(artifact.MetadataJson))
            .GroupBy(artifact => TryReadMetadataString(artifact.MetadataJson, "match_id") ?? "")
            .Where(group => !string.IsNullOrWhiteSpace(group.Key))
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        var snapshots = includeTestData ? GetDataSnapshots() : GetProductionDataSnapshots();

        var filtered = matches.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(stage)) filtered = filtered.Where(match => match.Stage.Equals(stage, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(group)) filtered = filtered.Where(match => match.GroupName.Equals(group, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(status)) filtered = filtered.Where(match => match.Status.Equals(status, StringComparison.OrdinalIgnoreCase));

        var result = new WorldCupMatchBoardResult
        {
            Total = matches.Count,
            Stages = matches.Select(match => match.Stage).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value).ToList(),
            Groups = matches.Select(match => match.GroupName).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value).ToList()
        };

        foreach (var match in filtered.Take(Math.Clamp(limit, 1, 500)))
        {
            var homeTeam = ResolveMatchBoardTeam(teamsById, match.HomeObjectId);
            var awayTeam = ResolveMatchBoardTeam(teamsById, match.AwayObjectId);
            latestPredictions.TryGetValue(match.Id, out var prediction);
            workflowsByMatch.TryGetValue(match.Id, out var matchWorkflows);
            artifactsByMatch.TryGetValue(match.Id, out var artifactCount);
            result.Items.Add(new WorldCupMatchBoardItem
            {
                Match = match,
                HomeTeam = homeTeam,
                AwayTeam = awayTeam,
                LatestPrediction = prediction,
                WorkflowCount = matchWorkflows?.Count ?? 0,
                ArtifactCount = artifactCount,
                SnapshotCount = snapshots.Count(snapshot => snapshot.MatchId == match.Id),
                HasResult = match.Status == "finished" && match.HomeScore != null && match.AwayScore != null,
                DisplayTitle = $"{homeTeam?.DisplayName ?? match.HomeObjectId} vs {awayTeam?.DisplayName ?? match.AwayObjectId}"
            });
        }

        result.Filtered = result.Items.Count;
        var expectedMatches = includeTestData ? 100 : 72;
        if (result.Total < expectedMatches) result.Notes.Add($"Expected {expectedMatches}+ match records for this data view, got {result.Total}.");
        if (result.Items.Any(item =>
                IsUnresolvedConcreteTeamId(item.Match.HomeObjectId, item.HomeTeam)
                || IsUnresolvedConcreteTeamId(item.Match.AwayObjectId, item.AwayTeam)))
        {
            result.Notes.Add("Some matches reference unresolved non-placeholder teams.");
        }
        result.Passed = result.Total >= expectedMatches && result.Items.Count > 0;
        return result;
    }

    public WorldCupBffHarnessResult RunWorldCupBffHarness()
    {
        SeedDemoWorldCupCompany();
        AddSystemEventLog(new WorldCupSystemEventLog
        {
            EventType = "bff_harness_event",
            Category = "harness",
            Source = "bff_harness",
            Title = "BFF harness activity",
            Message = $"bff_harness_{Guid.NewGuid():N}"
        });

        var dashboard = GetCompanyDashboard(20, includeTestData: true);
        var operations = GetOperationsSummary(includeTestData: true);
        var employeeSummary = GetEmployeeStatusSummary(includeTestData: true);
        var predictionAccuracy = GetPredictionAccuracy(includeTestData: true);
        var memorySummary = GetMemorySummary();
        var matchBoard = GetMatchBoard(limit: 500, includeTestData: true);
        var employeeFieldsEnriched = employeeSummary.Employees.Any(employee =>
            employee.CompletedTaskCount >= 0
            && employee.TokenConsumed >= 0
            && employee.EstimatedCostUsd >= 0);
        var result = new WorldCupBffHarnessResult
        {
            DashboardPassed = dashboard.Passed,
            OperationsPassed = operations.Matches >= 100
                && operations.DataSnapshots > 0
                && operations.WorkflowRuns >= 0
                && operations.Llm.Calls >= 0,
            EmployeeSummaryPassed = employeeSummary.Passed,
            PredictionAccuracyPassed = predictionAccuracy.Passed,
            MemorySummaryPassed = memorySummary.Passed,
            MatchBoardPassed = matchBoard.Passed && matchBoard.Items.Count >= 100 && matchBoard.Items.All(item => item.HomeTeam != null && item.AwayTeam != null),
            FootballTeams = dashboard.Company.TotalTeams,
            Matches = operations.Matches,
            Employees = employeeSummary.Total,
            EmployeeFieldsEnriched = employeeFieldsEnriched,
            MatchBoardItems = matchBoard.Items.Count,
            RecentActivity = dashboard.RecentActivity.Count,
            LlmCalls = operations.Llm.Calls
        };

        if (!result.DashboardPassed) result.Notes.AddRange(dashboard.Notes.Select(note => $"dashboard: {note}"));
        if (!result.OperationsPassed) result.Notes.Add("Operations summary did not include expected counts.");
        if (!result.EmployeeSummaryPassed) result.Notes.AddRange(employeeSummary.Notes.Select(note => $"employees: {note}"));
        if (!result.PredictionAccuracyPassed) result.Notes.AddRange(predictionAccuracy.Notes.Select(note => $"prediction_accuracy: {note}"));
        if (!result.MemorySummaryPassed) result.Notes.AddRange(memorySummary.Notes.Select(note => $"memory_summary: {note}"));
        if (!result.MatchBoardPassed) result.Notes.AddRange(matchBoard.Notes.Select(note => $"match_board: {note}"));
        if (!result.EmployeeFieldsEnriched) result.Notes.Add("Employee summary did not include enriched computed fields.");
        result.Passed = result.DashboardPassed
            && result.OperationsPassed
            && result.EmployeeSummaryPassed
            && result.PredictionAccuracyPassed
            && result.MemorySummaryPassed
            && result.MatchBoardPassed
            && result.EmployeeFieldsEnriched;
        return result;
    }

    private WorldCupCompanySummary BuildCompanySummary(bool includeTestData = false)
    {
        var teams = (includeTestData ? GetWatchObjects() : GetProductionWatchObjects()).Where(item => item.Type == "football_team").ToList();
        var employees = includeTestData ? GetEmployees() : GetProductionEmployees();
        var requiredRoles = new[] { "ceo", "hr", "data_analyst", "risk_officer" };
        var existingRoles = employees.Select(employee => NormalizeEmployeeRole(employee.Role)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new WorldCupCompanySummary
        {
            Stage = InferTournamentStage(includeTestData ? GetMatches() : GetProductionMatches()),
            TotalTeams = teams.Count,
            ActiveTeams = teams.Count(team => team.Status == "active"),
            EliminatedTeams = teams.Count(team => team.Status is "inactive" or "eliminated" or "hibernated"),
            ImplementedEmployees = employees.Count,
            ActiveEmployees = employees.Count(employee => employee.Status == "active"),
            InactiveEmployees = employees.Count(employee => employee.Status != "active"),
            MissingRoles = requiredRoles.Where(role => !existingRoles.Contains(role)).ToList()
        };
    }

    private DataSnapshotQualityResult BuildOperationalSnapshotQuality()
    {
        var configSources = AutoCollectionService.LoadAutoCollectionConfig()
            .Sources
            .Where(source => source.Enabled)
            .Select(source => string.IsNullOrWhiteSpace(source.SourceName) ? source.Id : source.SourceName)
            .ToList();
        var bootstrapSources = new[] { "worldcup26_bootstrap", "fixturedownload_bootstrap" };
        var sourceNames = configSources.Concat(bootstrapSources);
        var result = AuditDataSnapshotQualityForSources(sourceNames, 250);
        if (result.SnapshotsChecked == 0)
        {
            result = AuditDataSnapshotQuality(limit: 300);
        }
        return result;
    }

    private WorldCupAutoCollectionSummary BuildAutoCollectionSummary()
    {
        var config = AutoCollectionService.LoadAutoCollectionConfig();
        var last = AppContext.LastAutoCollectionRun;
        return new WorldCupAutoCollectionSummary
        {
            Enabled = config.Enabled,
            IntervalMinutes = config.IntervalMinutes,
            EnabledSources = config.Sources.Count(source => source.Enabled),
            LastRunPassed = last?.Passed,
            LastRunAt = string.IsNullOrWhiteSpace(last?.CompletedAt) ? last?.StartedAt : last.CompletedAt,
            LastImported = last?.Imported ?? 0,
            LastDuplicates = last?.SkippedDuplicates ?? 0
        };
    }

    private static string InferTournamentStage(IReadOnlyList<WorldCupMatch> matches)
    {
        if (matches.Count == 0) return "pre_tournament";
        
        var now = DateTime.UtcNow;
        
        // 检查是否有决赛已完成
        if (matches.Any(match => match.Status == "finished" && match.Stage == "final")) 
            return "post_review";
        
        // 检查是否有进行中的比赛
        var liveMatches = matches.Where(m => m.Status == "live").ToList();
        if (liveMatches.Any())
        {
            var stage = liveMatches.First().Stage;
            return stage == "group" ? "group_stage" : "knockout_stage";
        }

        // 检查是否有已完成的淘汰赛
        if (matches.Any(match => match.Status == "finished" && !match.Stage.Contains("group", StringComparison.OrdinalIgnoreCase))) 
            return "knockout_stage";
        
        // 检查是否有已完成的小组赛
        if (matches.Any(match => match.Status == "finished")) 
            return "group_stage";
        
        // 检查下一场比赛
        var nextMatch = matches.Where(m => m.Status == "scheduled").OrderBy(m => m.KickoffTime).FirstOrDefault();
        if (nextMatch != null && DateTime.TryParse(nextMatch.KickoffTime, out var kickoffTime))
        {
            var timeUntilKickoff = kickoffTime - now;
            // 如果比赛在一周内开始，显示对应阶段
            if (timeUntilKickoff.TotalDays < 7)
            {
                return nextMatch.Stage == "group" ? "group_stage" : "knockout_stage";
            }
        }
        
        return "pre_tournament";
    }

    private static LlmCallRecord ReadLlmCall(SqliteDataReader reader)
    {
        return new LlmCallRecord
        {
            Id = reader.GetString(0),
            AgentTaskId = reader.IsDBNull(1) ? null : reader.GetString(1),
            EmployeeId = reader.IsDBNull(2) ? null : reader.GetString(2),
            ModelName = reader.GetString(3),
            Provider = reader.GetString(4),
            PromptVersion = reader.GetString(5),
            PromptTokens = reader.GetInt32(6),
            CompletionTokens = reader.GetInt32(7),
            CostEstimate = reader.GetDouble(8),
            RequestHash = reader.GetString(9),
            ResponseHash = reader.GetString(10),
            Status = reader.GetString(11),
            ErrorMessage = reader.IsDBNull(12) ? null : reader.GetString(12),
            CreatedAt = reader.GetString(13)
        };
    }

    private static string? TryReadMetadataString(string metadataJson, string propertyName)
    {
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(metadataJson);
            return document.RootElement.TryGetProperty(propertyName, out var value) && value.ValueKind == System.Text.Json.JsonValueKind.String
                ? value.GetString()
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static MemoryRecord? ResolveLatestLifecycleMemory(
        IReadOnlyList<MemoryRecord> memories,
        string employeeId,
        string? teamId)
    {
        return memories
            .Where(memory => memory.SourceType == "match_lifecycle"
                && (memory.OwnerId == employeeId || (!string.IsNullOrWhiteSpace(teamId) && memory.ObjectId == teamId)))
            .OrderByDescending(memory => memory.CreatedAt)
            .FirstOrDefault();
    }

    private static double? ResolveTeamAccuracy(StrategyEvaluationSummary evaluation, string teamName)
    {
        var items = evaluation.Items
            .Where(item => item.HomeTeam.Equals(teamName, StringComparison.OrdinalIgnoreCase)
                || item.AwayTeam.Equals(teamName, StringComparison.OrdinalIgnoreCase))
            .ToList();
        return items.Count == 0 ? null : items.Count(item => item.Hit) / (double)items.Count;
    }

    private static WorldCupWatchObject? ResolveMatchBoardTeam(
        IReadOnlyDictionary<string, WorldCupWatchObject> teamsById,
        string objectId)
    {
        if (teamsById.TryGetValue(objectId, out var team)) return team;
        if (!objectId.Equals("slot_tba", StringComparison.OrdinalIgnoreCase)) return null;
        return new WorldCupWatchObject
        {
            Id = "slot_tba",
            Type = "placeholder_team",
            Symbol = "TBD",
            Name = "To Be Determined",
            DisplayName = "TBD",
            Status = "pending",
            MetadataJson = """{"placeholder":true}"""
        };
    }

    private static bool IsUnresolvedConcreteTeamId(string objectId, WorldCupWatchObject? team)
    {
        return team == null && !objectId.Equals("slot_tba", StringComparison.OrdinalIgnoreCase);
    }

    private static WorldCupPredictionAccuracyGroup BuildAccuracyGroup(
        string key,
        string label,
        IEnumerable<StrategyEvaluationItem> items)
    {
        var list = items.ToList();
        return new WorldCupPredictionAccuracyGroup
        {
            Key = string.IsNullOrWhiteSpace(key) ? "unknown" : key,
            Label = string.IsNullOrWhiteSpace(label) ? "unknown" : label,
            Total = list.Count,
            Correct = list.Count(item => item.Hit),
            Accuracy = list.Count == 0 ? 0 : list.Count(item => item.Hit) / (double)list.Count,
            AverageBrierScore = list.Count == 0 ? 0 : list.Average(item => item.BrierScore)
        };
    }

    private static string ResolveDisplayStatus(
        WorldCupEmployee employee,
        WorldCupWatchObject? assignedTeam,
        IReadOnlyList<WorkflowRunRecord> workflows)
    {
        if (employee.Status is "offboarded" or "inactive") return "inactive";
        if (assignedTeam?.Status is "eliminated" or "hibernated") return "hibernated";
        if (workflows.Any(workflow =>
                workflow.Status is "pending" or "running" or "needs_review"
                && (workflow.ObjectId == assignedTeam?.Id
                    || workflow.MetadataJson.Contains(employee.Id, StringComparison.OrdinalIgnoreCase))))
        {
            return "working";
        }

        return assignedTeam == null ? "standby" : employee.Status;
    }

#if false
    private static string NormalizeEmployeeRole(string role)
    {
        return role.Trim().ToLowerInvariant() switch
        {
            "ceo" => "ceo",
            "数据分析师" => "data_analyst",
            "data analyst" => "data_analyst",
            "data_analyst" => "data_analyst",
            "风险官" => "risk_officer",
            "risk officer" => "risk_officer",
            "risk_officer" => "risk_officer",
            "hr" => "hr",
            "人事" => "hr",
            "球队研究员" => "team_researcher",
            "team researcher" => "team_researcher",
            "team_researcher" => "team_researcher",
            _ => role.Trim().ToLowerInvariant()
        };
    }

#endif

    private static string NormalizeEmployeeRole(string role)
    {
        var normalized = role.Trim().ToLowerInvariant();
        return normalized switch
        {
            "ceo" => "ceo",
            "\u6570\u636e\u5206\u6790\u5e08" => "data_analyst",
            "data analyst" => "data_analyst",
            "data_analyst" => "data_analyst",
            "\u98ce\u9669\u5b98" => "risk_officer",
            "risk officer" => "risk_officer",
            "risk_officer" => "risk_officer",
            "hr" => "hr",
            "\u4eba\u4e8b" => "hr",
            "\u7403\u961f\u7814\u7a76\u5458" => "team_researcher",
            "team researcher" => "team_researcher",
            "team_researcher" => "team_researcher",
            _ => normalized
        };
    }

    private static WorldCupActivityFeedItem ToActivityFeedItem(WorldCupSystemEventLog log)
    {
        return new WorldCupActivityFeedItem
        {
            Id = log.Id,
            Time = log.CreatedAt,
            Type = log.EventType,
            Category = log.Category,
            Severity = log.Severity,
            FromEmployeeId = log.EmployeeId,
            ToEmployeeId = ResolveActivityTarget(log),
            ObjectId = log.ObjectId,
            MatchId = log.MatchId,
            WorkflowRunId = log.WorkflowRunId,
            ArtifactId = log.ArtifactId,
            Title = log.Title,
            Message = log.Message,
            AnimationHint = ResolveAnimationHint(log)
        };
    }

    private static string? ResolveActivityTarget(WorldCupSystemEventLog log)
    {
        return log.Category is "employee" or "llm" or "workflow" ? "emp_ceo" : null;
    }

    private static string ResolveAnimationHint(WorldCupSystemEventLog log)
    {
        return log.Category switch
        {
            "employee" => "bubble_to_ceo",
            "workflow" => "workflow_pulse",
            "llm" => "llm_flash",
            "data" => "data_refresh",
            "intelligence" => "signal_ping",
            _ => "log"
        };
    }
}

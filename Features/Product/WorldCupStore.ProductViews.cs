using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PiPiClaw.Team;

public sealed partial class WorldCupStore
{
    public ProductOverviewResult GetProductOverview(string? selectedMatchId = null, int queueLimit = 24)
    {
        var queue = GetProductMatchQueue(queueLimit);
        var selectedId = !string.IsNullOrWhiteSpace(selectedMatchId)
            ? selectedMatchId!
            : ResolveDefaultSelectedProductMatch(queue);
        var detail = string.IsNullOrWhiteSpace(selectedId) ? null : GetProductMatchDetail(selectedId);
        var operations = GetOperationsSummary(includeTestData: false);
        var dataTrust = GetProductDataTrust();

        var result = new ProductOverviewResult
        {
            Navigation = ["比赛预测", "球队研究室", "数据可信度", "研报归档"],
            SelectedMatchId = selectedId,
            Queue = queue,
            FeaturedMatch = detail,
            DataTrust = dataTrust.Take(6).ToList(),
            AutoCollection = BuildProductAutoCollectionStatus(),
            LlmUsage = operations.Llm,
            SummaryMetrics =
            [
                new ProductMetricView { Label = "生产球队", Value = operations.Matches > 0 ? "48 支" : "未就绪", Tone = operations.Matches > 0 ? "good" : "warning" },
                new ProductMetricView { Label = "赛程记录", Value = $"{operations.Matches} 场", Tone = operations.Matches >= 72 ? "good" : "warning" },
                new ProductMetricView { Label = "预测记录", Value = $"{operations.BaselinePredictions} 条", Tone = operations.BaselinePredictions >= operations.Matches && operations.Matches > 0 ? "good" : "warning" },
                new ProductMetricView { Label = "证据快照", Value = $"{operations.DataSnapshots} 条", Tone = operations.DataSnapshots > 0 ? "good" : "warning" },
                new ProductMetricView { Label = "模型调用", Value = $"{operations.Llm.Calls} 次", Tone = operations.Llm.FailedCalls == 0 ? "good" : "warning" }
            ]
        };

        if (queue.Count == 0) result.Notes.Add("当前没有可展示的生产赛程，请先运行公开数据引导或自动采集。");
        if (operations.BaselinePredictions < operations.Matches) result.Notes.Add("部分比赛还没有基线预测，可执行生产预测刷新。");
        result.Passed = queue.Count > 0 && detail != null;
        return result;
    }

    private static string ResolveDefaultSelectedProductMatch(IReadOnlyList<ProductMatchQueueItem> queue)
    {
        return queue
            .Where(item => item.Status is "running" or "live" or "starting_soon")
            .OrderBy(item => ParseDateOrMax(item.KickoffTime))
            .Select(item => item.MatchId)
            .FirstOrDefault()
            ?? queue
                .Where(item => item.Status == "scheduled")
                .OrderBy(item => ParseDateOrMax(item.KickoffTime))
                .Select(item => item.MatchId)
                .FirstOrDefault()
            ?? queue.FirstOrDefault()?.MatchId
            ?? "";
    }

    public List<ProductMatchQueueItem> GetProductMatchQueue(int limit = 72)
    {
        var now = DateTimeOffset.Now;
        var matches = GetProductionMatches()
            .OrderBy(match => ResolveQueuePriority(match, now))
            .ThenBy(match => ResolveQueueDateSort(match, now))
            .ThenBy(match => match.Id, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Clamp(limit, 1, 200))
            .ToList();
        var teams = GetProductionWatchObjects()
            .Where(item => item.Type == "football_team")
            .ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
        var predictions = GetProductionBaselinePredictions()
            .GroupBy(item => item.MatchId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(item => item.CreatedAt, StringComparer.OrdinalIgnoreCase).First(),
                StringComparer.OrdinalIgnoreCase);
        var snapshots = GetProductionDataSnapshots();
        var memories = GetMemories();
        var artifacts = GetProductionArtifacts();

        return matches
            .Select(match => BuildProductMatchQueueItem(match, teams, predictions, snapshots, memories, artifacts))
            .ToList();
    }

    public ProductTeamResearchResult GetProductTeamResearch(int matchLimit = 200)
    {
        var queue = GetProductMatchQueue(matchLimit);
        var sourceMatches = GetProductionMatches()
            .ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
        var teams = GetProductionWatchObjects()
            .Where(item => item.Type == "football_team")
            .OrderBy(item => ReadString(ParseObject(item.MetadataJson), "group") ?? "", StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => ReadInt(ParseObject(item.MetadataJson), "fifa_rank") ?? 999)
            .ThenBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var snapshots = GetProductionDataSnapshots();
        var memories = GetMemories();
        var artifacts = GetProductionArtifacts();
        var signalsByObjectId = GetProductionIntelligenceSignals(limit: 5000)
            .Where(item => !string.IsNullOrWhiteSpace(item.ObjectId))
            .GroupBy(item => item.ObjectId!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(item => item.CreatedAt, StringComparer.OrdinalIgnoreCase)
                    .Take(20)
                    .ToList(),
                StringComparer.OrdinalIgnoreCase);
        var eventsByObjectId = GetProductionSystemEventLogs(limit: 5000)
            .Where(item => !string.IsNullOrWhiteSpace(item.ObjectId))
            .GroupBy(item => item.ObjectId!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(item => item.CreatedAt, StringComparer.OrdinalIgnoreCase)
                    .Take(8)
                    .ToList(),
                StringComparer.OrdinalIgnoreCase);

        var result = new ProductTeamResearchResult();
        foreach (var team in teams)
        {
            var teamView = ToProductTeam(team, team.Id);
            var teamMatches = queue
                .Where(item => string.Equals(item.Home.Id, team.Id, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(item.Away.Id, team.Id, StringComparison.OrdinalIgnoreCase))
                .OrderBy(item => ParseDateOrMax(item.KickoffTime))
                .ToList();

            var teamEvidence = snapshots
                .Where(item => string.Equals(item.ObjectId, team.Id, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(item => item.CapturedAt, StringComparer.OrdinalIgnoreCase)
                .Take(16)
                .Select(ToProductEvidence)
                .ToList();

            signalsByObjectId.TryGetValue(team.Id, out var rawSignals);
            var signals = (rawSignals ?? [])
                .OrderByDescending(item => item.CreatedAt, StringComparer.OrdinalIgnoreCase)
                .Take(8)
                .Select(ToProductEvidence)
                .ToList();
            teamEvidence.AddRange(signals);

            var teamMemories = memories
                .Where(item => string.Equals(item.ObjectId, team.Id, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(item => item.Importance)
                .ThenByDescending(item => item.Confidence)
                .ThenByDescending(item => item.CreatedAt, StringComparer.OrdinalIgnoreCase)
                .Take(10)
                .Select(ToProductMemory)
                .ToList();

            eventsByObjectId.TryGetValue(team.Id, out var rawEvents);
            var teamActivities = (rawEvents ?? [])
                .Select(ToProductActivity)
                .ToList();

            ProductTeamProfileView? profile = null;
            var seedMatch = teamMatches
                .Select(item => sourceMatches.TryGetValue(item.MatchId, out var match) ? match : null)
                .FirstOrDefault(item => item != null);
            if (seedMatch != null)
            {
                profile = BuildTeamProfile(team, team.Id, teamEvidence, seedMatch);
            }

            var peakProbability = teamMatches.Count == 0
                ? 0
                : teamMatches.Max(item => string.Equals(item.Home.Id, team.Id, StringComparison.OrdinalIgnoreCase)
                    ? item.Prediction.HomeWin
                    : item.Prediction.AwayWin);
            var reportCount = artifacts.Count(artifact => teamMatches.Any(match =>
                sourceMatches.TryGetValue(match.MatchId, out var sourceMatch) && sourceMatch != null && IsArtifactRelatedToMatch(artifact, sourceMatch)));

            var item = new ProductTeamResearchItem
            {
                Team = teamView,
                Employee = ResolveProductEmployee(team.Id),
                Profile = profile,
                Matches = teamMatches.Take(8).ToList(),
                EvidenceCount = snapshots.Count(snapshot => string.Equals(snapshot.ObjectId, team.Id, StringComparison.OrdinalIgnoreCase))
                    + signals.Count,
                MemoryCount = memories.Count(memory => string.Equals(memory.ObjectId, team.Id, StringComparison.OrdinalIgnoreCase)),
                ReportCount = reportCount,
                PeakProbability = Math.Round(peakProbability, 4),
                Evidence = teamEvidence
                    .GroupBy(item => $"{item.Source}|{item.Kind}|{item.Summary}", StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.OrderByDescending(item => item.Confidence).First())
                    .Take(10)
                    .ToList(),
                Memories = teamMemories,
                RecentActivity = teamActivities
            };

            if (item.Evidence.Count == 0) item.DataNotes.Add("暂无可追溯球队证据，当前画像主要来自赛程、排名和历史模型变量。");
            if (item.Matches.Count == 0) item.DataNotes.Add("暂无关联赛程，等待公开赛程源更新。");
            result.Teams.Add(item);
        }

        result.Passed = result.Teams.Count > 0;
        if (!result.Passed) result.Notes.Add("暂无球队研究室数据，请先运行世界杯公开数据采集。");
        return result;
    }

    public ProductMatchDetail? GetProductMatchDetail(string matchId)
    {
        var match = GetProductionMatches().FirstOrDefault(item => item.Id.Equals(matchId, StringComparison.OrdinalIgnoreCase));
        if (match == null) return null;

        var teams = GetProductionWatchObjects()
            .Where(item => item.Type == "football_team")
            .ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
        var predictions = GetProductionBaselinePredictions(match.Id)
            .OrderByDescending(item => item.CreatedAt, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var latestPrediction = predictions.FirstOrDefault();
        var snapshots = GetProductionDataSnapshots();
        var artifacts = GetProductionArtifacts();
        var memories = GetMemories();
        var queueItem = BuildProductMatchQueueItem(
            match,
            teams,
            latestPrediction == null
                ? new Dictionary<string, BaselinePredictionRecord>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, BaselinePredictionRecord>(StringComparer.OrdinalIgnoreCase) { [match.Id] = latestPrediction },
            snapshots,
            memories,
            artifacts);

        teams.TryGetValue(match.HomeObjectId, out var home);
        teams.TryGetValue(match.AwayObjectId, out var away);
        var homeEmployee = ResolveProductEmployee(match.HomeObjectId);
        var awayEmployee = ResolveProductEmployee(match.AwayObjectId);

        var evidence = new List<ProductEvidenceView>();
        foreach (var snapshot in snapshots
            .Where(item => item.MatchId == match.Id || item.ObjectId == match.HomeObjectId || item.ObjectId == match.AwayObjectId)
            .Where(item => IsProductSnapshotRelevantToMatch(item, home, away))
            .OrderByDescending(item => item.CapturedAt, StringComparer.OrdinalIgnoreCase)
            .Take(14))
        {
            evidence.Add(ToProductEvidence(snapshot, home, away));
        }

        var signals = GetProductionIntelligenceSignals(matchId: match.Id, limit: 80)
            .Concat(GetProductionIntelligenceSignals(objectId: match.HomeObjectId, limit: 40))
            .Concat(GetProductionIntelligenceSignals(objectId: match.AwayObjectId, limit: 40))
            .GroupBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Where(item => IsProductSignalRelevantToMatch(item, home, away))
            .OrderByDescending(item => item.CreatedAt, StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToList();
        evidence.AddRange(signals.Select(ToProductEvidence));

        var relevantMemories = memories
            .Where(item =>
                item.ObjectId == match.HomeObjectId
                || item.ObjectId == match.AwayObjectId
                || item.SourceId == match.Id
                || item.OwnerId == homeEmployee?.Id
                || item.OwnerId == awayEmployee?.Id)
            .OrderByDescending(item => item.Importance)
            .ThenByDescending(item => item.Confidence)
            .ThenByDescending(item => item.CreatedAt, StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .Select(ToProductMemory)
            .ToList();

        var recentActivity = GetProductionSystemEventLogs(matchId: match.Id, limit: 10)
            .Select(ToProductActivity)
            .ToList();
        var latestReport = ResolveLatestReportSummary(artifacts, match.Id, match.HomeObjectId, match.AwayObjectId);

        var dataCoverage = BuildProductDataCoverage(match, home, away, snapshots, evidence);
        var marketSignals = BuildProductMarketSignals(match, latestPrediction, snapshots);
        if (latestPrediction != null && marketSignals.Count > 0)
        {
            queueItem.Prediction.BettingAdvice = BuildMarketAwareBettingAdvice(
                queueItem.Prediction.BettingAdvice,
                marketSignals);
            queueItem.Prediction.BettingAdvice = ApplyQualityGateToBettingAdvice(
                queueItem.Prediction.BettingAdvice,
                queueItem.Prediction.QualityGate);
        }

        var detail = new ProductMatchDetail
        {
            QueueItem = queueItem,
            HomeEmployee = homeEmployee,
            AwayEmployee = awayEmployee,
            HomeProfile = BuildTeamProfile(home, match.HomeObjectId, evidence, match),
            AwayProfile = BuildTeamProfile(away, match.AwayObjectId, evidence, match),
            Evidence = evidence
                .GroupBy(item => $"{item.Source}|{item.Kind}|{item.Summary}", StringComparer.OrdinalIgnoreCase)
                .Select(group => group.OrderByDescending(item => item.Confidence).First())
                .OrderByDescending(item => item.Confidence)
                .ThenByDescending(item => item.CapturedAt, StringComparer.OrdinalIgnoreCase)
                .Take(18)
                .ToList(),
            Memories = relevantMemories,
            RecentActivity = recentActivity,
            LatestReport = latestReport,
            ModelReview = BuildProductModelReview(match, home, away, latestPrediction, evidence.Count, relevantMemories.Count),
            Metrics = BuildProductMetrics(match, home, away, latestPrediction, evidence.Count, relevantMemories.Count),
            DataCoverage = dataCoverage,
            PrematchWatchPlan = BuildPrematchWatchPlan(match, dataCoverage),
            CollectionPriorities = BuildCollectionPriorities(match, queueItem.Prediction.QualityGate, dataCoverage),
            MarketSignals = marketSignals,
            PredictionRule = BuildPredictionRule(latestPrediction)
        };

        detail.Risks = BuildProductRisks(detail);
        detail.DataNotes = BuildProductDataNotes(latestPrediction, detail.Evidence, detail.Memories);
        return detail;
    }

    public List<ProductDataTrustItem> GetProductDataTrust()
    {
        var audit = AuditWorldCupDataReadiness(includeTestData: false);
        return audit.RegisteredSources
            .OrderByDescending(item => item.ReliabilityScore)
            .ThenBy(item => item.RequiresApiKey)
            .ThenBy(item => item.SourceName, StringComparer.OrdinalIgnoreCase)
            .Select(item => new ProductDataTrustItem
            {
                SourceName = item.SourceName,
                Provider = item.Provider,
                AuthorityLabel = TranslateAuthority(item.AuthorityTier),
                StabilityLabel = TranslateStability(item.StabilityTier),
                Enabled = item.Enabled,
                LatestCapturedAt = item.LatestCapturedAt,
                FreshnessLabel = BuildFreshnessLabel(item.LatestCapturedAt, item.RequiresApiKey, item.Snapshots),
                SnapshotCount = item.Snapshots,
                ReliabilityScore = Math.Round(item.ReliabilityScore, 3),
                RequiresApiKey = item.RequiresApiKey,
                BestFor = item.BestFor.Select(TranslateSourceUsage).ToList(),
                NotFor = item.NotFor.Select(TranslateSourceUsage).ToList(),
                Notes = item.Notes
            })
            .ToList();
    }

    public ProductModelHealthResult GetProductModelHealth()
    {
        var result = new ProductModelHealthResult
        {
            StrategyEvaluation = GetStrategyEvaluation(includeTestData: false)
        };
        result.Backtest = GetCachedModelBacktest(out var cachedAt, out var cacheAgeMinutes);
        result.BacktestCachedAt = cachedAt;
        result.BacktestCacheAgeMinutes = cacheAgeMinutes;

        if (result.StrategyEvaluation.ReviewedMatches == 0)
        {
            result.Notes.Add("暂无本届世界杯已完成比赛复盘；比赛结束后会自动写入赛后评估。");
        }
        if (result.Backtest == null)
        {
            result.Notes.Add("暂无缓存回测结果；后台自动采集会定期生成，避免公网用户触发重型任务。");
        }
        else
        {
            result.Notes.Add($"历史回测缓存来自 {result.Backtest.Source}，样本 {result.Backtest.SamplesUsed} 场。");
            if (cacheAgeMinutes is > 360)
            {
                result.Notes.Add($"缓存已生成约 {cacheAgeMinutes / 60} 小时，后台会在下一轮采集后刷新。");
            }
        }

        result.Passed = result.StrategyEvaluation.ReviewedMatches > 0 || result.Backtest != null;
        return result;
    }

    private ProductAutoCollectionStatus BuildProductAutoCollectionStatus()
    {
        var config = AutoCollectionService.LoadAutoCollectionConfig();
        var delay = AutoCollectionService.ResolveNextDelay(config);
        var last = AppContext.CurrentAutoCollectionRun ?? AppContext.LastAutoCollectionRun;
        if (last?.Running == true && DateTime.TryParse(last.StartedAt, out var startedAt))
        {
            last.ElapsedSeconds = Math.Max(0, (int)Math.Round((DateTime.Now - startedAt).TotalSeconds));
        }
        var latestSnapshotAt = GetProductionDataSnapshots()
            .Select(item => item.CapturedAt)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .OrderByDescending(item => item, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault() ?? "";
        var reportsSkippedReason = last?.Notes.FirstOrDefault(note =>
            note.Contains("employee_trigger", StringComparison.OrdinalIgnoreCase)
            || note.Contains("report", StringComparison.OrdinalIgnoreCase)) ?? "";
        var notes = last?.Notes.ToList() ?? [];
        if (last == null && !string.IsNullOrWhiteSpace(latestSnapshotAt))
        {
            notes.Add("当前进程尚未完成新一轮自动采集，状态时间来自数据库中最新生产快照。");
        }

        return new ProductAutoCollectionStatus
        {
            Enabled = config.Enabled,
            AdaptiveScheduleEnabled = config.AdaptiveScheduleEnabled,
            Running = last?.Running ?? false,
            CurrentSourceId = last?.CurrentSourceId ?? "",
            CurrentSourceName = last?.CurrentSourceName ?? "",
            ElapsedSeconds = last?.ElapsedSeconds ?? 0,
            IntervalMinutes = config.IntervalMinutes,
            NextIntervalMinutes = delay.Minutes,
            NextIntervalReason = delay.Reason,
            LastStartedAt = last?.StartedAt ?? "",
            LastCompletedAt = last?.CompletedAt ?? latestSnapshotAt,
            LastPassed = last?.Passed ?? true,
            SourcesChecked = last?.SourcesChecked ?? 0,
            SourcesSucceeded = last?.SourcesSucceeded ?? 0,
            Imported = last?.Imported ?? 0,
            SkippedDuplicates = last?.SkippedDuplicates ?? 0,
            BaselinePredictionsRefreshed = last?.BaselinePredictionsRefreshed ?? 0,
            SignalsCreated = last?.IntelligenceTriage?.SignalsCreated ?? 0,
            ReportsCreated = last?.EmployeeReportTrigger?.ReportsCreated ?? 0,
            ReportsSkippedReason = reportsSkippedReason,
            AutoLlmReportsEnabled = config.AutoLlmReportsEnabled,
            LlmReportIntervalMinutes = config.LlmReportIntervalMinutes,
            MaxLlmReportTeams = config.MaxLlmReportTeams,
            LastLlmReportRunAt = AppContext.LastAutoLlmReportRun?.CompletedAt ?? "",
            LastLlmReportsCreated = AppContext.LastAutoLlmReportRun?.ReportsCreated ?? 0,
            SourceRuns = (last?.SourceRuns ?? [])
                .Select(ToProductAutoCollectionSourceRun)
                .ToList(),
            Notes = notes
        };
    }

    private static ProductAutoCollectionSourceRun ToProductAutoCollectionSourceRun(DataSourceAutoCollectionSourceRun sourceRun)
    {
        var elapsedLabel = sourceRun.ElapsedMs >= 1000
            ? $"{Math.Round(sourceRun.ElapsedMs / 1000.0, 1)} 秒"
            : $"{sourceRun.ElapsedMs} ms";
        var tone = sourceRun.Passed
            ? sourceRun.ElapsedMs > sourceRun.TimeoutSeconds * 1000L * 0.75 ? "warning" : "good"
            : "danger";
        return new ProductAutoCollectionSourceRun
        {
            Id = sourceRun.Id,
            SourceName = sourceRun.SourceName,
            Provider = sourceRun.Provider,
            ElapsedMs = sourceRun.ElapsedMs,
            ElapsedLabel = elapsedLabel,
            TimeoutSeconds = sourceRun.TimeoutSeconds,
            RawItems = sourceRun.RawItems,
            Imported = sourceRun.Imported,
            SkippedDuplicates = sourceRun.SkippedDuplicates,
            BaselinePredictionsRefreshed = sourceRun.BaselinePredictionsRefreshed,
            StatusLabel = sourceRun.Passed ? "成功" : "失败",
            Tone = tone,
            ErrorMessage = sourceRun.ErrorMessage,
            Notes = sourceRun.Notes
        };
    }

    private static string BuildFreshnessLabel(string? latestCapturedAt, bool requiresApiKey, int snapshots)
    {
        if (requiresApiKey && snapshots == 0) return "未启用";
        if (string.IsNullOrWhiteSpace(latestCapturedAt)) return snapshots > 0 ? "有历史快照" : "暂无快照";
        if (!DateTime.TryParse(latestCapturedAt, out var captured)) return "时间待核验";

        var age = DateTime.Now - captured;
        if (age.TotalHours <= 2) return "2 小时内";
        if (age.TotalHours <= 24) return "24 小时内";
        if (age.TotalDays <= 7) return "7 天内";
        return $"{Math.Floor(age.TotalDays)} 天前";
    }

    private static string TranslateSourceUsage(string value)
    {
        return value
            .Replace("official schedule reference", "官方赛程参考")
            .Replace("stadium naming", "场馆命名")
            .Replace("tournament dates", "赛事日期")
            .Replace("current FIFA ranking", "当前 FIFA 排名")
            .Replace("ranking points", "排名积分")
            .Replace("previous rank comparison", "上期排名对比")
            .Replace("team strength baseline", "球队实力基线")
            .Replace("team strength calibration", "球队实力校准")
            .Replace("cross-checking FIFA rank", "交叉校验 FIFA 排名")
            .Replace("longer-horizon relative quality", "长期相对实力")
            .Replace("recent form", "近期状态")
            .Replace("goals for/against trend", "进失球趋势")
            .Replace("sample tournament context", "样本赛事背景")
            .Replace("fixture bootstrap", "赛程初始化")
            .Replace("teams", "球队列表")
            .Replace("stadiums", "场馆")
            .Replace("scores when available", "可用赛果")
            .Replace("team bootstrap", "球队初始化")
            .Replace("FIFA code mapping", "FIFA code 映射")
            .Replace("fixture cross-check", "赛程交叉校验")
            .Replace("version-controlled schedule snapshots", "版本化赛程快照")
            .Replace("calendar export", "日历源")
            .Replace("kickoff time verification", "开球时间校验")
            .Replace("news discovery", "新闻发现")
            .Replace("human/LLM triage candidates", "人工/大模型分拣候选")
            .Replace("source links", "来源链接")
            .Replace("fixtures", "赛程")
            .Replace("competition metadata", "赛事元数据")
            .Replace("market-implied probabilities", "市场隐含概率")
            .Replace("model calibration", "模型校准")
            .Replace("line movement", "赔率变化")
            .Replace("bulk automated scraping", "大规模自动抓取")
            .Replace("injury data", "伤停事实")
            .Replace("lineups", "首发阵容")
            .Replace("odds", "赔率")
            .Replace("tactical style", "战术风格")
            .Replace("market odds", "市场赔率")
            .Replace("official ranking truth", "官方排名真相")
            .Replace("live match updates", "实时比赛更新")
            .Replace("confirmed squads", "确认名单")
            .Replace("current FIFA ranking", "当前 FIFA 排名")
            .Replace("squad quality", "阵容质量")
            .Replace("team strength", "球队实力")
            .Replace("confirmed injury database", "确认伤停库")
            .Replace("automatic team attribution", "自动球队归因")
            .Replace("prediction probability input without review", "未经复核直接进入概率模型")
            .Replace("rich player injuries", "丰富球员伤停")
            .Replace("official truth", "官方事实")
            .Replace("injury confirmation", "伤停确认");
    }

    public ProductAuditResult GetProductAudit(string? matchId = null, string? teamId = null)
    {
        var events = GetProductionSystemEventLogs(matchId: matchId, objectId: teamId, limit: 120)
            .Select(ToProductActivity)
            .ToList();
        var snapshots = GetProductionDataSnapshots(matchId, teamId)
            .Take(60)
            .Select(ToProductEvidence)
            .ToList();
        var memories = GetMemories(teamId)
            .Take(30)
            .Select(ToProductMemory)
            .ToList();

        var result = new ProductAuditResult
        {
            MatchId = matchId,
            TeamId = teamId,
            Events = events,
            Evidence = snapshots,
            Memories = memories,
            Passed = events.Count > 0 || snapshots.Count > 0 || memories.Count > 0
        };
        if (!result.Passed) result.Notes.Add("没有匹配到审计内容。可以先刷新数据源或生成比赛研报。");
        return result;
    }

    private ProductMatchQueueItem BuildProductMatchQueueItem(
        WorldCupMatch match,
        IReadOnlyDictionary<string, WorldCupWatchObject> teams,
        IReadOnlyDictionary<string, BaselinePredictionRecord> predictions,
        IReadOnlyList<DataSnapshotRecord> snapshots,
        IReadOnlyList<MemoryRecord> memories,
        IReadOnlyList<ArtifactRecord> artifacts)
    {
        teams.TryGetValue(match.HomeObjectId, out var home);
        teams.TryGetValue(match.AwayObjectId, out var away);
        predictions.TryGetValue(match.Id, out var prediction);

        var homeView = ToProductTeam(home, match.HomeObjectId);
        var awayView = ToProductTeam(away, match.AwayObjectId);
        var displayStatus = ResolveDisplayMatchStatus(match, DateTimeOffset.Now);
        var probability = ToProductProbability(prediction, match, displayStatus, homeView.NameCn, awayView.NameCn);
        var evidenceCount = snapshots.Count(item => item.MatchId == match.Id || item.ObjectId == match.HomeObjectId || item.ObjectId == match.AwayObjectId);
        var memoryCount = memories.Count(item => item.ObjectId == match.HomeObjectId || item.ObjectId == match.AwayObjectId || item.SourceId == match.Id);
        var reportCount = artifacts.Count(item => IsArtifactRelatedToMatch(item, match));
        var qualityGate = BuildPredictionQualityGate(match, prediction, snapshots, displayStatus, home, away);
        probability.QualityGate = qualityGate;
        probability.BettingAdvice = ApplyQualityGateToBettingAdvice(probability.BettingAdvice, qualityGate);

        return new ProductMatchQueueItem
        {
            MatchId = match.Id,
            Stage = match.Stage,
            StageLabel = TranslateStage(match.Stage, match.GroupName),
            GroupName = TranslateGroup(match.GroupName),
            KickoffTime = match.KickoffTime,
            KickoffLabel = FormatKickoff(match.KickoffTime),
            Venue = match.Venue,
            Status = displayStatus,
            StatusLabel = TranslateStatus(displayStatus),
            HomeScore = match.HomeScore,
            AwayScore = match.AwayScore,
            Home = homeView,
            Away = awayView,
            Prediction = probability,
            EvidenceCount = evidenceCount,
            MemoryCount = memoryCount,
            ReportCount = reportCount,
            Summary = BuildMatchSummary(homeView, awayView, probability, evidenceCount, memoryCount),
            IsReady = qualityGate.Passed
        };
    }

    private ProductEmployeeView? ResolveProductEmployee(string teamId)
    {
        var employeeId = GetPrimaryEmployeeIdForObject(teamId);
        if (string.IsNullOrWhiteSpace(employeeId)) return null;
        var employee = GetEmployeeById(employeeId);
        if (employee == null) return null;
        return new ProductEmployeeView
        {
            Id = employee.Id,
            Name = BuildEmployeeDisplayName(employee, teamId),
            Role = TranslateRole(employee.Role),
            Specialty = TranslateSpecialty(employee.Specialty, teamId),
            Status = TranslateEmployeeStatus(employee.Status),
            TeamId = teamId
        };
    }

    private static ProductTeamView ToProductTeam(WorldCupWatchObject? team, string fallbackId)
    {
        if (team == null)
        {
            return new ProductTeamView
            {
                Id = fallbackId,
                Name = fallbackId,
                NameCn = fallbackId,
                Code = fallbackId,
                Status = "未知"
            };
        }

        var metadata = ParseObject(team.MetadataJson);
        var nameCn = TranslateTeamCode(team.Symbol)
            ?? ReadString(metadata, "name_cn")
            ?? ReadString(metadata, "chinese_name")
            ?? TranslateTeamName(team.DisplayName);
        var group = ReadString(metadata, "group") ?? "";
        return new ProductTeamView
        {
            Id = team.Id,
            Name = string.IsNullOrWhiteSpace(team.DisplayName) ? team.Name : team.DisplayName,
            NameCn = nameCn,
            Code = string.IsNullOrWhiteSpace(team.Symbol) ? team.Id : team.Symbol,
            Group = TranslateGroup(group),
            FifaRank = ReadInt(metadata, "fifa_rank"),
            Status = TranslateTeamStatus(team.Status),
            FlagAsset = BuildFlagAsset(team.Symbol)
        };
    }

    private static ProductProbabilityView ToProductProbability(
        BaselinePredictionRecord? prediction,
        WorldCupMatch match,
        string displayStatus,
        string homeName,
        string awayName)
    {
        if (prediction == null)
        {
            return new ProductProbabilityView
            {
                Phase = BuildPredictionPhase(null, match, displayStatus, homeName, awayName),
                FavoriteLabel = "等待预测",
                ConfidenceLabel = "待生成",
                RiskLabel = "未知",
                Method = "未生成",
                BettingAdvice = new ProductBettingAdviceView
                {
                    Action = "no_bet",
                    ActionLabel = "暂不建议投注",
                    SuggestedPlay = "先刷新数据并生成结构化预测",
                    Confidence = "不可用",
                    Threshold = "未生成胜率，不进入投注判断",
                    StakePolicy = "不下注",
                    RiskNotes = ["没有结构化预测前，不应给出投注建议。"]
                }
            };
        }

        var favorite = ResolveFavorite(prediction, homeName, awayName);
        var edge = new[] { prediction.HomeWinProbability, prediction.DrawProbability, prediction.AwayWinProbability }
            .OrderByDescending(item => item)
            .Take(2)
            .ToList();
        var spread = edge.Count == 2 ? edge[0] - edge[1] : 0;
        var factors = ParsePredictionFactors(prediction.InputSnapshotIdsJson);
        return new ProductProbabilityView
        {
            HomeWin = Math.Round(prediction.HomeWinProbability, 4),
            Draw = Math.Round(prediction.DrawProbability, 4),
            AwayWin = Math.Round(prediction.AwayWinProbability, 4),
            Phase = BuildPredictionPhase(prediction, match, displayStatus, homeName, awayName),
            FavoriteLabel = favorite,
            ConfidenceLabel = spread >= 0.25 ? "较高" : spread >= 0.12 ? "中等" : "谨慎",
            RiskLabel = spread >= 0.25 ? "低波动" : spread >= 0.12 ? "中波动" : "高波动",
            UpdatedAt = prediction.CreatedAt,
            Method = TranslatePredictionMethod(prediction.Method),
            Factors = factors,
            BettingAdvice = BuildBettingAdvice(prediction, homeName, awayName)
        };
    }

    private static ProductPredictionPhaseView BuildPredictionPhase(
        BaselinePredictionRecord? prediction,
        WorldCupMatch match,
        string displayStatus,
        string homeName,
        string awayName)
    {
        var status = NormalizeMatchStatus(displayStatus);
        var scoreLabel = match.HomeScore == null || match.AwayScore == null ? "" : $"{match.HomeScore}:{match.AwayScore}";
        var predictedOutcome = prediction == null ? "" : ResolvePredictedOutcomeKey(prediction);
        var predictedLabel = prediction == null ? "等待预测" : OutcomeLabel(predictedOutcome, homeName, awayName);

        if (prediction == null)
        {
            return new ProductPredictionPhaseView
            {
                Phase = "pending",
                PhaseLabel = "等待预测",
                PrimaryLabel = "资料准备中",
                Summary = "系统尚未生成结构化胜率，当前只能展示赛程和已有证据。",
                ScoreLabel = scoreLabel,
                IsActionablePreMatch = false
            };
        }

        if (status == "finished" && match.HomeScore != null && match.AwayScore != null)
        {
            var actualOutcome = ResolveActualOutcomeKey(match.HomeScore.Value, match.AwayScore.Value);
            var hit = string.Equals(predictedOutcome, actualOutcome, StringComparison.OrdinalIgnoreCase);
            var brier = CalculateProductBrierScore(prediction, actualOutcome);
            return new ProductPredictionPhaseView
            {
                Phase = "post_match",
                PhaseLabel = "赛后复盘",
                PrimaryLabel = hit ? "预测命中" : "预测偏差",
                Summary = $"已完赛，赛前模型倾向为{predictedLabel}，实际为{OutcomeLabel(actualOutcome, homeName, awayName)}。当前概率只能用于模型复盘，不再作为赛前投注依据。",
                ScoreLabel = scoreLabel,
                PredictedOutcome = predictedOutcome,
                PredictedLabel = predictedLabel,
                ActualOutcome = actualOutcome,
                ActualLabel = OutcomeLabel(actualOutcome, homeName, awayName),
                Hit = hit,
                BrierScore = Math.Round(brier, 4),
                IsPostMatch = true,
                IsActionablePreMatch = false
            };
        }

        if (status is "running" or "live" or "live_window")
        {
            return new ProductPredictionPhaseView
            {
                Phase = "live_tracking",
                PhaseLabel = "临场追踪",
                PrimaryLabel = "赛前模型冻结",
                Summary = "比赛已进入开赛窗口，赛前胜率只作为基准参照；系统应优先采集实时比分、事件和赛后技术统计。",
                ScoreLabel = scoreLabel,
                PredictedOutcome = predictedOutcome,
                PredictedLabel = predictedLabel,
                IsActionablePreMatch = false
            };
        }

        if (status == "needs_result_update")
        {
            return new ProductPredictionPhaseView
            {
                Phase = "result_pending",
                PhaseLabel = "等待赛果",
                PrimaryLabel = "需回填比分",
                Summary = "比赛时间已过但系统尚未拿到可靠赛果，必须先同步比分再进入赛后复盘。",
                ScoreLabel = scoreLabel,
                PredictedOutcome = predictedOutcome,
                PredictedLabel = predictedLabel,
                IsActionablePreMatch = false
            };
        }

        return new ProductPredictionPhaseView
        {
            Phase = "pre_match",
            PhaseLabel = status == "starting_soon" ? "临近开赛" : "赛前预测",
            PrimaryLabel = predictedLabel,
            Summary = status == "starting_soon"
                ? "临近开赛，胜率仍可参考，但需要重点复核首发、伤停、天气和临场市场变化。"
                : "未开赛阶段，胜率用于赛前研究；投注建议仍需通过质量门槛、赔率校准和临场复核。",
            ScoreLabel = scoreLabel,
            PredictedOutcome = predictedOutcome,
            PredictedLabel = predictedLabel,
            IsActionablePreMatch = true
        };
    }

    private static string ResolvePredictedOutcomeKey(BaselinePredictionRecord prediction)
    {
        var outcomes = new[]
        {
            ("home_win", prediction.HomeWinProbability),
            ("draw", prediction.DrawProbability),
            ("away_win", prediction.AwayWinProbability)
        };
        return outcomes.OrderByDescending(item => item.Item2).First().Item1;
    }

    private static string ResolveActualOutcomeKey(int homeScore, int awayScore)
    {
        if (homeScore > awayScore) return "home_win";
        if (homeScore < awayScore) return "away_win";
        return "draw";
    }

    private static string OutcomeLabel(string outcome, string homeName, string awayName)
    {
        return outcome switch
        {
            "home_win" => $"{homeName}胜",
            "away_win" => $"{awayName}胜",
            "draw" => "平局",
            _ => "未知"
        };
    }

    private static double CalculateProductBrierScore(BaselinePredictionRecord prediction, string actualOutcome)
    {
        var homeActual = actualOutcome == "home_win" ? 1 : 0;
        var drawActual = actualOutcome == "draw" ? 1 : 0;
        var awayActual = actualOutcome == "away_win" ? 1 : 0;
        var sum =
            Math.Pow(prediction.HomeWinProbability - homeActual, 2) +
            Math.Pow(prediction.DrawProbability - drawActual, 2) +
            Math.Pow(prediction.AwayWinProbability - awayActual, 2);
        return sum / 3.0;
    }

    private static ProductBettingAdviceView BuildBettingAdvice(BaselinePredictionRecord prediction, string homeName, string awayName)
    {
        var outcomes = new[]
        {
            new { Key = "home", Label = $"{homeName}胜", Probability = prediction.HomeWinProbability },
            new { Key = "draw", Label = "平局", Probability = prediction.DrawProbability },
            new { Key = "away", Label = $"{awayName}胜", Probability = prediction.AwayWinProbability }
        }.OrderByDescending(item => item.Probability).ToList();
        var top = outcomes[0];
        var second = outcomes[1];
        var gap = top.Probability - second.Probability;
        var dataQuality = ReadPredictionPayloadDouble(prediction.InputSnapshotIdsJson, "data_quality", 0.55);
        var drawRisk = prediction.DrawProbability;
        var notes = new List<string>();

        if (dataQuality < 0.65) notes.Add($"数据质量 {dataQuality:P0} 偏低，缺少足够交叉验证。");
        if (gap < 0.10) notes.Add("最高概率与第二概率差距不足 10%，不适合激进单选。");
        if (drawRisk >= 0.25 && top.Key != "draw") notes.Add($"平局概率 {drawRisk:P0} 偏高，需要防平。");
        if (prediction.StrategyVersion != BaselinePredictionStrategy.Version) notes.Add("预测版本不是当前最新策略，建议先刷新。");

        if (dataQuality < 0.65 || top.Probability < 0.58 || gap < 0.08)
        {
            return new ProductBettingAdviceView
            {
                Action = "no_bet",
                ActionLabel = "暂不建议投注",
                SuggestedPlay = "观察，不做赛前投注",
                Confidence = "谨慎",
                Threshold = "需要最高胜率 >= 58%、优势差 >= 8%、数据质量 >= 65%",
                StakePolicy = "不下注；等待伤停、首发和市场信息进一步确认。",
                RiskNotes = notes.Count == 0 ? ["模型优势不足，风险收益比不清晰。"] : notes
            };
        }

        if (top.Key != "draw" && top.Probability >= 0.67 && gap >= 0.16 && drawRisk < 0.25 && dataQuality >= 0.75)
        {
            return new ProductBettingAdviceView
            {
                Action = "single",
                ActionLabel = "可研究单选",
                SuggestedPlay = $"胜平负单选：{top.Label}",
                Confidence = "较高",
                Threshold = "单选阈值：最高胜率 >= 67%、优势差 >= 16%、平局 < 25%、数据质量 >= 75%",
                StakePolicy = "小额固定仓位，单场不超过预算 1%，不加倍追单。",
                RiskNotes = notes.Count == 0 ? ["仍需赛前核验首发、伤停和临场天气。"] : notes
            };
        }

        if (top.Key != "draw" && drawRisk >= 0.24)
        {
            return new ProductBettingAdviceView
            {
                Action = "draw_protect",
                ActionLabel = "建议防平",
                SuggestedPlay = top.Key == "home" ? $"{homeName}不败：主胜/平" : $"{awayName}不败：客胜/平",
                Confidence = "中等",
                Threshold = "防平阈值：热门方胜率 >= 58%，且平局概率 >= 24%",
                StakePolicy = "小额固定仓位，优先组合票或放弃单关激进下注。",
                RiskNotes = notes.Count == 0 ? ["平局权重不低，单选胜负的容错不足。"] : notes
            };
        }

        if (top.Key == "draw" && top.Probability >= 0.30 && dataQuality >= 0.70)
        {
            return new ProductBettingAdviceView
            {
                Action = "small_draw",
                ActionLabel = "可小注研究平局",
                SuggestedPlay = "胜平负：平局，小额研究",
                Confidence = "谨慎",
                Threshold = "平局阈值：平局概率 >= 30%、数据质量 >= 70%",
                StakePolicy = "只适合极小仓位，不作为主策略。",
                RiskNotes = notes.Count == 0 ? ["平局天然波动大，需要赛前阵容和比赛动机确认。"] : notes
            };
        }

        return new ProductBettingAdviceView
        {
            Action = "double_chance",
            ActionLabel = "建议双选",
            SuggestedPlay = top.Key == "home"
                ? $"胜平负双选：{homeName}胜 / 平"
                : top.Key == "away"
                    ? $"胜平负双选：{awayName}胜 / 平"
                    : $"胜平负双选：平 / {second.Label}",
            Confidence = "中等",
            Threshold = "双选阈值：最高胜率 >= 58%，但未达到单选安全阈值",
            StakePolicy = "小额固定仓位，单场不超过预算 0.5%-1%。",
            RiskNotes = notes.Count == 0 ? ["适合降低单选误差，不代表正收益。"] : notes
        };
    }

    private static ProductPredictionQualityGateView BuildPredictionQualityGate(
        WorldCupMatch match,
        BaselinePredictionRecord? prediction,
        IReadOnlyList<DataSnapshotRecord> snapshots,
        string displayStatus,
        WorldCupWatchObject? home,
        WorldCupWatchObject? away)
    {
        var gate = new ProductPredictionQualityGateView
        {
            UpdatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
        };
        if (prediction == null)
        {
            gate.MissingSources.Add("结构化胜率");
            gate.Explanation = "还没有生成结构化胜率，只能展示赛程信息，不能进入预测研判。";
            return gate;
        }

        var related = snapshots
            .Where(item => string.Equals(item.MatchId, match.Id, StringComparison.OrdinalIgnoreCase)
                || string.Equals(item.ObjectId, match.HomeObjectId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(item.ObjectId, match.AwayObjectId, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var dataQuality = ReadPredictionPayloadDouble(prediction.InputSnapshotIdsJson, "data_quality", 0.55);
        var hasFixtureScore = related.Any(item => item.SnapshotType.Contains("fixture", StringComparison.OrdinalIgnoreCase)
            || item.SnapshotType.Contains("score", StringComparison.OrdinalIgnoreCase)
            || item.Source.Contains("scoreboard", StringComparison.OrdinalIgnoreCase));
        var hasFifaRanking = PredictionPayloadHasNumber(prediction.InputSnapshotIdsJson, "home_fifa_points")
            && PredictionPayloadHasNumber(prediction.InputSnapshotIdsJson, "away_fifa_points");
        var hasElo = PredictionPayloadHasNumber(prediction.InputSnapshotIdsJson, "home_elo")
            && PredictionPayloadHasNumber(prediction.InputSnapshotIdsJson, "away_elo");
        var hasRecentForm = PredictionPayloadHasNumber(prediction.InputSnapshotIdsJson, "home_recent_form")
            && PredictionPayloadHasNumber(prediction.InputSnapshotIdsJson, "away_recent_form");
        var hasUsableLineup = related.Any(IsUsableLineupSnapshot);
        var hasRelevantNews = related.Any(item => IsUsableNewsSnapshot(item, home, away));
        var hasMarketOdds = related.Any(item => item.SnapshotType.Contains("market", StringComparison.OrdinalIgnoreCase)
            || item.ContentJson.Contains("\"moneyline\"", StringComparison.OrdinalIgnoreCase)
            || item.ContentJson.Contains("\"odds\"", StringComparison.OrdinalIgnoreCase));
        var required = new (string Key, string Label, bool Ready, double Weight, string ReadyReason, string MissingReason)[]
        {
            ("fixture_score", "赛程/比分", hasFixtureScore, 0.16, "已找到赛程或记分牌快照，能够判断比赛状态和比分。", "缺少赛程/比分会影响队列排序、赛后复盘和状态判断。"),
            ("fifa_ranking", "FIFA 官方排名", hasFifaRanking, 0.18, "预测载荷包含双方 FIFA 积分，具备官方基础强弱锚点。", "缺少 FIFA 官方积分时，基础强弱判断缺少权威锚点。"),
            ("team_elo", "World Football Elo", hasElo, 0.18, "预测载荷包含双方 Elo，能补充长期战力校准。", "缺少 Elo 时，模型容易过度依赖 FIFA 排名。"),
            ("recent_form", "近期战绩状态", hasRecentForm, 0.16, "预测载荷包含双方近期状态因子，能识别短期走势。", "缺少近期战绩时，模型难以识别状态波动。"),
            ("timely_intel", "本场情报/新闻/阵容线索", hasRelevantNews || hasUsableLineup, 0.14, "已匹配到本场相关新闻或可用阵容线索，但关键事实仍需复核。", "缺少本场时效情报，伤停、首发和轮换风险需要继续采集。"),
            ("market_odds", "市场赔率校准", hasMarketOdds, 0.18, "已发现公开赔率字段，可与模型概率做市场校准。", "缺少市场赔率时，只能做胜率研究，不能判断投注价值。")
        };

        gate.RequiredSourcesTotal = required.Length;
        gate.RequiredSourcesReady = required.Count(item => item.Ready);
        gate.MissingSources = required.Where(item => !item.Ready).Select(item => item.Label).ToList();
        gate.SourceChecks = required
            .Select(item => new ProductPredictionSourceCheckView
            {
                Key = item.Key,
                Label = item.Label,
                Ready = item.Ready,
                StatusLabel = item.Ready ? "已就绪" : "待补充",
                Weight = item.Weight,
                Reason = item.Ready ? item.ReadyReason : item.MissingReason
            })
            .ToList();
        var sourceScore = required.Sum(item => item.Ready ? item.Weight : 0);
        var score = Math.Clamp(0.58 * dataQuality + 0.42 * sourceScore, 0, 1);

        var status = NormalizeMatchStatus(displayStatus);
        if (status is "scheduled" or "starting_soon" && !hasRelevantNews && !hasUsableLineup)
        {
            score = Math.Min(score, 0.76);
        }
        if (status is "scheduled" or "starting_soon" && !hasMarketOdds)
        {
            score = Math.Min(score, 0.76);
        }
        if (status is "finished")
        {
            gate.Warnings.Add("比赛已结束，当前页面应进入赛后复盘，不再生成赛前投注建议。");
            score = Math.Min(score, 0.62);
        }
        if (status is "running" or "live" or "live_window")
        {
            gate.Warnings.Add("比赛处于进行中或疑似进行中，赛前模型需要转为临场观察/赛后复盘。");
            score = Math.Min(score, 0.64);
        }
        if (status == "needs_result_update")
        {
            gate.Warnings.Add("比赛时间已过但比分尚未回填，必须先同步赛果。");
            score = Math.Min(score, 0.48);
        }
        if (dataQuality < 0.65)
        {
            gate.Warnings.Add($"模型底层数据质量仅 {dataQuality:P0}，不适合给强投注建议。");
        }
        if (!required.Any(item => item.Label == "市场赔率校准" && item.Ready))
        {
            gate.Warnings.Add("缺少市场赔率校准，只能做胜率研究，不能判断是否存在正期望投注价值。");
        }
        if (!required.Any(item => item.Label == "本场情报/新闻/阵容线索" && item.Ready))
        {
            gate.Warnings.Add("缺少本场时效情报，伤停、首发和轮换风险需要继续采集。");
        }

        gate.Score = Math.Round(score, 4);
        gate.Passed = score >= 0.52 && status is not ("needs_result_update");
        gate.BettingAllowed = score >= 0.78
            && status == "scheduled"
            && required.Any(item => item.Label == "市场赔率校准" && item.Ready)
            && required.Any(item => item.Label == "本场情报/新闻/阵容线索" && item.Ready);
        gate.StrongAdviceAllowed = gate.BettingAllowed
            && score >= 0.86
            && dataQuality >= 0.78
            && gate.MissingSources.Count == 0;

        (gate.Level, gate.LevelLabel) = gate.StrongAdviceAllowed ? ("strong", "可强研判")
            : gate.BettingAllowed ? ("research", "可投研")
            : gate.Passed ? ("explainable", "可解释预测")
            : ("blocked", "数据不足");
        gate.Explanation = gate.Level switch
        {
            "strong" => "核心数据源齐备，且时效情报与市场赔率均已接入，可做高可信赛前研判；是否投注仍由赔率边际、概率差距和平局风险单独决定。",
            "research" => "基础数据、时效情报和市场校准已具备，可做投注研究，但仍需通过赔率边际、仓位和临场复核约束。",
            "explainable" => "结构化胜率可解释，但缺少赔率或时效情报，只能做赛前研究，不应输出强投注建议。",
            _ => "关键数据不足或赛果状态异常，暂不进入预测研判。"
        };

        return gate;
    }

    private static ProductBettingAdviceView ApplyQualityGateToBettingAdvice(
        ProductBettingAdviceView advice,
        ProductPredictionQualityGateView gate)
    {
        if (gate.StrongAdviceAllowed)
        {
            advice.RiskNotes = advice.RiskNotes
                .Concat(gate.Warnings)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            return advice;
        }

        var notes = advice.RiskNotes
            .Concat(gate.Warnings)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (gate.MissingSources.Count > 0)
        {
            notes.Insert(0, $"质量门槛缺失：{string.Join("、", gate.MissingSources)}。");
        }

        if (gate.BettingAllowed)
        {
            advice.Confidence = advice.Confidence == "较高" ? "中等" : advice.Confidence;
            advice.RiskNotes = notes;
            return advice;
        }

        return new ProductBettingAdviceView
        {
            Action = gate.Passed ? "research_only" : "no_bet",
            ActionLabel = gate.Passed ? "仅作研究" : "暂不建议投注",
            SuggestedPlay = gate.Passed
                ? "只展示胜率和风险解释，等待赔率、阵容、伤停或比分数据补齐后再评估。"
                : "先补齐关键数据源并刷新预测，不进入投注判断。",
            Confidence = gate.LevelLabel,
            Threshold = "投注研究门槛：质量分 >= 78%、赛前状态、市场赔率与本场时效情报均已接入；强建议需 >= 86% 且无关键缺口。",
            StakePolicy = "不下注；此阶段只用于赛前研究、模型体检和数据采集排障。",
            RiskNotes = notes.Count == 0 ? ["质量门槛未达到投注研究级别。"] : notes,
            Disclaimer = advice.Disclaimer
        };
    }

    private static List<ProductProbabilityFactorView> ParsePredictionFactors(string json)
    {
        var factors = new List<ProductProbabilityFactorView>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("factors", out var factorArray) || factorArray.ValueKind != JsonValueKind.Array)
            {
                return factors;
            }

            foreach (var item in factorArray.EnumerateArray())
            {
                var id = ReadJsonString(item, "id") ?? "";
                var contribution = ReadJsonDouble(item, "home_contribution");
                var weight = ReadJsonDouble(item, "weight");
                factors.Add(new ProductProbabilityFactorView
                {
                    Id = id,
                    Label = TranslateFactorLabel(id, ReadJsonString(item, "label") ?? id),
                    HomeContribution = Math.Round(contribution, 4),
                    Weight = Math.Round(weight, 4),
                    Explanation = BuildFactorExplanation(id, contribution)
                });
            }
        }
        catch
        {
            return factors;
        }

        return factors;
    }

    private ProductTeamProfileView? BuildTeamProfile(
        WorldCupWatchObject? team,
        string fallbackId,
        IReadOnlyList<ProductEvidenceView> matchEvidence,
        WorldCupMatch match)
    {
        var teamView = ToProductTeam(team, fallbackId);
        if (team == null) return null;

        var teamEvidence = matchEvidence
            .Where(item => item.Summary.Contains(teamView.NameCn, StringComparison.OrdinalIgnoreCase)
                || item.Summary.Contains(teamView.Name, StringComparison.OrdinalIgnoreCase)
                || item.Source.Contains(teamView.Code, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (teamEvidence.Count == 0)
        {
            teamEvidence = GetProductionDataSnapshots(objectId: team.Id)
                .Take(8)
                .Select(ToProductEvidence)
                .ToList();
        }

        var rankScore = teamView.FifaRank == null ? 58 : Math.Clamp(96 - teamView.FifaRank.Value * 0.72, 28, 94);
        var evidenceScore = Math.Clamp(46 + teamEvidence.Count * 7, 42, 92);
        var positiveScore = EstimateKeywordScore(teamEvidence, ["strong", "stable", "control", "pressing", "win", "positive", "\u7a33\u5b9a", "\u538b\u8feb", "\u63a7\u7403"], 55);
        var riskPenalty = EstimateKeywordScore(teamEvidence, ["injury", "risk", "fatigue", "rotation", "negative", "\u4f24", "\u98ce\u9669", "\u75b2\u52b3"], 0);
        var formScore = Math.Clamp((positiveScore + evidenceScore) / 2 - riskPenalty * 0.16, 30, 94);
        var attackScore = Math.Clamp(rankScore * 0.58 + positiveScore * 0.42, 28, 96);
        var defenseScore = Math.Clamp(rankScore * 0.52 + evidenceScore * 0.36 + (100 - riskPenalty) * 0.12, 28, 96);
        var dataScore = Math.Clamp(evidenceScore, 28, 96);

        return new ProductTeamProfileView
        {
            TeamId = teamView.Id,
            NameCn = teamView.NameCn,
            Code = teamView.Code,
            Group = teamView.Group,
            FifaRank = teamView.FifaRank,
            Status = teamView.Status,
            Headline = BuildTeamHeadline(teamView, match, teamEvidence.Count),
            Stars = BuildLikelyStars(teamView),
            Formation = BuildLikelyFormation(teamView),
            StyleTags = BuildStyleTags(teamView, attackScore, defenseScore, riskPenalty),
            Strengths = BuildStrengths(teamView, attackScore, defenseScore, dataScore),
            Weaknesses = BuildWeaknesses(teamView, riskPenalty, dataScore),
            IntelMetrics = BuildTeamIntelMetrics(team, teamView, teamEvidence),
            InjuryWatch = BuildEvidenceWatch(teamEvidence, ["injury", "injured", "hamstring", "calf", "doubtful", "fitness", "伤", "缺阵"], "暂无明确伤停信号，仍需赛前首发前复核。"),
            LineupWatch = BuildEvidenceWatch(teamEvidence, ["lineup", "squad", "roster", "selection", "starting", "名单", "首发", "阵容"], "暂无明确阵容变化信号，等待官方名单或权威媒体更新。"),
            KeyVariables = BuildKeyVariables(teamView, teamEvidence, riskPenalty, dataScore),
            RecentFormNotes = BuildRecentFormNotes(team),
            Radar =
            [
                new ProductRadarMetricView { Key = "attack", Label = "\u8fdb\u653b", Value = Math.Round(attackScore / 100.0, 3) },
                new ProductRadarMetricView { Key = "defense", Label = "\u9632\u5b88", Value = Math.Round(defenseScore / 100.0, 3) },
                new ProductRadarMetricView { Key = "form", Label = "\u72b6\u6001", Value = Math.Round(formScore / 100.0, 3) },
                new ProductRadarMetricView { Key = "depth", Label = "\u9635\u5bb9", Value = Math.Round(Math.Clamp(rankScore * 0.72 + dataScore * 0.28, 25.0, 95.0) / 100.0, 3) },
                new ProductRadarMetricView { Key = "data", Label = "\u6570\u636e", Value = Math.Round(dataScore / 100.0, 3) }
            ],
            Notes = teamEvidence.Count == 0
                ? ["\u6682\u65e0\u8db3\u591f\u7403\u961f\u60c5\u62a5\uff0c\u5f53\u524d\u7531\u6392\u540d\u548c\u8d5b\u7a0b\u4fe1\u606f\u63a8\u5bfc\u3002"]
                : [$"\u5df2\u7ed3\u5408 {teamEvidence.Count} \u6761\u76f8\u5173\u8bc1\u636e\u751f\u6210\u7403\u961f\u5361\u7247\u3002"]
        };
    }

    private static ProductPredictionRuleView BuildPredictionRule(BaselinePredictionRecord? prediction)
    {
        var factorCount = prediction == null ? 0 : ParsePredictionFactors(prediction.InputSnapshotIdsJson).Count;
        return new ProductPredictionRuleView
        {
            Title = "\u7ed3\u6784\u5316\u80dc\u7387\u89c4\u5219",
            Summary = prediction == null
                ? "\u5c1a\u672a\u751f\u6210\u9884\u6d4b\uff0c\u8bf7\u5148\u5237\u65b0\u8fd9\u573a\u6bd4\u8d5b\u3002"
                : $"当前使用 {TranslatePredictionMethod(prediction.Method)}，共读取 {factorCount} 个显式因子。核心胜率由结构化模型计算，大模型负责证据压缩、解释和审查，不直接拍脑袋改概率。",
            Steps =
            [
                "读取 FIFA 官方排名与积分，形成基础强弱差。",
                "读取 World Football Elo，作为长期战力校准信号。",
                "读取近三年国际比赛结果，聚合胜平负、进失球和近期状态分。",
                "叠加主办国/场地因素、球队证据快照信号、热门压力和冷门保护。",
                "将 aggregate score 通过 logistic 映射为主胜、平局、客胜概率，并保持总和为 100%。",
                "记忆系统召回历史复盘和球队长期特征，进入研报解释与风险提示。"
            ],
            Guardrails =
            [
                "概率总和必须为 100%，且单项概率不能越界。",
                "单选投注研究阈值：最高胜率 >= 67%、优势差 >= 16%、数据质量 >= 75%。",
                "双选/防平阈值：最高胜率 >= 58%，但平局风险或优势差不足时必须降级。",
                "新闻 RSS 只作为发现信号，不直接作为伤停、首发和阵容事实。",
                "大模型输出必须回到证据链，不允许无来源的球员伤停或阵容断言。"
            ],
            Limitations =
            [
                "当前仍缺少权威实时首发、伤停和球员状态源。",
                "世界杯尚未开始时，本届赛事回测样本不足，应将投注建议视为研究信号而非收益承诺。",
                "未接入真实赔率时，无法判断模型概率和市场赔率之间是否存在正期望差。"
            ]
        };
    }

    private static ProductEvidenceView ToProductEvidence(DataSnapshotRecord snapshot)
    {
        return ToProductEvidence(snapshot, null, null);
    }

    private static ProductEvidenceView ToProductEvidence(DataSnapshotRecord snapshot, WorldCupWatchObject? home, WorldCupWatchObject? away)
    {
        var contentJson = BuildProductEvidenceContentJson(snapshot, home, away);
        var parsed = ParseEvidenceText(contentJson);
        var isInvalidElo = IsMismatchedEloSnapshot(snapshot);
        return new ProductEvidenceView
        {
            Id = snapshot.Id,
            Source = snapshot.Source,
            SourceLabel = TranslateSource(snapshot.Source),
            Kind = snapshot.SnapshotType,
            KindLabel = TranslateEvidenceKind(snapshot.SnapshotType),
            FactLevel = ResolveSnapshotFactLevel(snapshot),
            FactLabel = TranslateFactLevel(ResolveSnapshotFactLevel(snapshot)),
            PredictionUsage = ResolveSnapshotPredictionUsage(snapshot),
            Summary = isInvalidElo
                ? Truncate($"已降权：Elo 原始代码与球队不匹配，保留为历史审计记录。{parsed.Text}", 180)
                : Truncate(parsed.Text, 180),
            CapturedAt = snapshot.CapturedAt,
            Confidence = isInvalidElo ? 0.05 : EstimateSourceReliability(snapshot.Source),
            Url = parsed.Url
        };
    }

    private static bool IsMismatchedEloSnapshot(DataSnapshotRecord snapshot)
    {
        if (!snapshot.SnapshotType.Equals("team_elo", StringComparison.OrdinalIgnoreCase)) return false;

        try
        {
            using var doc = JsonDocument.Parse(snapshot.ContentJson);
            if (!doc.RootElement.TryGetProperty("elo_code", out var eloCodeProperty)) return false;
            var eloCode = eloCodeProperty.GetString();
            if (string.IsNullOrWhiteSpace(eloCode)) return false;

            var objectId = snapshot.ObjectId ?? "";
            var fifaCode = objectId.StartsWith("team_", StringComparison.OrdinalIgnoreCase)
                ? objectId["team_".Length..].Trim().ToUpperInvariant()
                : "";
            if (string.IsNullOrWhiteSpace(fifaCode)) return false;

            var expected = ExpectedProductEloCode(fifaCode);
            return !string.IsNullOrWhiteSpace(expected)
                && !eloCode.Equals(expected, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string ExpectedProductEloCode(string fifaCode)
    {
        return fifaCode.Trim().ToUpperInvariant() switch
        {
            "SCO" => "SQ",
            "SYC" => "SC",
            "RSA" => "ZA",
            "POR" => "PT",
            "USA" => "US",
            "GER" => "DE",
            "NED" => "NL",
            "KOR" => "KR",
            "CZE" => "CZ",
            "MAR" => "MA",
            "MEX" => "MX",
            "HAI" => "HT",
            "BRA" => "BR",
            _ => ""
        };
    }

    private static ProductEvidenceView ToProductEvidence(IntelligenceSignalRecord signal)
    {
        var parsed = ParseEvidenceText(signal.EvidenceJson);
        return new ProductEvidenceView
        {
            Id = signal.Id,
            Source = parsed.Source ?? "intelligence_signal",
            SourceLabel = TranslateSource(parsed.Source ?? "intelligence_signal"),
            Kind = signal.SignalType,
            KindLabel = TranslateEvidenceKind(signal.SignalType),
            FactLevel = parsed.FactLevel ?? ResolveSignalProductFactLevel(signal),
            FactLabel = TranslateFactLevel(parsed.FactLevel ?? ResolveSignalProductFactLevel(signal)),
            PredictionUsage = parsed.PredictionUsage ?? ResolveSignalPredictionUsage(signal),
            Summary = Truncate(string.IsNullOrWhiteSpace(signal.Summary) ? signal.Title : signal.Summary, 180),
            CapturedAt = signal.CreatedAt,
            Confidence = Math.Round(Math.Clamp(signal.Confidence, 0, 1), 3),
            Url = parsed.Url
        };
    }

    private static ProductMemoryView ToProductMemory(MemoryRecord memory)
    {
        return new ProductMemoryView
        {
            Id = memory.Id,
            Scope = TranslateMemoryScope(memory.Scope),
            Type = TranslateMemoryType(memory.MemoryType),
            Summary = memory.Summary,
            Importance = Math.Round(memory.Importance, 3),
            Confidence = Math.Round(memory.Confidence, 3),
            CreatedAt = memory.CreatedAt
        };
    }

    private static ProductActivityView ToProductActivity(WorldCupSystemEventLog log)
    {
        return new ProductActivityView
        {
            Id = log.Id,
            Time = log.CreatedAt,
            Title = CleanProductText(log.Title, log.EventType),
            Message = CleanProductText(log.Message, ""),
            Tone = log.Severity switch
            {
                "error" => "danger",
                "warning" => "warning",
                _ => "neutral"
            }
        };
    }

    private static List<ProductMetricView> BuildProductMetrics(
        WorldCupMatch match,
        WorldCupWatchObject? home,
        WorldCupWatchObject? away,
        BaselinePredictionRecord? prediction,
        int evidenceCount,
        int memoryCount)
    {
        return
        [
            new ProductMetricView { Label = "比赛阶段", Value = TranslateStage(match.Stage, match.GroupName), Tone = "neutral" },
            new ProductMetricView { Label = "开球时间", Value = FormatKickoff(match.KickoffTime), Tone = "neutral" },
            new ProductMetricView { Label = "主队排名", Value = FormatRank(home), Tone = "neutral" },
            new ProductMetricView { Label = "客队排名", Value = FormatRank(away), Tone = "neutral" },
            new ProductMetricView { Label = "证据数量", Value = $"{evidenceCount} 条", Tone = evidenceCount > 0 ? "good" : "warning" },
            new ProductMetricView { Label = "历史记忆", Value = $"{memoryCount} 条", Tone = memoryCount > 0 ? "good" : "neutral" },
            new ProductMetricView { Label = "预测状态", Value = prediction == null ? "未生成" : "已生成", Tone = prediction == null ? "warning" : "good" }
        ];
    }

    private static List<string> BuildProductRisks(ProductMatchDetail detail)
    {
        var risks = new List<string>();
        if (detail.QueueItem.Prediction.RiskLabel == "高波动") risks.Add("双方胜率接近，模型需要把冷门与平局风险单独提示。");
        if (detail.Evidence.Count < 4) risks.Add("证据包偏薄，当前结论更适合作为赛前初判。");
        if (detail.Memories.Count == 0) risks.Add("缺少历史记忆，后续应结合比赛结果复盘更新判断。");
        if (detail.QueueItem.Prediction.Method == "未生成") risks.Add("尚未生成结构化预测，不能给出正式胜率判断。");
        if (risks.Count == 0) risks.Add("当前证据、记忆和预测记录已具备基础可解释性，但仍需赛前最新信息复核。");
        return risks;
    }

    private static List<string> BuildProductDataNotes(
        BaselinePredictionRecord? prediction,
        IReadOnlyList<ProductEvidenceView> evidence,
        IReadOnlyList<ProductMemoryView> memories)
    {
        var notes = new List<string>();
        if (prediction != null) notes.Add("胜率由策略层计算，大模型用于解释、审查和压缩证据。");
        if (evidence.Any(item => item.Source.Contains("rss", StringComparison.OrdinalIgnoreCase))) notes.Add("新闻 RSS 只作为情报发现信号，关键事实需要二次核对。");
        if (evidence.Any(item => item.Source.Contains("fixture", StringComparison.OrdinalIgnoreCase) || item.Source.Contains("openfootball", StringComparison.OrdinalIgnoreCase))) notes.Add("赛程源适合验证比赛时间与对阵，不直接证明球队强弱。");
        if (memories.Count > 0) notes.Add("记忆会进入后续研报上下文，但会按重要性和时效性限量召回。");
        return notes;
    }

    private static List<ProductDataCoverageItem> BuildProductDataCoverage(
        WorldCupMatch match,
        WorldCupWatchObject? home,
        WorldCupWatchObject? away,
        IReadOnlyList<DataSnapshotRecord> snapshots,
        IReadOnlyList<ProductEvidenceView> evidence)
    {
        var matchSnapshots = snapshots
            .Where(item => string.Equals(item.MatchId, match.Id, StringComparison.OrdinalIgnoreCase)
                || string.Equals(item.ObjectId, match.HomeObjectId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(item.ObjectId, match.AwayObjectId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var result = new List<ProductDataCoverageItem>
        {
            BuildCoverageItem(
                "fixture_score",
                "赛程与比分",
                matchSnapshots,
                item => item.SnapshotType.Contains("fixture", StringComparison.OrdinalIgnoreCase)
                    || item.SnapshotType.Contains("score", StringComparison.OrdinalIgnoreCase),
                match.Status == "finished" && match.HomeScore != null && match.AwayScore != null
                    ? $"已同步完赛比分：{match.HomeScore}:{match.AwayScore}。"
                    : match.Status == "running"
                        ? "比赛进行中，比分会随记分牌刷新。"
                        : "已同步赛程，待开赛后更新比分。"),
            BuildCoverageItem(
                "match_events",
                "进球与红黄牌事件",
                matchSnapshots,
                item => SnapshotHasNonEmptyArray(item, "details") || SnapshotHasNonEmptyArray(item, "events"),
                "ESPN 记分牌已提供比赛事件，可用于赛后复盘和模型审查。",
                match.Status == "scheduled"
                    ? "比赛尚未开始，进球、红黄牌和换人事件需要开赛后由记分牌产生。"
                    : "当前公开记分牌暂未提供可用事件明细，系统会在下一轮自动采集中继续复核。"),
            BuildCoverageItem(
                "team_match_stats",
                "技术统计",
                matchSnapshots,
                item => item.SnapshotType.Equals("team_match_stats", StringComparison.OrdinalIgnoreCase)
                    && (match.Status is "running" or "finished"),
                match.Status == "scheduled"
                    ? "比赛尚未开始，本场控球、射门、角球等技术统计需要开赛后由记分牌产生。"
                    : "已采集控球、射门、角球等技术统计，适合赛后校准球队状态。",
                match.Status == "scheduled"
                    ? "比赛尚未开始，本场控球、射门、角球等技术统计需要开赛后由记分牌产生。"
                    : "当前公开记分牌暂未返回本场技术统计，系统会随赛后数据继续补齐。"),
            BuildCoverageItem(
                "match_summary",
                "赛后摘要",
                matchSnapshots,
                item => item.SnapshotType.Equals("match_summary", StringComparison.OrdinalIgnoreCase),
                "已采集赛后摘要和双方近期战绩上下文。",
                match.Status == "finished"
                    ? "比赛已结束，但公开摘要暂未返回；系统会在 ESPN summary 后续刷新时补齐。"
                    : "赛后摘要需要比赛结束后才会稳定产生，未开赛阶段不作为预测输入。"),
            BuildCoverageItem(
                "lineup",
                "阵容/首发",
                matchSnapshots,
                item => item.SnapshotType.Equals("lineup_fact", StringComparison.OrdinalIgnoreCase)
                    && (SnapshotReadInt(item, "starter_count") > 0 || SnapshotReadNestedInt(item, "lineup", "starter_count") > 0),
                "已拿到可用首发名单；若仍显示缺失，说明公开源暂未提供球员级首发。",
                "公开免费源暂未提供可稳定解析的球员级首发；通常需要赛前临近时间由权威源二次确认。"),
            BuildCoverageItem(
                "recent_form",
                "近期战绩",
                matchSnapshots,
                item => item.SnapshotType.Equals("team_recent_form", StringComparison.OrdinalIgnoreCase)
                    || SnapshotHasNonEmptyArray(item, "recent_form"),
                "已采集近期比赛结果，可进入球队状态因子。"),
            BuildCoverageItem(
                "market_odds",
                "市场赔率",
                matchSnapshots,
                item => item.SnapshotType.Contains("market", StringComparison.OrdinalIgnoreCase)
                    || SnapshotHasNonEmptyArray(item, "odds")
                    || item.ContentJson.Contains("\"moneyline\"", StringComparison.OrdinalIgnoreCase),
                "已发现公开赔率字段，可用于后续做市场隐含概率对比。",
                "当前公开记分牌未暴露可用赔率字段；不是每场比赛都会提供无 Key 赔率。"),
            BuildCoverageItem(
                "news",
                "新闻线索",
                matchSnapshots,
                item => (item.SnapshotType.Contains("news", StringComparison.OrdinalIgnoreCase)
                        || item.Source.Contains("rss", StringComparison.OrdinalIgnoreCase))
                    && SnapshotHasRelevantNewsArticles(item, home, away),
                "新闻只作为情报发现入口，关键伤停和首发仍需二次核验。",
                "当前没有匹配到本场可追溯新闻线索；系统会继续从 RSS 等公开入口做低频分拣。")
        };

        if (evidence.Any(item => item.Source.Contains("espn_scoreboard", StringComparison.OrdinalIgnoreCase)))
        {
            result.Insert(0, new ProductDataCoverageItem
            {
                Key = "live_feed",
                Label = "实时记分牌",
                Status = "ready",
                StatusLabel = "已接入",
                SourceLabel = "ESPN 公开记分牌",
                UpdatedAt = evidence
                    .Where(item => item.Source.Contains("espn_scoreboard", StringComparison.OrdinalIgnoreCase))
                    .Select(item => item.CapturedAt)
                    .OrderByDescending(item => item, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault() ?? "",
                Summary = "当前比赛队列会随公开记分牌刷新状态、比分和赛后统计。"
            });
        }

        return result;
    }

    private static List<ProductPrematchWatchPlanItem> BuildPrematchWatchPlan(
        WorldCupMatch match,
        IReadOnlyList<ProductDataCoverageItem> coverage)
    {
        var status = NormalizeMatchStatus(match.Status);
        var kickoff = ParseDateOrMax(match.KickoffTime);
        var now = DateTimeOffset.Now;
        var hoursToKickoff = kickoff == DateTimeOffset.MaxValue ? double.MaxValue : (kickoff - now).TotalHours;
        var windowLabel = BuildPrematchWindowLabel(status, hoursToKickoff);
        ProductDataCoverageItem? FindCoverage(string key) =>
            coverage.FirstOrDefault(item => item.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

        return
        [
            BuildWatchPlanItem(
                "fixture_score",
                "赛程比分",
                windowLabel,
                FindCoverage("fixture_score"),
                "持续核对 ESPN 公开记分牌，比赛开始后自动更新状态和比分。"),
            BuildWatchPlanItem(
                "market_odds",
                "市场赔率",
                hoursToKickoff <= 24 ? "赛前 24 小时重点复核" : "赛前常规复核",
                FindCoverage("market_odds"),
                "读取公开赔率字段，换算市场隐含概率并与模型胜率做差异校准。"),
            BuildWatchPlanItem(
                "news",
                "新闻/伤停线索",
                hoursToKickoff <= 24 ? "赛前 24 小时重点分拣" : "低频情报分拣",
                FindCoverage("news"),
                "RSS 只作为发现入口；伤停、轮换和名单传闻必须二次复核后才进入预测解释。"),
            BuildWatchPlanItem(
                "lineup",
                "首发阵容",
                hoursToKickoff <= 3 ? "赛前 3 小时高优先级" : "临场前等待权威源",
                FindCoverage("lineup"),
                "首发通常临近开赛才稳定出现；若公开源提供阵容，系统会标记为复核后进模型。"),
            BuildWatchPlanItem(
                "post_match",
                "赛后复盘",
                status == "finished" ? "赛后复盘窗口" : "完赛后触发",
                FindCoverage("team_match_stats"),
            "完赛后读取事件、技术统计和赛后摘要，写入模型体检与长期记忆。")
        ];
    }

    private static List<ProductCollectionPriorityView> BuildCollectionPriorities(
        WorldCupMatch match,
        ProductPredictionQualityGateView gate,
        IReadOnlyList<ProductDataCoverageItem> coverage)
    {
        var status = NormalizeMatchStatus(match.Status);
        var kickoff = ParseDateOrMax(match.KickoffTime);
        var now = DateTimeOffset.Now;
        var hoursToKickoff = kickoff == DateTimeOffset.MaxValue ? double.MaxValue : (kickoff - now).TotalHours;
        var window = BuildPrematchWindowLabel(status, hoursToKickoff);
        var rows = new List<ProductCollectionPriorityView>();
        ProductDataCoverageItem? Coverage(string key) =>
            coverage.FirstOrDefault(item => item.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

        void Add(string key, string label, string priority, string reason, string source, bool ready = false)
        {
            rows.Add(new ProductCollectionPriorityView
            {
                Key = key,
                Label = label,
                Priority = priority,
                PriorityLabel = priority switch
                {
                    "critical" => "最高",
                    "high" => "高",
                    "medium" => "中",
                    _ => "常规"
                },
                Status = ready ? "ready" : "pending",
                StatusLabel = ready ? "已就绪" : "待补充",
                Reason = reason,
                SuggestedSource = source,
                WindowLabel = window
            });
        }

        if (status == "needs_result_update")
        {
            Add("fixture_score", "赛果比分", "critical", "比赛时间已过但比分尚未回填，必须先同步赛果，否则预测队列会误判赛程状态。", "ESPN 公开记分牌 / FixtureDownload");
        }
        if (status is "running" or "live" or "live_window")
        {
            Add("fixture_score", "实时比分", "critical", "比赛正在进行或处于开赛窗口，比分和事件优先级高于赛前新闻。", "ESPN 公开记分牌");
            Add("match_events", "进球红黄牌", Coverage("match_events")?.Status == "ready" ? "medium" : "high", "临场事件会影响赛后复盘和模型校准。", "ESPN summary / scoreboard", Coverage("match_events")?.Status == "ready");
        }

        foreach (var missing in gate.MissingSources)
        {
            switch (missing)
            {
                case "World Football Elo":
                    Add("team_elo", "Elo 战力", "high", "质量门槛缺少长期战力校准，容易让 FIFA 排名单一信号过重。", "World Football Elo Ratings");
                    break;
                case "近期战绩状态":
                    Add("recent_form", "近期战绩", "high", "质量门槛缺少近期状态，模型无法识别球队最近表现、进失球趋势和状态波动。", "martj42/international_results");
                    break;
                case "市场赔率校准":
                    Add("market_odds", "市场赔率", "high", "缺少市场隐含概率时，只能判断胜率，不能判断是否有投注研究价值。", "ESPN odds 字段 / 公开赔率源");
                    break;
                case "本场情报/新闻/阵容线索":
                    Add("news", "时效情报", hoursToKickoff <= 48 ? "high" : "medium", "缺少本场新闻、伤停或阵容线索，模型必须保守降级。", "ESPN/Guardian RSS + 赛前人工或 LLM 分拣");
                    break;
                case "FIFA 官方排名":
                    Add("fifa_ranking", "FIFA 排名", "high", "缺少官方排名和积分时，基础强弱差会失去权威锚点。", "FIFA official ranking");
                    break;
                case "赛程/比分":
                    Add("fixture_score", "赛程比分", "critical", "缺少赛程比分会影响比赛状态、排序和赛后复盘触发。", "ESPN 公开记分牌 / worldcup26");
                    break;
            }
        }

        foreach (var item in coverage.Where(item => item.Status != "ready"))
        {
            if (status == "finished" && item.Key == "lineup")
            {
                continue;
            }

            var priority = item.Key switch
            {
                "lineup" => status is "running" or "live" or "live_window"
                    ? "high"
                    : hoursToKickoff <= 3 ? "critical" : hoursToKickoff <= 24 ? "high" : "medium",
                "market_odds" => "high",
                "fixture_score" => "critical",
                "match_events" when status is "running" or "finished" => "high",
                "team_match_stats" when status is "finished" => "high",
                "news" => hoursToKickoff <= 48 ? "high" : "medium",
                _ => "normal"
            };
            Add(item.Key, item.Label, priority, item.Summary, item.SourceLabel);
        }

        if (rows.Count == 0)
        {
            Add("quality_gate", "质量门槛巡检", "normal", "当前关键数据源齐备，下一轮保持常规刷新并监控赛前阵容、赔率和新闻变化。", "后台自动采集器", ready: true);
        }

        return rows
            .GroupBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderBy(item => PriorityRank(item.Priority)).First())
            .OrderBy(item => PriorityRank(item.Priority))
            .ThenBy(item => item.Label, StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();
    }

    private static int PriorityRank(string priority)
    {
        return priority switch
        {
            "critical" => 0,
            "high" => 1,
            "medium" => 2,
            _ => 3
        };
    }

    private static ProductPrematchWatchPlanItem BuildWatchPlanItem(
        string key,
        string label,
        string windowLabel,
        ProductDataCoverageItem? coverage,
        string summary)
    {
        var ready = coverage?.Status == "ready";
        return new ProductPrematchWatchPlanItem
        {
            Key = key,
            Label = label,
            WindowLabel = windowLabel,
            Status = ready ? "ready" : "watching",
            StatusLabel = ready ? "已有数据" : "继续盯防",
            SourceLabel = coverage?.SourceLabel ?? "后台自动采集",
            Summary = ready && !string.IsNullOrWhiteSpace(coverage?.Summary)
                ? coverage!.Summary
                : summary
        };
    }

    private static string BuildPrematchWindowLabel(string status, double hoursToKickoff)
    {
        if (status == "finished") return "已完赛，进入复盘";
        if (status is "running" or "live") return "比赛进行中，5 分钟级复核";
        if (hoursToKickoff <= 2) return "开赛前后 2 小时，5 分钟级复核";
        if (hoursToKickoff <= 12) return "赛前 12 小时，10 分钟级复核";
        if (hoursToKickoff <= 48) return "赛前 48 小时，15 分钟级复核";
        if (hoursToKickoff <= 168) return "赛前 7 天，30 分钟级复核";
        return "远期赛程，常规复核";
    }

    private static ProductDataCoverageItem BuildCoverageItem(
        string key,
        string label,
        IReadOnlyList<DataSnapshotRecord> snapshots,
        Func<DataSnapshotRecord, bool> predicate,
        string readySummary,
        string missingSummary = "")
    {
        var snapshot = snapshots
            .Where(predicate)
            .OrderByDescending(item => item.CapturedAt, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (snapshot == null)
        {
            return new ProductDataCoverageItem
            {
                Key = key,
                Label = label,
                Status = "missing",
                StatusLabel = "待补充",
                SourceLabel = "暂无可信公开源",
                Summary = string.IsNullOrWhiteSpace(missingSummary)
                    ? $"{label}暂未获得可用结构化数据，系统会在后续自动采集中继续尝试。"
                    : missingSummary
            };
        }

        return new ProductDataCoverageItem
        {
            Key = key,
            Label = label,
            Status = "ready",
            StatusLabel = "已就绪",
            SourceLabel = TranslateSource(snapshot.Source),
            UpdatedAt = snapshot.CapturedAt,
            Summary = readySummary
        };
    }

    private static bool SnapshotHasNonEmptyArray(DataSnapshotRecord snapshot, string propertyName)
    {
        try
        {
            using var doc = JsonDocument.Parse(snapshot.ContentJson);
            return SnapshotHasNonEmptyArray(doc.RootElement, propertyName);
        }
        catch
        {
            return false;
        }
    }

    private static bool SnapshotHasNonEmptyArray(JsonElement root, string propertyName)
    {
        if (root.ValueKind != JsonValueKind.Object) return false;
        if (root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Array && value.GetArrayLength() > 0) return true;
        if (root.TryGetProperty("payload", out var payload) && payload.ValueKind == JsonValueKind.Object) return SnapshotHasNonEmptyArray(payload, propertyName);
        return false;
    }

    private static bool SnapshotHasRelevantNewsArticles(DataSnapshotRecord snapshot, WorldCupWatchObject? home, WorldCupWatchObject? away)
    {
        return ExtractRelevantNewsArticles(snapshot.ContentJson, home, away).Count > 0;
    }

    private static bool IsUsableLineupSnapshot(DataSnapshotRecord snapshot)
    {
        return snapshot.SnapshotType.Equals("lineup_fact", StringComparison.OrdinalIgnoreCase)
            && (SnapshotReadInt(snapshot, "starter_count") > 0
                || SnapshotReadNestedInt(snapshot, "lineup", "starter_count") > 0);
    }

    private static bool IsUsableNewsSnapshot(DataSnapshotRecord snapshot, WorldCupWatchObject? home, WorldCupWatchObject? away)
    {
        return (snapshot.SnapshotType.Contains("news", StringComparison.OrdinalIgnoreCase)
                || snapshot.Source.Contains("rss", StringComparison.OrdinalIgnoreCase))
            && SnapshotHasRelevantNewsArticles(snapshot, home, away);
    }

    private static string BuildProductEvidenceContentJson(DataSnapshotRecord snapshot, WorldCupWatchObject? home, WorldCupWatchObject? away)
    {
        if (!snapshot.SnapshotType.Contains("news", StringComparison.OrdinalIgnoreCase)
            && !snapshot.Source.Contains("rss", StringComparison.OrdinalIgnoreCase))
        {
            return snapshot.ContentJson;
        }

        var articles = ExtractRelevantNewsArticles(snapshot.ContentJson, home, away);
        if (articles.Count == 0)
        {
            return snapshot.ContentJson;
        }

        try
        {
            using var doc = JsonDocument.Parse(snapshot.ContentJson);
            var next = doc.RootElement.ValueKind == JsonValueKind.Object
                ? JsonNode.Parse(doc.RootElement.GetRawText()) as JsonObject ?? []
                : [];
            var relevantArticles = new JsonArray();
            foreach (var article in articles)
            {
                relevantArticles.Add(JsonNode.Parse(article.ToJsonString()));
            }
            next["articles"] = relevantArticles;
            return next.ToJsonString();
        }
        catch
        {
            return snapshot.ContentJson;
        }
    }

    private static List<JsonObject> ExtractRelevantNewsArticles(string contentJson, WorldCupWatchObject? home, WorldCupWatchObject? away)
    {
        var rows = new List<JsonObject>();
        try
        {
            using var doc = JsonDocument.Parse(contentJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty("articles", out var articles)
                || articles.ValueKind != JsonValueKind.Array
                || articles.GetArrayLength() == 0)
            {
                return rows;
            }

            var needles = BuildNewsTeamNeedles(home)
                .Concat(BuildNewsTeamNeedles(away))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (needles.Count == 0)
            {
                return rows;
            }

            foreach (var article in articles.EnumerateArray())
            {
                var title = ReadJsonString(article, "title") ?? "";
                var description = ReadJsonString(article, "description") ?? "";
                var text = $"{title} {description}";
                if (ProductTextMentionsAnyNeedle(text, needles)
                    && JsonNode.Parse(article.GetRawText()) is JsonObject articleObject)
                {
                    rows.Add(articleObject);
                }
            }
        }
        catch
        {
            return rows;
        }

        return rows;
    }

    private static bool IsProductSnapshotRelevantToMatch(DataSnapshotRecord snapshot, WorldCupWatchObject? home, WorldCupWatchObject? away)
    {
        if (!snapshot.SnapshotType.Contains("news", StringComparison.OrdinalIgnoreCase)
            && !snapshot.Source.Contains("rss", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return SnapshotHasRelevantNewsArticles(snapshot, home, away);
    }

    private static bool IsProductSignalRelevantToMatch(IntelligenceSignalRecord signal, WorldCupWatchObject? home, WorldCupWatchObject? away)
    {
        if (signal.SignalType is not ("injury_risk" or "lineup_news" or "news_update"))
        {
            return true;
        }

        var text = ExtractSignalArticleText(signal);
        return ProductTextMentionsTeam(text, home) || ProductTextMentionsTeam(text, away);
    }

    private static string ExtractSignalArticleText(IntelligenceSignalRecord signal)
    {
        try
        {
            using var doc = JsonDocument.Parse(signal.EvidenceJson);
            var root = doc.RootElement;
            var excerpt = ReadJsonString(root, "excerpt") ?? "";
            if (!string.IsNullOrWhiteSpace(excerpt))
            {
                return excerpt;
            }

            return $"{ReadJsonString(root, "title")} {ReadJsonString(root, "description")}";
        }
        catch
        {
            return $"{signal.Title} {signal.Summary}";
        }
    }

    private static bool ProductTextMentionsTeam(string text, WorldCupWatchObject? team)
    {
        if (team == null || string.IsNullOrWhiteSpace(text)) return false;
        var matched = BuildNewsTeamNeedles(team).Any(needle => ProductTextMentionsNeedle(text, needle));
        return matched && !ProductLikelyHostOrVenueMention(text, team);
    }

    private static bool ProductLikelyHostOrVenueMention(string text, WorldCupWatchObject team)
    {
        if (!team.Symbol.Equals("MEX", StringComparison.OrdinalIgnoreCase)
            && !team.Symbol.Equals("USA", StringComparison.OrdinalIgnoreCase)
            && !team.Symbol.Equals("CAN", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var lowered = text.ToLowerInvariant();
        if (lowered.Contains("entry to canada", StringComparison.Ordinal)
            || lowered.Contains("denied entry", StringComparison.Ordinal)
            || lowered.Contains("visa", StringComparison.Ordinal)
            || lowered.Contains("travel document", StringComparison.Ordinal)
            || lowered.Contains("host city", StringComparison.Ordinal)
            || lowered.Contains("venue", StringComparison.Ordinal)
            || lowered.Contains("stadium", StringComparison.Ordinal))
        {
            return !ProductContainsTeamFootballContext(lowered, team);
        }

        if (lowered.Contains("canada, the us and mexico", StringComparison.Ordinal)
            || lowered.Contains("canada, us and mexico", StringComparison.Ordinal)
            || lowered.Contains("canada, the united states and mexico", StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }

    private static bool ProductContainsTeamFootballContext(string lowered, WorldCupWatchObject team)
    {
        var aliases = BuildNewsTeamNeedles(team)
            .Where(alias => alias.Length > 3)
            .Select(alias => alias.ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return aliases.Any(alias =>
            lowered.Contains($"{alias} squad", StringComparison.Ordinal)
            || lowered.Contains($"{alias} roster", StringComparison.Ordinal)
            || lowered.Contains($"{alias} lineup", StringComparison.Ordinal)
            || lowered.Contains($"{alias} coach", StringComparison.Ordinal)
            || lowered.Contains($"{alias} head coach", StringComparison.Ordinal)
            || lowered.Contains($"{alias} player", StringComparison.Ordinal)
            || lowered.Contains($"{alias} forward", StringComparison.Ordinal)
            || lowered.Contains($"{alias} goalkeeper", StringComparison.Ordinal)
            || lowered.Contains($"{alias} win", StringComparison.Ordinal)
            || lowered.Contains($"{alias} tie", StringComparison.Ordinal)
            || lowered.Contains($"{alias} draw", StringComparison.Ordinal)
            || lowered.Contains($"{alias} lose", StringComparison.Ordinal)
            || lowered.Contains($"{alias} vs", StringComparison.Ordinal)
            || lowered.Contains($"{alias} v ", StringComparison.Ordinal));
    }

    private static bool ProductTextMentionsAnyNeedle(string text, IEnumerable<string> needles)
    {
        return needles.Any(needle => ProductTextMentionsNeedle(text, needle));
    }

    private static bool ProductTextMentionsNeedle(string text, string needle)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(needle)) return false;
        return needle.Length <= 3
            ? System.Text.RegularExpressions.Regex.IsMatch(text, $@"(?<![\p{{L}}\p{{N}}]){System.Text.RegularExpressions.Regex.Escape(needle.ToUpperInvariant())}(?![\p{{L}}\p{{N}}])", System.Text.RegularExpressions.RegexOptions.CultureInvariant)
            : System.Text.RegularExpressions.Regex.IsMatch(text, $@"(?<![\p{{L}}\p{{N}}]){System.Text.RegularExpressions.Regex.Escape(needle)}(?![\p{{L}}\p{{N}}])", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    }

    private static IEnumerable<string> BuildNewsTeamNeedles(WorldCupWatchObject? team)
    {
        if (team == null) yield break;

        foreach (var value in new[] { team.DisplayName, team.Name })
        {
            if (string.IsNullOrWhiteSpace(value)) continue;
            var normalized = value.Trim();
            if (normalized.Length >= 4) yield return normalized;
            foreach (var token in normalized.Split([' ', '-', '_'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (token.Length >= 4 && !IsGenericNewsTeamToken(token))
                {
                    yield return token;
                }
            }
        }

        foreach (var alias in ResolveNewsTeamAliases(team.Symbol))
        {
            yield return alias;
        }
    }

    private static bool IsGenericNewsTeamToken(string token)
    {
        return token.Trim().ToLowerInvariant() switch
        {
            "and" => true,
            "the" => true,
            "team" => true,
            "united" => true,
            "republic" => true,
            _ => false
        };
    }

    private static IEnumerable<string> ResolveNewsTeamAliases(string code)
    {
        return code.Trim().ToUpperInvariant() switch
        {
            "USA" => ["USMNT", "U.S.", "U.S. Soccer", "United States"],
            "RSA" => ["South Africa", "Bafana"],
            "KOR" => ["South Korea", "Korea Republic"],
            "CZE" => ["Czechia", "Czech Republic"],
            "BIH" => ["Bosnia", "Herzegovina"],
            "CIV" => ["Cote d'Ivoire", "Ivory Coast"],
            "COD" => ["Congo DR", "DR Congo"],
            "CUW" => ["Curacao", "Curaçao"],
            "NED" => ["Netherlands", "Dutch"],
            "ENG" => ["England"],
            "SCO" => ["Scotland"],
            "WAL" => ["Wales"],
            _ => []
        };
    }

    private static int SnapshotReadInt(DataSnapshotRecord snapshot, string propertyName)
    {
        try
        {
            using var doc = JsonDocument.Parse(snapshot.ContentJson);
            return ReadJsonInt(doc.RootElement, propertyName);
        }
        catch
        {
            return 0;
        }
    }

    private static int SnapshotReadNestedInt(DataSnapshotRecord snapshot, string objectName, string propertyName)
    {
        try
        {
            using var doc = JsonDocument.Parse(snapshot.ContentJson);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty(objectName, out var nested)
                ? ReadJsonInt(nested, propertyName)
                : 0;
        }
        catch
        {
            return 0;
        }
    }

    private static List<ProductMarketSignalView> BuildProductMarketSignals(
        WorldCupMatch match,
        BaselinePredictionRecord? prediction,
        IReadOnlyList<DataSnapshotRecord> snapshots)
    {
        var snapshot = snapshots
            .Where(item => string.Equals(item.MatchId, match.Id, StringComparison.OrdinalIgnoreCase)
                && item.SnapshotType.Equals("market_signal", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.CapturedAt, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (snapshot == null)
        {
            return [];
        }

        try
        {
            using var doc = JsonDocument.Parse(snapshot.ContentJson);
            var root = doc.RootElement;
            var provider = ReadJsonString(root, "market_provider") ?? TranslateSource(snapshot.Source);
            var rows = new List<ProductMarketSignalView>
            {
                BuildMarketSignalRow("home", "主胜", ReadJsonNullableInt(root, "home_moneyline"), ReadJsonDouble(root, "home_implied_probability"), prediction?.HomeWinProbability ?? 0, provider, snapshot.CapturedAt),
                BuildMarketSignalRow("draw", "平局", ReadJsonNullableInt(root, "draw_moneyline"), ReadJsonDouble(root, "draw_implied_probability"), prediction?.DrawProbability ?? 0, provider, snapshot.CapturedAt),
                BuildMarketSignalRow("away", "客胜", ReadJsonNullableInt(root, "away_moneyline"), ReadJsonDouble(root, "away_implied_probability"), prediction?.AwayWinProbability ?? 0, provider, snapshot.CapturedAt)
            };
            return rows.Where(item => item.MarketProbability > 0).ToList();
        }
        catch
        {
            return [];
        }
    }

    private static ProductMarketSignalView BuildMarketSignalRow(
        string side,
        string label,
        int? moneyline,
        double marketProbability,
        double modelProbability,
        string provider,
        string updatedAt)
    {
        var edge = modelProbability - marketProbability;
        return new ProductMarketSignalView
        {
            Side = side,
            Label = label,
            Moneyline = moneyline,
            MarketProbability = Math.Round(Math.Clamp(marketProbability, 0, 1), 4),
            ModelProbability = Math.Round(Math.Clamp(modelProbability, 0, 1), 4),
            Edge = Math.Round(edge, 4),
            EdgeLabel = edge >= 0.04 ? "模型显著高于市场"
                : edge >= 0.015 ? "模型略高于市场"
                : edge <= -0.04 ? "市场显著更看好"
                : edge <= -0.015 ? "市场略高于模型"
                : "基本一致",
            Provider = provider,
            UpdatedAt = updatedAt
        };
    }

    private static ProductBettingAdviceView BuildMarketAwareBettingAdvice(
        ProductBettingAdviceView baseAdvice,
        IReadOnlyList<ProductMarketSignalView> marketSignals)
    {
        var bestEdge = marketSignals
            .Where(item => item.MarketProbability > 0 && item.ModelProbability > 0)
            .OrderByDescending(item => item.Edge)
            .FirstOrDefault();
        var strongestMarketDisagreement = marketSignals
            .Where(item => item.MarketProbability > 0 && item.ModelProbability > 0)
            .OrderBy(item => item.Edge)
            .FirstOrDefault();

        var notes = new List<string>(baseAdvice.RiskNotes);
        if (bestEdge != null && bestEdge.Edge >= 0.04)
        {
            notes.Insert(0, $"{bestEdge.Label} 的模型概率比市场隐含概率高 {bestEdge.Edge:P1}，可列为价值观察点。");
            return new ProductBettingAdviceView
            {
                Action = baseAdvice.Action == "no_bet" ? "value_watch" : baseAdvice.Action,
                ActionLabel = baseAdvice.Action == "no_bet" ? "价值观察" : baseAdvice.ActionLabel,
                SuggestedPlay = baseAdvice.Action == "no_bet"
                    ? $"暂不直接下注，重点跟踪 {bestEdge.Label} 赔率与首发变化"
                    : $"{baseAdvice.SuggestedPlay}；市场校准优先关注 {bestEdge.Label}",
                Confidence = baseAdvice.Confidence,
                Threshold = $"{baseAdvice.Threshold}；市场边际：模型 - 市场 >= 4% 只作为观察信号",
                StakePolicy = baseAdvice.StakePolicy,
                RiskNotes = notes,
                Disclaimer = baseAdvice.Disclaimer
            };
        }

        if (strongestMarketDisagreement != null && strongestMarketDisagreement.Edge <= -0.04)
        {
            notes.Insert(0, $"{strongestMarketDisagreement.Label} 市场隐含概率明显高于模型，说明盘口观点与模型存在分歧。");
            return new ProductBettingAdviceView
            {
                Action = "market_conflict",
                ActionLabel = "市场分歧，谨慎",
                SuggestedPlay = "不建议直接跟随模型单选，等待首发、伤停和临场赔率二次确认。",
                Confidence = "谨慎",
                Threshold = $"{baseAdvice.Threshold}；若市场与模型差距超过 4%，降级为复核信号",
                StakePolicy = "不下注或极小仓位观察，不追涨热门方向。",
                RiskNotes = notes,
                Disclaimer = baseAdvice.Disclaimer
            };
        }

        if (marketSignals.Count > 0)
        {
            notes.Add("市场隐含概率与模型概率没有明显偏差，当前投注建议主要由结构化模型阈值决定。");
            baseAdvice.RiskNotes = notes;
        }

        return baseAdvice;
    }

    private static string BuildProductModelReview(
        WorldCupMatch match,
        WorldCupWatchObject? home,
        WorldCupWatchObject? away,
        BaselinePredictionRecord? prediction,
        int evidenceCount,
        int memoryCount)
    {
        if (prediction == null)
        {
            return "这场比赛还没有结构化预测。建议先刷新公开数据源，再运行基线预测，最后让模型生成赛前研报。";
        }

        var homeName = ToProductTeam(home, match.HomeObjectId).NameCn;
        var awayName = ToProductTeam(away, match.AwayObjectId).NameCn;
        return $"{homeName} vs {awayName} 已有策略层胜率。当前证据 {evidenceCount} 条、历史记忆 {memoryCount} 条；模型审查应重点检查伤停、阵容、赛程密度和数据源冲突，而不是直接改写概率。";
    }

    private static string BuildMatchSummary(
        ProductTeamView home,
        ProductTeamView away,
        ProductProbabilityView probability,
        int evidenceCount,
        int memoryCount)
    {
        if (probability.Method == "未生成")
        {
            return $"{home.NameCn} 对阵 {away.NameCn}，等待生成结构化预测。";
        }
        return $"{probability.FavoriteLabel}；证据 {evidenceCount} 条，记忆 {memoryCount} 条，置信度{probability.ConfidenceLabel}。";
    }

    private static string ResolveLatestReportSummary(IReadOnlyList<ArtifactRecord> artifacts, string matchId, string homeId, string awayId)
    {
        var artifact = artifacts
            .Where(item => IsArtifactRelatedToMatch(item, matchId, homeId, awayId))
            .OrderByDescending(item => item.CreatedAt, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        return artifact?.Summary ?? "还没有正式研报。可以先生成这场比赛的赛前研报。";
    }

    private static bool IsArtifactRelatedToMatch(ArtifactRecord artifact, WorldCupMatch match)
    {
        return IsArtifactRelatedToMatch(artifact, match.Id, match.HomeObjectId, match.AwayObjectId);
    }

    private static bool IsArtifactRelatedToMatch(ArtifactRecord artifact, string matchId, string homeId, string awayId)
    {
        return artifact.MetadataJson.Contains(matchId, StringComparison.OrdinalIgnoreCase)
            || artifact.ObjectId == homeId
            || artifact.ObjectId == awayId
            || artifact.WorkflowRunId?.Contains(matchId, StringComparison.OrdinalIgnoreCase) == true;
    }

    private static string ResolveFavorite(BaselinePredictionRecord prediction, string homeName, string awayName)
    {
        var ordered = new[]
        {
            new { Label = $"{homeName}占优", Probability = prediction.HomeWinProbability },
            new { Label = "平局权重较高", Probability = prediction.DrawProbability },
            new { Label = $"{awayName}占优", Probability = prediction.AwayWinProbability }
        }.OrderByDescending(item => item.Probability).ToList();
        if (ordered.Count >= 2 && ordered[0].Probability - ordered[1].Probability < 0.015)
        {
            return "胜负接近";
        }
        return ordered[0].Label;
    }

    private static DateTimeOffset ParseDateOrMax(string value)
    {
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : DateTimeOffset.MaxValue;
    }

    private static int ResolveQueuePriority(WorldCupMatch match, DateTimeOffset now)
    {
        var status = NormalizeMatchStatus(match.Status);
        var kickoff = ParseDateOrMax(match.KickoffTime);
        if (status is "running" or "live") return 0;
        if (status == "finished" && kickoff != DateTimeOffset.MaxValue && now - kickoff <= TimeSpan.FromHours(14)) return 1;
        if (status == "scheduled" && kickoff != DateTimeOffset.MaxValue && kickoff <= now && now - kickoff <= TimeSpan.FromHours(3)) return 0;
        if (status == "scheduled" && kickoff >= now) return 2;
        if (status == "scheduled" && kickoff != DateTimeOffset.MaxValue && now - kickoff > TimeSpan.FromHours(3)) return 3;
        if (status == "finished") return 4;
        return 5;
    }

    private static long ResolveQueueDateSort(WorldCupMatch match, DateTimeOffset now)
    {
        var kickoff = ParseDateOrMax(match.KickoffTime);
        if (kickoff == DateTimeOffset.MaxValue) return long.MaxValue;

        var status = NormalizeMatchStatus(match.Status);
        if (status == "finished" && now - kickoff > TimeSpan.FromHours(14))
        {
            return -kickoff.ToUnixTimeSeconds();
        }

        return kickoff.ToUnixTimeSeconds();
    }

    private static string ResolveDisplayMatchStatus(WorldCupMatch match, DateTimeOffset now)
    {
        var status = NormalizeMatchStatus(match.Status);
        if (status != "scheduled") return status;

        var kickoff = ParseDateOrMax(match.KickoffTime);
        if (kickoff == DateTimeOffset.MaxValue) return status;
        var delta = kickoff - now;
        if (delta <= TimeSpan.Zero && now - kickoff <= TimeSpan.FromHours(3)) return "live_window";
        if (now - kickoff > TimeSpan.FromHours(3)) return "needs_result_update";
        if (delta <= TimeSpan.FromHours(2)) return "starting_soon";
        return status;
    }

    private static string NormalizeMatchStatus(string status)
    {
        return status.Trim().ToLowerInvariant() switch
        {
            "live" => "running",
            "in_progress" => "running",
            "playing" => "running",
            "completed" => "finished",
            "final" => "finished",
            _ => string.IsNullOrWhiteSpace(status) ? "scheduled" : status.Trim().ToLowerInvariant()
        };
    }

    private static string FormatKickoff(string value)
    {
        if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
        {
            return string.IsNullOrWhiteSpace(value) ? "时间待定" : value;
        }
        var local = parsed.ToOffset(TimeSpan.FromHours(8));
        return local.ToString("MM月dd日 HH:mm", CultureInfo.InvariantCulture);
    }

    private static string FormatRank(WorldCupWatchObject? team)
    {
        var rank = team == null ? null : ReadInt(ParseObject(team.MetadataJson), "fifa_rank");
        return rank == null ? "暂无" : $"第 {rank} 位";
    }

    private static string TranslateStage(string stage, string groupName)
    {
        var groupPart = string.IsNullOrWhiteSpace(groupName) ? "" : $" {groupName}";
        return stage switch
        {
            "group" => $"小组赛{TranslateGroup(groupPart.Trim())}",
            "round_of_32" => "32 强淘汰赛",
            "round_of_16" => "16 强淘汰赛",
            "quarter_final" => "四分之一决赛",
            "semi_final" => "半决赛",
            "third_place" => "三四名决赛",
            "final" => "决赛",
            _ => string.IsNullOrWhiteSpace(stage) ? "阶段待定" : stage
        };
    }

    private static string TranslateStatus(string status)
    {
        return status switch
        {
            "scheduled" => "未开赛",
            "finished" => "已结束",
            "running" => "进行中",
            "live" => "进行中",
            "live_window" => "疑似进行中",
            "starting_soon" => "即将开赛",
            "needs_result_update" => "等待赛果回填",
            "postponed" => "延期",
            _ => string.IsNullOrWhiteSpace(status) ? "未知" : status
        };
    }

    private static string TranslateTeamStatus(string status)
    {
        return status switch
        {
            "active" => "在赛",
            "eliminated" => "已出局",
            "hibernated" => "休眠",
            _ => string.IsNullOrWhiteSpace(status) ? "未知" : status
        };
    }

    private static string TranslateEmployeeStatus(string status)
    {
        return status switch
        {
            "active" => "在岗",
            "offboarded" => "已离职",
            "inactive" => "停用",
            _ => string.IsNullOrWhiteSpace(status) ? "未知" : status
        };
    }

    private static string TranslateRole(string role)
    {
        var normalized = role.Trim().ToLowerInvariant();
        return normalized switch
        {
            "ceo" => "CEO",
            "data analyst" or "data_analyst" or "数据分析师" => "数据分析师",
            "risk officer" or "risk_officer" or "风险官" => "风险官",
            "hr" or "人事" => "人事",
            "team researcher" or "team_researcher" or "球队研究员" => "球队研究员",
            _ => string.IsNullOrWhiteSpace(role) ? "未定义岗位" : role
        };
    }

    private string BuildEmployeeDisplayName(WorldCupEmployee employee, string teamId)
    {
        var team = GetWatchObjectById(teamId);
        var teamName = ToProductTeam(team, teamId).NameCn;
        if (TranslateRole(employee.Role) == "球队研究员") return $"{teamName}研究员";
        return string.IsNullOrWhiteSpace(employee.Name) ? $"{teamName}员工" : employee.Name;
    }

    private string TranslateSpecialty(string specialty, string teamId)
    {
        var team = GetWatchObjectById(teamId);
        var teamName = ToProductTeam(team, teamId).NameCn;
        return TranslateRole(specialty) == "球队研究员" || specialty.Contains("team", StringComparison.OrdinalIgnoreCase)
            ? $"{teamName}赛程、阵容、状态、新闻与风险监控"
            : specialty;
    }

    private static string TranslateGroup(string group)
    {
        return group.Trim() switch
        {
            "A" or "Group A" => "A 组",
            "B" or "Group B" => "B 组",
            "C" or "Group C" => "C 组",
            "D" or "Group D" => "D 组",
            "E" or "Group E" => "E 组",
            "F" or "Group F" => "F 组",
            "G" or "Group G" => "G 组",
            "H" or "Group H" => "H 组",
            "I" or "Group I" => "I 组",
            "J" or "Group J" => "J 组",
            "K" or "Group K" => "K 组",
            "L" or "Group L" => "L 组",
            _ => string.IsNullOrWhiteSpace(group) ? "" : group
        };
    }

    private static string TranslatePredictionMethod(string method)
    {
        return method switch
        {
            "snapshot_aware_factor" => "证据感知基线策略",
            "baseline_rank" => "排名基线策略",
            _ => string.IsNullOrWhiteSpace(method) ? "未知策略" : method
        };
    }

    private static string TranslateEvidenceKind(string kind)
    {
        return kind switch
        {
            "team_intel" => "球队情报",
            "match_intel" => "比赛情报",
            "team_match_stats" => "技术统计",
            "team_recent_form" => "近期战绩",
            "team_ranking" => "FIFA 排名",
            "team_elo" => "Elo 战力",
            "fixture_status" => "赛程比分",
            "fixture_intel" => "赛程情报",
            "fixture_update" => "赛程更新",
            "fixture_crosscheck" => "赛程交叉校验",
            "match_summary" => "比赛摘要",
            "lineup_fact" => "阵容首发",
            "news_intel" => "新闻线索",
            "news_update" => "新闻信号",
            "fixture" => "赛程",
            "schedule" => "赛程",
            "general_intel" => "综合情报",
            "injury_risk" => "伤停风险",
            "lineup_news" => "阵容消息",
            "market_signal" => "市场赔率",
            _ => string.IsNullOrWhiteSpace(kind) ? "证据" : kind
        };
    }

    private static string TranslateMemoryScope(string scope)
    {
        return scope switch
        {
            "object" => "球队",
            "employee" => "员工",
            "workflow" => "任务",
            "strategy" => "策略",
            "user" => "用户偏好",
            _ => string.IsNullOrWhiteSpace(scope) ? "记忆" : scope
        };
    }

    private static string TranslateMemoryType(string type)
    {
        return type switch
        {
            "episode" => "工作片段",
            "review" => "复盘",
            "lifecycle" => "生命周期",
            "preference" => "偏好",
            _ => string.IsNullOrWhiteSpace(type) ? "记忆" : type
        };
    }

    private static string TranslateAuthority(string tier)
    {
        return tier switch
        {
            "official_data" => "\u5b98\u65b9\u6570\u636e",
            "official_reference" => "官方参考",
            "rating_model" => "评级模型",
            "open_dataset" => "开源数据集",
            "public_feed" => "公开订阅源",
            "community_api" => "社区接口",
            "editorial_feed" => "媒体资讯",
            _ => string.IsNullOrWhiteSpace(tier) ? "未知" : tier
        };
    }

    private static string TranslateStability(string tier)
    {
        return tier switch
        {
            "high" => "高",
            "medium" => "中",
            "low" => "低",
            "web_page" => "网页参考",
            _ => string.IsNullOrWhiteSpace(tier) ? "未知" : tier
        };
    }

    private static string TranslateSource(string source)
    {
        if (source.Contains("espn_scoreboard", StringComparison.OrdinalIgnoreCase)) return "ESPN 公开记分牌";
        if (source.Contains("espn_summary", StringComparison.OrdinalIgnoreCase)) return "ESPN 比赛摘要";
        if (source.Contains("fifa", StringComparison.OrdinalIgnoreCase)) return "FIFA 官方参考";
        if (source.Contains("world_football_elo", StringComparison.OrdinalIgnoreCase) || source.Contains("elo", StringComparison.OrdinalIgnoreCase)) return "World Football Elo";
        if (source.Contains("international_results", StringComparison.OrdinalIgnoreCase)) return "国际赛果数据集";
        if (source.Contains("worldcup26", StringComparison.OrdinalIgnoreCase)) return "WorldCup26 公开接口";
        if (source.Contains("openfootball", StringComparison.OrdinalIgnoreCase)) return "OpenFootball 数据集";
        if (source.Contains("fixture", StringComparison.OrdinalIgnoreCase)) return "FixtureDownload 赛程";
        if (source.Contains("rss", StringComparison.OrdinalIgnoreCase)) return "足球新闻 RSS";
        if (source.Contains("intelligence", StringComparison.OrdinalIgnoreCase)) return "情报分拣";
        return string.IsNullOrWhiteSpace(source) ? "未知来源" : source;
    }

    private static double EstimateSourceReliability(string source)
    {
        if (source.Contains("espn_scoreboard", StringComparison.OrdinalIgnoreCase)) return 0.84;
        if (source.Contains("espn_summary", StringComparison.OrdinalIgnoreCase)) return 0.8;
        if (source.Contains("fifa", StringComparison.OrdinalIgnoreCase)) return 0.9;
        if (source.Contains("world_football_elo", StringComparison.OrdinalIgnoreCase) || source.Contains("elo", StringComparison.OrdinalIgnoreCase)) return 0.82;
        if (source.Contains("international_results", StringComparison.OrdinalIgnoreCase)) return 0.78;
        if (source.Contains("openfootball", StringComparison.OrdinalIgnoreCase)) return 0.82;
        if (source.Contains("fixture", StringComparison.OrdinalIgnoreCase)) return 0.78;
        if (source.Contains("worldcup26", StringComparison.OrdinalIgnoreCase)) return 0.68;
        if (source.Contains("rss", StringComparison.OrdinalIgnoreCase)) return 0.48;
        return 0.5;
    }

    private static string ResolveSnapshotFactLevel(DataSnapshotRecord snapshot)
    {
        if (snapshot.SnapshotType.Equals("market_signal", StringComparison.OrdinalIgnoreCase)) return "market_signal";
        if (snapshot.Source.Contains("espn_scoreboard", StringComparison.OrdinalIgnoreCase)) return "structured_crosscheck";
        if (snapshot.Source.Contains("espn_summary", StringComparison.OrdinalIgnoreCase)) return "structured_signal";
        if (snapshot.Source.Contains("fifa", StringComparison.OrdinalIgnoreCase)) return "official_data";
        if (snapshot.SnapshotType.Contains("fixture", StringComparison.OrdinalIgnoreCase)) return "structured_crosscheck";
        if (snapshot.SnapshotType.Contains("ranking", StringComparison.OrdinalIgnoreCase)) return "structured_signal";
        if (snapshot.SnapshotType.Contains("elo", StringComparison.OrdinalIgnoreCase)) return "model_signal";
        if (snapshot.SnapshotType.Contains("recent_form", StringComparison.OrdinalIgnoreCase)) return "open_dataset_aggregate";
        if (snapshot.Source.Contains("rss", StringComparison.OrdinalIgnoreCase)) return "single_source_news";
        return "raw_snapshot";
    }

    private static string ResolveSnapshotPredictionUsage(DataSnapshotRecord snapshot)
    {
        if (snapshot.SnapshotType.Equals("market_signal", StringComparison.OrdinalIgnoreCase)) return "market_calibration";
        if (snapshot.SnapshotType is "team_match_stats" or "match_summary") return "context_only";
        if (snapshot.SnapshotType is "lineup_fact") return "requires_review_before_prediction_input";
        if (snapshot.SnapshotType is "team_ranking" or "team_elo" or "team_recent_form") return "structured_prediction_input";
        if (snapshot.SnapshotType.Contains("fixture", StringComparison.OrdinalIgnoreCase)) return "fixture_context";
        if (snapshot.Source.Contains("rss", StringComparison.OrdinalIgnoreCase)) return "requires_review_before_prediction_input";
        return "context_only";
    }

    private static string ResolveSignalProductFactLevel(IntelligenceSignalRecord signal)
    {
        return signal.SignalType switch
        {
            "fixture_update" or "group_update" => "structured_crosscheck",
            "injury_risk" or "lineup_news" => "single_source_news",
            "news_update" => "context_signal",
            _ => "unverified_signal"
        };
    }

    private static string ResolveSignalPredictionUsage(IntelligenceSignalRecord signal)
    {
        return signal.SignalType is "injury_risk" or "lineup_news"
            ? "requires_review_before_prediction_input"
            : "context_only";
    }

    private static string TranslateFactLevel(string level)
    {
        return level switch
        {
            "official_data" => "官方数据",
            "official_reference" => "官方参考",
            "structured_crosscheck" => "结构化交叉校验",
            "structured_signal" => "结构化信号",
            "market_signal" => "市场信号",
            "model_signal" => "模型信号",
            "open_dataset_aggregate" => "开源聚合",
            "single_source_news" => "单源新闻线索",
            "context_signal" => "上下文线索",
            "unverified_signal" => "未复核信号",
            "raw_snapshot" => "原始快照",
            _ => string.IsNullOrWhiteSpace(level) ? "未分级" : level
        };
    }

    private static string? TranslateTeamCode(string code)
    {
        return code?.Trim().ToUpperInvariant() switch
        {
            "ALG" => "阿尔及利亚",
            "ANG" => "安哥拉",
            "ARG" => "阿根廷",
            "ARG2" => "阿根廷二队",
            "AUS" => "澳大利亚",
            "AUT" => "奥地利",
            "BEL" => "比利时",
            "BIH" => "波黑",
            "BRA" => "巴西",
            "CAN" => "加拿大",
            "CHN" => "中国",
            "CIV" => "科特迪瓦",
            "CMR" => "喀麦隆",
            "COD" => "刚果（金）",
            "COL" => "哥伦比亚",
            "CPV" => "佛得角",
            "CRC" => "哥斯达黎加",
            "CRO" => "克罗地亚",
            "CUW" => "库拉索",
            "CZE" => "捷克",
            "DEN" => "丹麦",
            "ECU" => "厄瓜多尔",
            "EGY" => "埃及",
            "ENG" => "英格兰",
            "ESP" => "西班牙",
            "FRA" => "法国",
            "GER" => "德国",
            "GHA" => "加纳",
            "HAI" => "海地",
            "HON" => "洪都拉斯",
            "IRN" => "伊朗",
            "IRQ" => "伊拉克",
            "ISL" => "冰岛",
            "ITA" => "意大利",
            "JOR" => "约旦",
            "JPN" => "日本",
            "KOR" => "韩国",
            "KSA" => "沙特阿拉伯",
            "MAR" => "摩洛哥",
            "MEX" => "墨西哥",
            "NED" => "荷兰",
            "NGA" or "NGR" => "尼日利亚",
            "NOR" => "挪威",
            "NZL" => "新西兰",
            "PAN" => "巴拿马",
            "PAR" => "巴拉圭",
            "POR" => "葡萄牙",
            "QAT" => "卡塔尔",
            "RSA" => "南非",
            "SCO" => "苏格兰",
            "SEN" => "塞内加尔",
            "SLO" => "斯洛文尼亚",
            "SRB" => "塞尔维亚",
            "SUI" => "瑞士",
            "SWE" => "瑞典",
            "TGA" => "汤加",
            "TUN" => "突尼斯",
            "TUR" => "土耳其",
            "UKR" => "乌克兰",
            "URU" => "乌拉圭",
            "USA" => "美国",
            "UZB" => "乌兹别克斯坦",
            "WAL" => "威尔士",
            _ => null
        };
    }

    private static string TranslateTeamName(string name)
    {
        return name switch
        {
            "Argentina" => "阿根廷",
            "Algeria" => "阿尔及利亚",
            "Bosnia and Herzegovina" => "波黑",
            "Brazil" => "巴西",
            "Czech Republic" => "捷克",
            "France" => "法国",
            "England" => "英格兰",
            "Spain" => "西班牙",
            "Germany" => "德国",
            "Portugal" => "葡萄牙",
            "Netherlands" => "荷兰",
            "Belgium" => "比利时",
            "Italy" => "意大利",
            "Uruguay" => "乌拉圭",
            "Croatia" => "克罗地亚",
            "Morocco" => "摩洛哥",
            "Japan" => "日本",
            "South Korea" => "韩国",
            "Mexico" => "墨西哥",
            "Paraguay" => "巴拉圭",
            "United States" => "美国",
            "Canada" => "加拿大",
            "Qatar" => "卡塔尔",
            "Saudi Arabia" => "沙特阿拉伯",
            "Australia" => "澳大利亚",
            "Switzerland" => "瑞士",
            "Denmark" => "丹麦",
            "Poland" => "波兰",
            "Serbia" => "塞尔维亚",
            "Senegal" => "塞内加尔",
            "Ghana" => "加纳",
            "Cameroon" => "喀麦隆",
            "Ecuador" => "厄瓜多尔",
            "Colombia" => "哥伦比亚",
            "Chile" => "智利",
            "Peru" => "秘鲁",
            "Iran" => "伊朗",
            "Tunisia" => "突尼斯",
            "Costa Rica" => "哥斯达黎加",
            "Panama" => "巴拿马",
            "Jamaica" => "牙买加",
            "New Zealand" => "新西兰",
            "Egypt" => "埃及",
            "Nigeria" => "尼日利亚",
            "South Africa" => "南非",
            "Norway" => "挪威",
            "Sweden" => "瑞典",
            "Austria" => "奥地利",
            "Turkey" => "土耳其",
            "Ukraine" => "乌克兰",
            "Wales" => "威尔士",
            "Scotland" => "苏格兰",
            _ => string.IsNullOrWhiteSpace(name) ? "未知球队" : name
        };
    }

    private static string BuildFlagAsset(string symbol)
    {
        return string.IsNullOrWhiteSpace(symbol) ? "" : $"flag:{symbol.ToUpperInvariant()}";
    }

    private static Dictionary<string, JsonElement> ParseObject(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return [];
            return doc.RootElement.EnumerateObject()
                .ToDictionary(item => item.Name, item => item.Value.Clone(), StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return [];
        }
    }

    private static string? ReadString(IReadOnlyDictionary<string, JsonElement> values, string key)
    {
        return values.TryGetValue(key, out var value) && value.ValueKind != JsonValueKind.Null
            ? value.ToString()
            : null;
    }

    private static int? ReadInt(IReadOnlyDictionary<string, JsonElement> values, string key)
    {
        if (!values.TryGetValue(key, out var value)) return null;
        if (value.TryGetInt32(out var parsed)) return parsed;
        return int.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed) ? parsed : null;
    }

    private static (string Text, string? Url, string? Source, string? FactLevel, string? PredictionUsage) ParseEvidenceText(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var url = ReadJsonString(root, "url");
            var source = ReadJsonString(root, "source");
            var factLevel = ReadJsonString(root, "fact_level");
            var title = ReadJsonString(root, "title");
            var description = ReadJsonString(root, "description");
            var excerpt = ReadJsonString(root, "excerpt");
            var usage = ReadJsonString(root, "prediction_usage");
            var keywords = ReadJsonStringArray(root, "matched_keywords");
            if (!string.IsNullOrWhiteSpace(excerpt))
            {
                var prefix = usage == "requires_review_before_prediction_input" ? "需复核情报" : "";
                var keywordText = keywords.Count > 0 ? $"关键词：{string.Join("、", keywords.Take(5))}" : "";
                return (string.Join(" / ", new[] { prefix, keywordText, excerpt }.Where(item => !string.IsNullOrWhiteSpace(item))), url, source, factLevel, usage);
            }
            if (!string.IsNullOrWhiteSpace(title) || !string.IsNullOrWhiteSpace(description)) return ($"{title} {description}".Trim(), url, source, factLevel, usage);
            if (root.TryGetProperty("payload", out var payload)) return (payload.ToString(), url, source, factLevel, usage);
            if (root.TryGetProperty("articles", out var articles) && articles.ValueKind == JsonValueKind.Array)
            {
                var text = string.Join(" | ", articles.EnumerateArray()
                    .Take(3)
                    .Select(item => $"{ReadJsonString(item, "title")} {ReadJsonString(item, "description")}".Trim())
                    .Where(item => !string.IsNullOrWhiteSpace(item)));
                if (!string.IsNullOrWhiteSpace(text)) return (text, url, source, factLevel, usage);
            }
            return (root.ToString(), url, source, factLevel, usage);
        }
        catch
        {
            return (json, null, null, null, null);
        }
    }

    private static List<string> ReadJsonStringArray(JsonElement root, string key)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty(key, out var value)
            || value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return value.EnumerateArray()
            .Select(item => item.ToString())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToList();
    }

    private static string? ReadJsonString(JsonElement root, string key)
    {
        return root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty(key, out var value)
            && value.ValueKind != JsonValueKind.Null
            ? value.ToString()
            : null;
    }

    private static double ReadJsonDouble(JsonElement root, string key)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(key, out var value)) return 0;
        if (value.TryGetDouble(out var parsed)) return parsed;
        return double.TryParse(value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out parsed) ? parsed : 0;
    }

    private static int? ReadJsonNullableInt(JsonElement root, string key)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(key, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Null) return null;
        if (value.TryGetInt32(out var parsed)) return parsed;
        return int.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed) ? parsed : null;
    }

    private static int ReadJsonInt(JsonElement root, string key)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(key, out var value)) return 0;
        if (value.TryGetInt32(out var parsed)) return parsed;
        return int.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed) ? parsed : 0;
    }

    private static double ReadPredictionPayloadDouble(string json, string key, double fallback)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty(key, out var value))
            {
                return fallback;
            }
            if (value.TryGetDouble(out var parsed)) return parsed;
            return double.TryParse(value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out parsed) ? parsed : fallback;
        }
        catch
        {
            return fallback;
        }
    }

    private static bool PredictionPayloadHasNumber(string json, string key)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty(key, out var value))
            {
                return false;
            }
            if (value.ValueKind == JsonValueKind.Null || value.ValueKind == JsonValueKind.Undefined) return false;
            return value.TryGetDouble(out _) || double.TryParse(value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out _);
        }
        catch
        {
            return false;
        }
    }

    private static string TranslateFactorLabel(string id, string fallback)
    {
        return id switch
        {
            "rank_edge" => "\u6392\u540d\u5f3a\u5f31\u5dee",
            "fifa_points_edge" => "FIFA 积分差",
            "fifa_rank_edge" => "FIFA 排名差",
            "elo_edge" => "Elo 战力差",
            "recent_form_edge" => "对手修正近期战绩差",
            "host_context" => "主办国/场地因素",
            "nominal_home_edge" => "\u540d\u4e49\u4e3b\u573a\u56e0\u5b50",
            "favorite_pressure" => "\u70ed\u95e8\u538b\u529b\u4fee\u6b63",
            "upset_guard" => "\u51b7\u95e8\u4fdd\u62a4",
            "draw_calibration" => "平局校准",
            "home_snapshot_signal" => "\u4e3b\u961f\u8bc1\u636e\u4fe1\u53f7",
            "away_snapshot_signal" => "\u5ba2\u961f\u8bc1\u636e\u4fe1\u53f7",
            _ => fallback
        };
    }

    private static string BuildFactorExplanation(string id, double contribution)
    {
        var direction = contribution > 0.015 ? "\u504f\u5411\u4e3b\u961f" : contribution < -0.015 ? "\u504f\u5411\u5ba2\u961f" : "\u63a5\u8fd1\u4e2d\u6027";
        return id switch
        {
            "rank_edge" => $"\u6839\u636e\u4e24\u961f FIFA \u6392\u540d\u5dee\u5f02\u63a8\u5bfc\uff0c\u5f53\u524d{direction}\u3002",
            "fifa_points_edge" => $"根据 FIFA 官方积分差推导基础强弱，当前{direction}。",
            "fifa_rank_edge" => $"用 FIFA 排名作为积分缺失时的补充信号，当前{direction}。",
            "elo_edge" => $"根据 World Football Elo 评级校准长期战力，当前{direction}。",
            "recent_form_edge" => $"根据近三年国际比赛、对手强度、赛事权重和时间衰减修正近期状态，当前{direction}。",
            "host_context" => $"考虑 2026 主办国和场地上下文，当前{direction}。",
            "nominal_home_edge" => $"\u8003\u8651\u8d5b\u7a0b\u4e2d\u7684\u540d\u4e49\u4e3b\u961f\u6216\u573a\u5730\u56e0\u7d20\uff0c\u5f53\u524d{direction}\u3002",
            "favorite_pressure" => $"\u5bf9\u8fc7\u70ed\u4e00\u65b9\u505a\u538b\u529b\u4fee\u6b63\uff0c\u9632\u6b62\u8fc7\u5ea6\u81ea\u4fe1\uff0c\u5f53\u524d{direction}\u3002",
            "upset_guard" => $"\u5f3a\u5f31\u5dee\u8fc7\u5927\u65f6\u4fdd\u7559\u51b7\u95e8\u7a7a\u95f4\uff0c\u5f53\u524d{direction}\u3002",
            "draw_calibration" => "根据强弱接近程度、近期状态接近程度、赛事类型和数据不确定性调整平局概率。",
            "home_snapshot_signal" => $"\u4e3b\u961f\u516c\u5f00\u8bc1\u636e\u4e2d\u7684\u72b6\u6001\u4fe1\u53f7\uff0c\u5f53\u524d{direction}\u3002",
            "away_snapshot_signal" => $"\u5ba2\u961f\u516c\u5f00\u8bc1\u636e\u4e2d\u7684\u72b6\u6001\u4fe1\u53f7\uff0c\u5f53\u524d{direction}\u3002",
            _ => $"\u7b56\u7565\u56e0\u5b50\u5bf9\u6bd4\u8d5b\u80dc\u7387\u7684\u8d21\u732e\uff0c\u5f53\u524d{direction}\u3002"
        };
    }

    private static double EstimateKeywordScore(IReadOnlyList<ProductEvidenceView> evidence, IReadOnlyList<string> keywords, double baseValue)
    {
        var joined = string.Join(" ", evidence.Select(item => item.Summary)).ToLowerInvariant();
        var hits = keywords.Count(keyword => joined.Contains(keyword.ToLowerInvariant(), StringComparison.OrdinalIgnoreCase));
        return Math.Clamp(baseValue + hits * 8 + evidence.Count * 2, 0, 100);
    }

    private static string BuildTeamHeadline(ProductTeamView team, WorldCupMatch match, int evidenceCount)
    {
        var rankText = team.FifaRank == null ? "\u6392\u540d\u6682\u7f3a" : $"FIFA \u7b2c {team.FifaRank} \u4f4d";
        return $"{team.NameCn}\u5904\u4e8e {team.Group}\uff0c{rankText}\uff0c\u5f53\u524d\u5df2\u5173\u8054 {evidenceCount} \u6761\u8d5b\u524d\u8bc1\u636e\u3002";
    }

    private static List<string> BuildLikelyStars(ProductTeamView team)
    {
        return team.Code.ToUpperInvariant() switch
        {
            "ARG" => ["梅西", "劳塔罗", "麦卡利斯特"],
            "BRA" => ["维尼修斯", "罗德里戈", "卡塞米罗"],
            "FRA" => ["姆巴佩", "格列兹曼", "楚阿梅尼"],
            "ENG" => ["贝林厄姆", "凯恩", "福登"],
            "ESP" => ["佩德里", "罗德里", "亚马尔"],
            "GER" => ["穆西亚拉", "维尔茨", "基米希"],
            "POR" => ["B. 费尔南德斯", "莱奥", "C. 罗纳尔多"],
            "NED" => ["范戴克", "德容", "加克波"],
            "USA" => ["普利希奇", "雷纳", "麦肯尼"],
            "MEX" => ["希门尼斯", "洛萨诺", "阿尔瓦雷斯"],
            "JPN" => ["三笘薰", "久保建英", "远藤航"],
            "KOR" => ["孙兴慜", "金玟哉", "李刚仁"],
            "MAR" => ["阿什拉夫", "齐耶赫", "阿姆拉巴特"],
            "SUI" => ["扎卡", "阿坎吉", "索默"],
            "URU" => ["巴尔韦德", "努涅斯", "阿劳霍"],
            _ => ["核心前锋", "中场组织者", "防线领袖"]
        };
    }

    private static string BuildLikelyFormation(ProductTeamView team)
    {
        return team.Code.ToUpperInvariant() switch
        {
            "BRA" or "FRA" or "ENG" or "ESP" => "4-3-3 / 4-2-3-1",
            "ARG" or "GER" or "POR" or "NED" => "4-3-3 / 3-4-2-1",
            "JPN" or "KOR" or "MAR" or "USA" => "4-2-3-1 / 4-4-2",
            _ => "4-2-3-1"
        };
    }

    private static List<string> BuildStyleTags(ProductTeamView team, double attackScore, double defenseScore, double riskPenalty)
    {
        var tags = new List<string>();
        tags.Add(attackScore >= 70 ? "\u8fdb\u653b\u4e3b\u5bfc" : "\u7a33\u6001\u63a7\u5236");
        tags.Add(defenseScore >= 70 ? "\u9632\u7ebf\u7a33\u5b9a" : "\u9632\u5b88\u9700\u590d\u6838");
        tags.Add(riskPenalty >= 25 ? "\u98ce\u9669\u654f\u611f" : "\u98ce\u9669\u4e2d\u6027");
        if (team.FifaRank is <= 20) tags.Add("\u5f3a\u961f\u538b\u529b");
        return tags;
    }

    private static List<string> BuildStrengths(ProductTeamView team, double attackScore, double defenseScore, double dataScore)
    {
        var list = new List<string>();
        if (attackScore >= 68) list.Add("\u8fdb\u653b\u706b\u529b\u548c\u4e2a\u4eba\u80fd\u529b\u6709\u660e\u663e\u4f18\u52bf\u3002");
        if (defenseScore >= 68) list.Add("\u9632\u5b88\u7ed3\u6784\u548c\u7ecf\u9a8c\u503c\u5f97\u4fe1\u4efb\u3002");
        if (dataScore >= 65) list.Add("\u5f53\u524d\u8bc1\u636e\u5305\u76f8\u5bf9\u5145\u8db3\uff0c\u53ef\u652f\u6491\u521d\u6b65\u5224\u65ad\u3002");
        if (list.Count == 0) list.Add($"{team.NameCn}\u7684\u57fa\u7840\u5f3a\u5ea6\u63a5\u8fd1\u4e2d\u4f4d\uff0c\u9700\u8981\u4f9d\u8d56\u8d5b\u524d\u6700\u65b0\u60c5\u62a5\u3002");
        return list;
    }

    private static List<string> BuildWeaknesses(ProductTeamView team, double riskPenalty, double dataScore)
    {
        var list = new List<string>();
        if (riskPenalty >= 25) list.Add("\u8bc1\u636e\u4e2d\u51fa\u73b0\u98ce\u9669\u6216\u4e0d\u786e\u5b9a\u4fe1\u53f7\uff0c\u9700\u8981\u4eba\u5de5\u590d\u6838\u3002");
        if (dataScore < 60) list.Add("\u6570\u636e\u8986\u76d6\u4e0d\u8db3\uff0c\u5f53\u524d\u4e0d\u9002\u5408\u7ed9\u51fa\u8fc7\u5ea6\u786e\u5b9a\u7684\u7ed3\u8bba\u3002");
        if (team.FifaRank is > 45 or null) list.Add("\u6392\u540d\u6216\u5386\u53f2\u6218\u529b\u4e0d\u5360\u4f18\uff0c\u5bf9\u5f3a\u961f\u65f6\u9700\u8981\u964d\u4f4e\u9884\u671f\u3002");
        if (list.Count == 0) list.Add("\u4e3b\u8981\u77ed\u677f\u6765\u81ea\u8d5b\u524d\u4f24\u505c\u3001\u9996\u53d1\u548c\u65b0\u95fb\u4e0d\u786e\u5b9a\u6027\u3002");
        return list;
    }

    private List<ProductMetricView> BuildTeamIntelMetrics(
        WorldCupWatchObject team,
        ProductTeamView teamView,
        IReadOnlyList<ProductEvidenceView> evidence)
    {
        var snapshots = GetProductionDataSnapshots(objectId: team.Id);
        var elo = ReadLatestSnapshotDouble(snapshots, "team_elo", "elo_rating");
        var recentForm = ReadLatestSnapshotDouble(snapshots, "team_recent_form", "opponent_adjusted_form_score")
            ?? ReadLatestSnapshotDouble(snapshots, "team_recent_form", "recent_form_score");
        var latestFormDate = ReadLatestSnapshotString(snapshots, "team_recent_form", "latest_match_date");
        var injurySignals = evidence.Count(item => item.Kind.Contains("injury", StringComparison.OrdinalIgnoreCase)
            || item.Summary.Contains("injury", StringComparison.OrdinalIgnoreCase)
            || item.Summary.Contains("伤", StringComparison.OrdinalIgnoreCase));
        var lineupSignals = evidence.Count(item => item.Kind.Contains("lineup", StringComparison.OrdinalIgnoreCase)
            || item.Summary.Contains("lineup", StringComparison.OrdinalIgnoreCase)
            || item.Summary.Contains("squad", StringComparison.OrdinalIgnoreCase)
            || item.Summary.Contains("阵容", StringComparison.OrdinalIgnoreCase));

        return
        [
            new ProductMetricView { Label = "FIFA", Value = teamView.FifaRank == null ? "暂无" : $"第 {teamView.FifaRank} 位", Tone = teamView.FifaRank is <= 25 ? "good" : "neutral" },
            new ProductMetricView { Label = "Elo", Value = elo == null ? "暂无" : Math.Round(elo.Value).ToString(CultureInfo.InvariantCulture), Tone = elo is >= 1750 ? "good" : "neutral" },
            new ProductMetricView { Label = "近况", Value = recentForm == null ? "暂无" : $"{recentForm.Value:+0.00;-0.00;0.00}", Tone = recentForm is > 0.12 ? "good" : recentForm is < -0.12 ? "warning" : "neutral" },
            new ProductMetricView { Label = "更新", Value = string.IsNullOrWhiteSpace(latestFormDate) ? "待补" : latestFormDate, Tone = "neutral" },
            new ProductMetricView { Label = "伤停", Value = injurySignals == 0 ? "未确认" : $"{injurySignals} 条", Tone = injurySignals == 0 ? "neutral" : "warning" },
            new ProductMetricView { Label = "阵容", Value = lineupSignals == 0 ? "待复核" : $"{lineupSignals} 条", Tone = lineupSignals == 0 ? "neutral" : "good" }
        ];
    }

    private static List<string> BuildEvidenceWatch(IReadOnlyList<ProductEvidenceView> evidence, IReadOnlyList<string> keywords, string fallback)
    {
        var rows = evidence
            .Where(item => keywords.Any(keyword => item.Summary.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                || item.Kind.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(item => item.Confidence)
            .ThenByDescending(item => item.CapturedAt, StringComparer.OrdinalIgnoreCase)
            .Select(item => $"{item.KindLabel}：{Truncate(item.Summary, 92)}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(4)
            .ToList();
        return rows.Count == 0 ? [fallback] : rows;
    }

    private static List<string> BuildKeyVariables(ProductTeamView team, IReadOnlyList<ProductEvidenceView> evidence, double riskPenalty, double dataScore)
    {
        var variables = new List<string>();
        if (riskPenalty >= 35) variables.Add("伤停、体能或阵容不确定性偏高，赛前名单会显著影响判断。");
        if (dataScore < 62) variables.Add("公开证据不足，需等待更接近比赛日的权威新闻。");
        if (team.FifaRank is >= 45) variables.Add("硬实力处于弱势区间，防守稳定性和转换效率是关键。");
        if (team.FifaRank is <= 20) variables.Add("强队优势成立，但需要防范热门压力和低比分平局。");
        if (evidence.Any(item => item.Source.Contains("rss", StringComparison.OrdinalIgnoreCase))) variables.Add("新闻信号来自 RSS 发现层，进入模型前必须二次复核。");
        return variables.Count == 0 ? ["临场首发、核心球员健康和比赛节奏是主要观察变量。"] : variables.Take(5).ToList();
    }

    private List<string> BuildRecentFormNotes(WorldCupWatchObject team)
    {
        var snapshot = GetProductionDataSnapshots(objectId: team.Id, snapshotType: "team_recent_form")
            .OrderByDescending(item => item.CapturedAt, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (snapshot == null)
        {
            return ["暂无近期赛果聚合数据，等待 international_results 自动采集补充。"];
        }

        try
        {
            using var doc = JsonDocument.Parse(snapshot.ContentJson);
            var root = doc.RootElement;
            var matches = ReadJsonInt(root, "matches");
            var wins = ReadJsonInt(root, "wins");
            var draws = ReadJsonInt(root, "draws");
            var losses = ReadJsonInt(root, "losses");
            var goalsFor = ReadJsonInt(root, "goals_for");
            var goalsAgainst = ReadJsonInt(root, "goals_against");
            var ppg = ReadJsonDouble(root, "points_per_match");
            var gd = ReadJsonDouble(root, "goal_diff_per_match");
            var adjusted = ReadJsonDouble(root, "opponent_adjusted_form_score");
            var latest = ReadJsonString(root, "latest_match_date") ?? "日期待补";
            var tournaments = ReadJsonStringArray(root, "sample_tournaments");
            var notes = new List<string>
            {
                $"近三年 {matches} 场：{wins} 胜 {draws} 平 {losses} 负，进 {goalsFor} / 失 {goalsAgainst}。",
                $"场均积分 {ppg:0.00}，场均净胜球 {gd:+0.00;-0.00;0.00}，对手修正状态 {adjusted:+0.00;-0.00;0.00}。",
                $"最近样本更新到 {latest}。"
            };
            if (tournaments.Count > 0)
            {
                notes.Add($"样本赛事：{string.Join(" / ", tournaments.Take(4))}。");
            }
            return notes;
        }
        catch
        {
            return ["近期赛果快照解析失败，已保留原始证据供审计。"];
        }
    }

    private static double? ReadLatestSnapshotDouble(IReadOnlyList<DataSnapshotRecord> snapshots, string snapshotType, string propertyName)
    {
        foreach (var snapshot in snapshots
            .Where(item => item.SnapshotType.Equals(snapshotType, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.CapturedAt, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                using var doc = JsonDocument.Parse(snapshot.ContentJson);
                if (!doc.RootElement.TryGetProperty(propertyName, out var value)) continue;
                if (value.TryGetDouble(out var parsed)) return parsed;
                if (double.TryParse(value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out parsed)) return parsed;
            }
            catch
            {
                // Ignore malformed snapshots in product cards.
            }
        }
        return null;
    }

    private static string? ReadLatestSnapshotString(IReadOnlyList<DataSnapshotRecord> snapshots, string snapshotType, string propertyName)
    {
        foreach (var snapshot in snapshots
            .Where(item => item.SnapshotType.Equals(snapshotType, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.CapturedAt, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                using var doc = JsonDocument.Parse(snapshot.ContentJson);
                if (doc.RootElement.TryGetProperty(propertyName, out var value) && value.ValueKind != JsonValueKind.Null)
                {
                    return value.ToString();
                }
            }
            catch
            {
                // Ignore malformed snapshots in product cards.
            }
        }
        return null;
    }

    private static string Truncate(string text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var normalized = string.Join(" ", text.Split(default(string[]), StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength] + "...";
    }

    private static string CleanProductText(string text, string fallback)
    {
        if (string.IsNullOrWhiteSpace(text)) return fallback;
        if (text.Contains('�', StringComparison.Ordinal)) return fallback;
        return text
            .Replace("Baseline prediction refreshed", "基线预测已刷新", StringComparison.OrdinalIgnoreCase)
            .Replace("Data snapshot imported", "数据快照已导入", StringComparison.OrdinalIgnoreCase)
            .Replace("Fixture update signal", "赛程更新信号", StringComparison.OrdinalIgnoreCase)
            .Replace("LLM report generated", "模型研报已生成", StringComparison.OrdinalIgnoreCase)
            .Replace("espn_scoreboard/market_signal imported.", "ESPN 公开记分牌/市场赔率已导入。", StringComparison.OrdinalIgnoreCase)
            .Replace("espn_scoreboard/fixture_status imported.", "ESPN 公开记分牌/赛程比分已导入。", StringComparison.OrdinalIgnoreCase)
            .Replace("draw", "平局", StringComparison.OrdinalIgnoreCase)
            .Replace("United States", "美国", StringComparison.OrdinalIgnoreCase)
            .Replace("Paraguay", "巴拉圭", StringComparison.OrdinalIgnoreCase)
            .Replace("Mexico", "墨西哥", StringComparison.OrdinalIgnoreCase)
            .Replace("South Africa", "南非", StringComparison.OrdinalIgnoreCase);
    }
}

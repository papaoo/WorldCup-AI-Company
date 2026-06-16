using System.Text;
using System.Text.Json;

namespace PiPiClaw.Team;

/// <summary>
/// Background public-data collection service.
/// </summary>
public static class AutoCollectionService
{
    private const int ModelBacktestCacheMaxAgeMinutes = 6 * 60;

    public static void EnsureDefaultAutoCollectionConfig()
    {
        var path = AppContext.AutoCollectionConfigPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        if (File.Exists(path))
        {
            try
            {
                var current = JsonSerializer.Deserialize(
                    File.ReadAllText(path, Encoding.UTF8),
                    AppJsonContext.Default.DataSourceAutoCollectionConfig);
                if (current != null && AddMissingDefaultSources(current))
                {
                    File.WriteAllText(path, JsonSerializer.Serialize(current, AppJsonContext.Default.DataSourceAutoCollectionConfig), Encoding.UTF8);
                }
            }
            catch
            {
                // Keep the existing file untouched if it cannot be parsed.
            }
            return;
        }

        var config = new DataSourceAutoCollectionConfig
        {
            Enabled = true,
            IntervalMinutes = 30,
            AdaptiveScheduleEnabled = true,
            RunIntelligenceTriage = true,
            TriggerEmployeeReports = true,
            TriggerReportsOnlyWhenNewData = true,
            MaxReportTeams = 8,
            TriageSnapshotLimit = 800,
            AutoLlmReportsEnabled = false,
            LlmReportIntervalMinutes = 360,
            MaxLlmReportTeams = 2,
            Sources = BuildDefaultNoKeySources()
        };
        File.WriteAllText(path, JsonSerializer.Serialize(config, AppJsonContext.Default.DataSourceAutoCollectionConfig), Encoding.UTF8);
    }

    private static bool AddMissingDefaultSources(DataSourceAutoCollectionConfig config)
    {
        var changed = false;
        var existing = config.Sources.Select(source => source.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var source in BuildDefaultNoKeySources())
        {
            if (existing.Contains(source.Id)) continue;
            config.Sources.Add(source);
            changed = true;
        }
        return changed;
    }

    private static List<DataSourceAutoCollectionSource> BuildDefaultNoKeySources()
    {
        return
        [
            new DataSourceAutoCollectionSource
            {
                Id = "worldcup26_games",
                Enabled = true,
                SourceName = "worldcup26_games",
                Provider = "worldcup26",
                Query = "games"
            },
            new DataSourceAutoCollectionSource
            {
                Id = "worldcup26_teams",
                Enabled = true,
                SourceName = "worldcup26_teams",
                Provider = "worldcup26",
                Query = "teams"
            },
            new DataSourceAutoCollectionSource
            {
                Id = "fifa_official_mens_ranking",
                Enabled = true,
                SourceName = "fifa_official_mens_ranking",
                Provider = "fifa_ranking",
                SnapshotType = "team_ranking"
            },
            new DataSourceAutoCollectionSource
            {
                Id = "world_football_elo",
                Enabled = true,
                SourceName = "world_football_elo",
                Provider = "world_football_elo",
                SnapshotType = "team_elo"
            },
            new DataSourceAutoCollectionSource
            {
                Id = "international_results_recent_form",
                Enabled = true,
                SourceName = "international_results_recent_form",
                Provider = "international_results",
                SnapshotType = "team_recent_form"
            },
            new DataSourceAutoCollectionSource
            {
                Id = "openfootball_schedule",
                Enabled = true,
                SourceName = "openfootball_schedule",
                Provider = "openfootball_schedule"
            },
            new DataSourceAutoCollectionSource
            {
                Id = "fixturedownload_schedule",
                Enabled = true,
                SourceName = "fixturedownload_schedule",
                Provider = "fixturedownload_schedule"
            },
            new DataSourceAutoCollectionSource
            {
                Id = "espn_scoreboard",
                Enabled = true,
                SourceName = "espn_scoreboard",
                Provider = "espn_scoreboard",
                SnapshotType = "fixture_status",
                Query = "1:7",
                TimeoutSeconds = 30
            },
            new DataSourceAutoCollectionSource
            {
                Id = "espn_summary",
                Enabled = true,
                SourceName = "espn_summary",
                Provider = "espn_summary",
                SnapshotType = "match_summary",
                Query = "1:2",
                TimeoutSeconds = 45
            },
            new DataSourceAutoCollectionSource
            {
                Id = "rss_soccer_news",
                Enabled = true,
                SourceName = "rss_soccer_news",
                Provider = "rss_news",
                SnapshotType = "news_intel",
                Query = "World Cup injury lineup squad",
                TimeoutSeconds = 12
            }
        ];
    }

    public static DataSourceAutoCollectionConfig LoadAutoCollectionConfig()
    {
        EnsureDefaultAutoCollectionConfig();
        try
        {
            var json = File.ReadAllText(AppContext.AutoCollectionConfigPath, Encoding.UTF8);
            var config = JsonSerializer.Deserialize(json, AppJsonContext.Default.DataSourceAutoCollectionConfig);
            return config ?? new DataSourceAutoCollectionConfig { Enabled = false };
        }
        catch
        {
            return new DataSourceAutoCollectionConfig { Enabled = false };
        }
    }

    public static DataSourceAutoCollectionConfig SaveAutoCollectionConfig(DataSourceAutoCollectionConfig config)
    {
        var sanitized = SanitizeAutoCollectionConfig(config);
        Directory.CreateDirectory(Path.GetDirectoryName(AppContext.AutoCollectionConfigPath) ?? ".");
        File.WriteAllText(
            AppContext.AutoCollectionConfigPath,
            JsonSerializer.Serialize(sanitized, AppJsonContext.Default.DataSourceAutoCollectionConfig),
            Encoding.UTF8);
        return sanitized;
    }

    private static DataSourceAutoCollectionConfig SanitizeAutoCollectionConfig(DataSourceAutoCollectionConfig config)
    {
        config.IntervalMinutes = Math.Clamp(config.IntervalMinutes, 5, 24 * 60);
        config.MaxReportTeams = Math.Clamp(config.MaxReportTeams, 0, 48);
        config.TriageSnapshotLimit = Math.Clamp(config.TriageSnapshotLimit, 50, 5000);
        config.LlmReportIntervalMinutes = Math.Clamp(config.LlmReportIntervalMinutes, 60, 24 * 60);
        config.MaxLlmReportTeams = Math.Clamp(config.MaxLlmReportTeams, 1, 8);
        foreach (var source in config.Sources)
        {
            if (string.IsNullOrWhiteSpace(source.Id)) source.Id = Guid.NewGuid().ToString("N");
            if (string.IsNullOrWhiteSpace(source.SourceName)) source.SourceName = source.Id;
        }
        return config;
    }

    public static async Task RunAutoCollectionLoopAsync()
    {
        var store = AppContext.WorldCupStore;
        await Task.Delay(TimeSpan.FromSeconds(15));
        while (true)
        {
            var config = LoadAutoCollectionConfig();
            if (config.Enabled)
            {
                await RunAutoCollectionWithLockAsync(config, includeMaintenance: true);
            }

            var delay = ResolveAdaptiveDelay(config);
            if (AppContext.LastAutoCollectionRun != null)
            {
                AppContext.LastAutoCollectionRun.NextIntervalMinutes = delay.Minutes;
                AppContext.LastAutoCollectionRun.NextIntervalReason = delay.Reason;
            }
            var delayMinutes = delay.Minutes;
            await Task.Delay(TimeSpan.FromMinutes(delayMinutes));
        }
    }

    public static async Task<DataSourceAutoCollectionRunResult> RunAutoCollectionWithLockAsync(
        DataSourceAutoCollectionConfig config,
        bool includeMaintenance)
    {
        if (!await AppContext.AutoCollectionLock.WaitAsync(0))
        {
            return new DataSourceAutoCollectionRunResult
            {
                StartedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                CompletedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                Passed = false,
                Notes = ["Auto collection is already running; skipped this overlapping request."]
            };
        }

        try
        {
            AppContext.CurrentAutoCollectionRun = new DataSourceAutoCollectionRunResult
            {
                StartedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                Running = true,
                Notes = ["Auto collection is running."]
            };
            var result = await AppContext.WorldCupStore.RunAutoCollectionAsync(config);
            result.Running = false;
            AppContext.LastAutoCollectionRun = result;
            if (includeMaintenance)
            {
                await RefreshModelBacktestCacheIfDueAsync();
                await RunAutoLlmReportsIfDueAsync(config);
            }
            AppContext.CurrentAutoCollectionRun = null;
            return result;
        }
        catch (Exception ex)
        {
            var failed = new DataSourceAutoCollectionRunResult
            {
                StartedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                CompletedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                Passed = false,
                Notes = [$"Auto collection failed: {ex.Message}"]
            };
            AppContext.LastAutoCollectionRun = failed;
            AppContext.CurrentAutoCollectionRun = null;
            return failed;
        }
        finally
        {
            AppContext.CurrentAutoCollectionRun = null;
            AppContext.AutoCollectionLock.Release();
        }
    }

    private static async Task RefreshModelBacktestCacheIfDueAsync()
    {
        var store = AppContext.WorldCupStore;
        var cached = store.GetCachedModelBacktest(out _, out var cacheAgeMinutes);
        if (cached != null && cacheAgeMinutes is < ModelBacktestCacheMaxAgeMinutes)
        {
            return;
        }

        try
        {
            var result = await store.RunAndCacheModelBacktestAsync(365, 80);
            AppContext.LastAutoCollectionRun?.Notes.Add(
                $"模型回测缓存已刷新：{result.SamplesUsed} 场，Top1 命中 {Math.Round(result.Top1HitRate * 100, 1)}%。");
        }
        catch (Exception ex)
        {
            AppContext.LastAutoCollectionRun?.Notes.Add($"模型回测缓存刷新失败：{ex.Message}");
        }
    }

    private static async Task RunAutoLlmReportsIfDueAsync(DataSourceAutoCollectionConfig config)
    {
        if (!config.AutoLlmReportsEnabled)
        {
            return;
        }

        if (AppContext.LastAutoLlmReportRun != null
            && DateTime.TryParse(AppContext.LastAutoLlmReportRun.CompletedAt, out var completedAt)
            && DateTime.Now - completedAt < TimeSpan.FromMinutes(config.LlmReportIntervalMinutes))
        {
            AppContext.LastAutoCollectionRun?.Notes.Add(
                $"LLM 深度报告未到运行间隔，下一轮间隔 {config.LlmReportIntervalMinutes} 分钟。");
            return;
        }

        AppContext.LastAutoLlmReportRun = await TeamIntelligenceLlmReportService.RunBatchAsync(
            config.MaxLlmReportTeams,
            maxSignals: 8);
        AppContext.LastAutoCollectionRun?.Notes.Add(
            $"LLM 深度报告批处理完成：生成 {AppContext.LastAutoLlmReportRun.ReportsCreated} 份，失败 {AppContext.LastAutoLlmReportRun.FailedReports} 份。");
    }

    public static (int Minutes, string Reason) ResolveNextDelay(DataSourceAutoCollectionConfig config)
    {
        var delay = ResolveAdaptiveDelay(config);
        return (delay.Minutes, delay.Reason);
    }

    private static AutoCollectionDelay ResolveAdaptiveDelay(DataSourceAutoCollectionConfig config)
    {
        var baseInterval = Math.Clamp(config.IntervalMinutes, 5, 24 * 60);
        if (!config.AdaptiveScheduleEnabled)
        {
            return new AutoCollectionDelay(baseInterval, $"固定间隔 {baseInterval} 分钟。");
        }

        try
        {
            var now = DateTime.Now;
            var nextKickoff = AppContext.WorldCupStore.GetProductionMatches()
                .Select(match => DateTime.TryParse(match.KickoffTime, out var kickoff) ? kickoff : (DateTime?)null)
                .Where(kickoff => kickoff.HasValue && kickoff.Value >= now.AddHours(-2))
                .OrderBy(kickoff => kickoff)
                .FirstOrDefault();
            if (!nextKickoff.HasValue)
            {
                return new AutoCollectionDelay(baseInterval, $"未找到未来赛程，使用基础间隔 {baseInterval} 分钟。");
            }

            var hours = (nextKickoff.Value - now).TotalHours;
            if (hours <= 2)
            {
                return new AutoCollectionDelay(Math.Min(baseInterval, 5), "下一场比赛处于开赛前后 2 小时窗口，使用 5 分钟采集间隔。");
            }
            if (hours <= 12)
            {
                return new AutoCollectionDelay(Math.Min(baseInterval, 10), "下一场比赛 12 小时内，使用 10 分钟采集间隔。");
            }
            if (hours <= 48)
            {
                return new AutoCollectionDelay(Math.Min(baseInterval, 15), "下一场比赛 48 小时内，使用 15 分钟采集间隔。");
            }
            if (hours <= 24 * 7)
            {
                return new AutoCollectionDelay(Math.Min(baseInterval, 30), "下一场比赛 7 天内，使用不高于 30 分钟采集间隔。");
            }

            return new AutoCollectionDelay(baseInterval, $"下一场比赛距离较远，使用基础间隔 {baseInterval} 分钟。");
        }
        catch (Exception ex)
        {
            return new AutoCollectionDelay(baseInterval, $"自适应间隔计算失败，使用基础间隔 {baseInterval} 分钟：{ex.Message}");
        }
    }

    private sealed record AutoCollectionDelay(int Minutes, string Reason);
}

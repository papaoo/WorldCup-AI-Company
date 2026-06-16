using System.Globalization;
using System.Text;
using System.Text.Json;

namespace PiPiClaw.Team;

public sealed partial class WorldCupStore
{
    private const string InternationalResultsBacktestUrl = "https://cdn.jsdelivr.net/gh/martj42/international_results@master/results.csv";
    private static readonly string[] InternationalResultsBacktestUrls =
    [
        InternationalResultsBacktestUrl,
        "https://raw.githubusercontent.com/martj42/international_results/master/results.csv"
    ];

    public async Task<ModelBacktestResult> RunAndCacheModelBacktestAsync(int days = 365, int limit = 80)
    {
        var result = await RunModelBacktestAsync(days, limit);
        SaveModelBacktestCache(result);
        return result;
    }

    public ModelBacktestResult? GetCachedModelBacktest(out string? cachedAt, out int? cacheAgeMinutes)
    {
        cachedAt = null;
        cacheAgeMinutes = null;
        var path = AppContext.ModelBacktestCachePath;
        if (!File.Exists(path)) return null;

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
            var root = doc.RootElement;
            if (root.TryGetProperty("cached_at", out var cachedAtElement))
            {
                cachedAt = cachedAtElement.GetString();
                if (DateTime.TryParse(cachedAt, out var parsed))
                {
                    cacheAgeMinutes = Math.Max(0, (int)Math.Round((DateTime.Now - parsed).TotalMinutes));
                }
            }

            return root.TryGetProperty("backtest", out var backtest)
                ? JsonSerializer.Deserialize(backtest, AppJsonContext.Default.ModelBacktestResult)
                : null;
        }
        catch
        {
            return null;
        }
    }

    public async Task<ModelBacktestResult> RunModelBacktestAsync(int days = 1095, int limit = 300)
    {
        var cappedDays = Math.Clamp(days, 180, 3650);
        var cappedLimit = Math.Clamp(limit, 30, 1200);
        var since = DateTime.UtcNow.Date.AddDays(-cappedDays);
        var teams = GetProductionWatchObjects()
            .Where(team => team.Type == "football_team")
            .ToList();
        var teamMap = BuildBacktestTeamMap(teams);
        var snapshots = GetProductionDataSnapshots();
        var rows = await LoadBacktestRowsAsync(since);
        var result = new ModelBacktestResult
        {
            StrategyVersion = BaselinePredictionStrategy.Version,
            Source = "martj42/international_results",
            DateFrom = since.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            DateTo = DateTime.UtcNow.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            SamplesChecked = rows.Count
        };

        var items = new List<ModelBacktestItem>();
        foreach (var row in rows.OrderByDescending(item => item.Date))
        {
            if (items.Count >= cappedLimit) break;
            if (!teamMap.TryGetValue(NormalizeBacktestTeamName(row.HomeTeam), out var home)
                || !teamMap.TryGetValue(NormalizeBacktestTeamName(row.AwayTeam), out var away))
            {
                result.SkippedUnmapped++;
                continue;
            }

            var match = new WorldCupMatch
            {
                Id = $"backtest_{row.Date:yyyyMMdd}_{home.Symbol}_{away.Symbol}",
                Stage = "group",
                GroupName = "",
                HomeObjectId = home.Id,
                AwayObjectId = away.Id,
                KickoffTime = row.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Venue = row.Tournament,
                Status = "finished",
                HomeScore = row.HomeScore,
                AwayScore = row.AwayScore
            };
            var predictionSnapshots = snapshots
                .Where(snapshot => snapshot.ObjectId == home.Id || snapshot.ObjectId == away.Id)
                .OrderByDescending(snapshot => snapshot.CapturedAt, StringComparer.OrdinalIgnoreCase)
                .Take(80)
                .ToList();
            var prediction = BaselinePredictionStrategy.Predict(match, home, away, predictionSnapshots);
            var actual = ResolveOutcome(row.HomeScore, row.AwayScore);
            var predicted = ResolvePredictedOutcome(prediction);
            var brier = CalculateBrierScore(prediction, actual);
            var logLoss = CalculateLogLoss(prediction, actual);

            items.Add(new ModelBacktestItem
            {
                Date = row.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                HomeTeam = home.DisplayName,
                AwayTeam = away.DisplayName,
                Score = $"{row.HomeScore}:{row.AwayScore}",
                Tournament = row.Tournament,
                ActualOutcome = actual,
                PredictedOutcome = predicted,
                Hit = actual == predicted,
                HomeWinProbability = Math.Round(prediction.HomeWinProbability, 4),
                DrawProbability = Math.Round(prediction.DrawProbability, 4),
                AwayWinProbability = Math.Round(prediction.AwayWinProbability, 4),
                BrierScore = Math.Round(brier, 6),
                LogLoss = Math.Round(logLoss, 6)
            });
        }

        result.SamplesUsed = items.Count;
        result.Top1HitCount = items.Count(item => item.Hit);
        result.Top1HitRate = Rate(result.Top1HitCount, result.SamplesUsed);
        result.AverageBrierScore = items.Count == 0 ? 0 : Math.Round(items.Average(item => item.BrierScore), 6);
        result.AverageLogLoss = items.Count == 0 ? 0 : Math.Round(items.Average(item => item.LogLoss), 6);
        result.DrawSamples = items.Count(item => item.ActualOutcome == "draw");
        result.DrawHitCount = items.Count(item => item.ActualOutcome == "draw" && item.PredictedOutcome == "draw");
        result.DrawRecall = Rate(result.DrawHitCount, result.DrawSamples);

        var favoriteItems = items
            .Where(item => Math.Max(item.HomeWinProbability, Math.Max(item.DrawProbability, item.AwayWinProbability)) >= 0.58)
            .ToList();
        result.FavoriteSamples = favoriteItems.Count;
        result.FavoriteHitRate = Rate(favoriteItems.Count(item => item.Hit), favoriteItems.Count);
        result.Buckets = BuildBacktestBuckets(items);
        result.SampleItems = items
            .OrderByDescending(item => item.Date, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(item => item.BrierScore)
            .Take(24)
            .ToList();
        result.Passed = result.SamplesUsed >= 30
            && result.Top1HitRate >= 0
            && result.Top1HitRate <= 1
            && result.AverageBrierScore >= 0
            && result.AverageLogLoss >= 0;
        result.Notes.Add("这是当前快照校准回测：使用现有 FIFA/Elo/近期状态快照预测历史对阵，适合发现偏差，但不是严格的时间序列无泄漏回测。");
        result.Notes.Add("伤停、首发、实时赔率尚未进入概率模型，赛前投注建议仍需人工或 LLM 复核。");
        if (result.SkippedUnmapped > 0) result.Notes.Add($"有 {result.SkippedUnmapped} 场因球队不在当前 48 队生产名单或名称无法映射被跳过。");
        if (result.DrawSamples > 0 && result.DrawRecall < 0.10) result.Notes.Add("平局识别偏弱，后续需要单独校准 draw 模块。");
        return result;
    }

    private static Dictionary<string, WorldCupWatchObject> BuildBacktestTeamMap(IReadOnlyList<WorldCupWatchObject> teams)
    {
        var map = new Dictionary<string, WorldCupWatchObject>(StringComparer.OrdinalIgnoreCase);
        foreach (var team in teams)
        {
            foreach (var alias in BuildTeamAliases(team))
            {
                var key = NormalizeBacktestTeamName(alias);
                if (!string.IsNullOrWhiteSpace(key) && !map.ContainsKey(key))
                {
                    map[key] = team;
                }
            }
        }
        return map;
    }

    private static void SaveModelBacktestCache(ModelBacktestResult result)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(AppContext.ModelBacktestCachePath) ?? ".");
        var cachedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        var backtestJson = JsonSerializer.Serialize(result, AppJsonContext.Default.ModelBacktestResult);
        var json = $$"""
        {
          "cached_at": "{{cachedAt}}",
          "backtest": {{backtestJson}}
        }
        """;
        File.WriteAllText(AppContext.ModelBacktestCachePath, json, Encoding.UTF8);
    }

    private static async Task<List<BacktestResultRow>> LoadBacktestRowsAsync(DateTime since)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(150) };
        string? csv = null;
        var failures = new List<string>();
        foreach (var url in InternationalResultsBacktestUrls)
        {
            try
            {
                csv = await client.GetStringAsync(url);
                break;
            }
            catch (Exception ex)
            {
                failures.Add($"{url}: {ex.Message}");
            }
        }
        if (string.IsNullOrWhiteSpace(csv))
        {
            throw new InvalidOperationException($"Unable to load international results backtest data. {string.Join(" | ", failures)}");
        }

        var rows = new List<BacktestResultRow>();
        foreach (var line in csv.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Skip(1))
        {
            var fields = ParseBacktestCsvLine(line);
            if (fields.Count < 6) continue;
            if (!DateTime.TryParse(fields[0], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date) || date.Date < since) continue;
            if (!int.TryParse(fields[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var homeScore)) continue;
            if (!int.TryParse(fields[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var awayScore)) continue;
            rows.Add(new BacktestResultRow(date.Date, fields[1], fields[2], homeScore, awayScore, fields[5]));
        }
        return rows;
    }

    private static List<ModelBacktestBucket> BuildBacktestBuckets(IReadOnlyList<ModelBacktestItem> items)
    {
        var buckets = new (string Label, double Min, double Max)[]
        {
            ("最高概率 <45%", 0.00, 0.45),
            ("最高概率 45%-55%", 0.45, 0.55),
            ("最高概率 55%-65%", 0.55, 0.65),
            ("最高概率 >=65%", 0.65, 1.01)
        };

        return buckets
            .Select(bucket =>
            {
                var slice = items
                    .Where(item =>
                    {
                        var top = Math.Max(item.HomeWinProbability, Math.Max(item.DrawProbability, item.AwayWinProbability));
                        return top >= bucket.Min && top < bucket.Max;
                    })
                    .ToList();
                return new ModelBacktestBucket
                {
                    Label = bucket.Label,
                    Samples = slice.Count,
                    HitRate = Rate(slice.Count(item => item.Hit), slice.Count),
                    AverageBrierScore = slice.Count == 0 ? 0 : Math.Round(slice.Average(item => item.BrierScore), 6)
                };
            })
            .ToList();
    }

    private static double CalculateLogLoss(BaselinePredictionRecord prediction, string actual)
    {
        var probability = actual switch
        {
            "home_win" => prediction.HomeWinProbability,
            "draw" => prediction.DrawProbability,
            "away_win" => prediction.AwayWinProbability,
            _ => 0.000001
        };
        return -Math.Log(Math.Clamp(probability, 0.000001, 0.999999));
    }

    private static double Rate(int numerator, int denominator)
    {
        return denominator == 0 ? 0 : Math.Round(numerator / (double)denominator, 6);
    }

    private static string NormalizeBacktestTeamName(string value)
    {
        return value.Trim()
            .ToLowerInvariant()
            .Replace("`", "'", StringComparison.Ordinal)
            .Replace("’", "'", StringComparison.Ordinal)
            .Replace("é", "e", StringComparison.Ordinal)
            .Replace("è", "e", StringComparison.Ordinal)
            .Replace("ê", "e", StringComparison.Ordinal)
            .Replace("á", "a", StringComparison.Ordinal)
            .Replace("ã", "a", StringComparison.Ordinal)
            .Replace("ç", "c", StringComparison.Ordinal)
            .Replace("ô", "o", StringComparison.Ordinal)
            .Replace("ö", "o", StringComparison.Ordinal);
    }

    private static List<string> ParseBacktestCsvLine(string line)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (ch == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
                continue;
            }
            if (ch == ',' && !inQuotes)
            {
                fields.Add(current.ToString());
                current.Clear();
                continue;
            }
            current.Append(ch);
        }
        fields.Add(current.ToString());
        return fields;
    }

    private sealed record BacktestResultRow(
        DateTime Date,
        string HomeTeam,
        string AwayTeam,
        int HomeScore,
        int AwayScore,
        string Tournament);
}

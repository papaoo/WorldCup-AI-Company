using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace PiPiClaw.Team;

public sealed partial class WorldCupStore
{
    public BaselinePredictionRecord CreateBaselinePrediction(string matchId)
    {
        using var connection = OpenConnection();
        var match = GetMatch(connection, matchId) ?? throw new InvalidOperationException($"Match not found: {matchId}");
        var home = GetWatchObject(connection, match.HomeObjectId) ?? throw new InvalidOperationException($"Home team not found: {match.HomeObjectId}");
        var away = GetWatchObject(connection, match.AwayObjectId) ?? throw new InvalidOperationException($"Away team not found: {match.AwayObjectId}");

        var snapshots = GetPredictionSnapshots(match.Id, home.Id, away.Id);
        var prediction = BaselinePredictionStrategy.Predict(match, home, away, snapshots);
        UpsertBaselinePrediction(connection, prediction);
        SaveSystemEventLog(connection, null, new WorldCupSystemEventLog
        {
            EventType = "baseline_prediction_created",
            Category = "algorithm",
            Source = prediction.StrategyVersion,
            ObjectId = home.Id,
            MatchId = match.Id,
            Title = "Baseline prediction refreshed",
            Message = $"{home.DisplayName} {prediction.HomeWinProbability:P1}, draw {prediction.DrawProbability:P1}, {away.DisplayName} {prediction.AwayWinProbability:P1}.",
            PayloadJson = new JsonObject
            {
                ["baseline_prediction_id"] = prediction.Id,
                ["strategy_version"] = prediction.StrategyVersion,
                ["method"] = prediction.Method,
                ["home_object_id"] = home.Id,
                ["away_object_id"] = away.Id,
                ["home_win_probability"] = prediction.HomeWinProbability,
                ["draw_probability"] = prediction.DrawProbability,
                ["away_win_probability"] = prediction.AwayWinProbability,
                ["input_snapshot_ids_json"] = prediction.InputSnapshotIdsJson,
                ["explanation"] = prediction.Explanation
            }.ToJsonString()
        });
        return prediction;
    }

    private List<DataSnapshotRecord> GetPredictionSnapshots(string matchId, string homeObjectId, string awayObjectId)
    {
        return GetDataSnapshots(matchId)
            .Concat(GetDataSnapshots(objectId: homeObjectId))
            .Concat(GetDataSnapshots(objectId: awayObjectId))
            .GroupBy(snapshot => snapshot.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderByDescending(snapshot => snapshot.CapturedAt, StringComparer.OrdinalIgnoreCase)
            .Take(80)
            .ToList();
    }

    public BaselineBacktestResult RefreshProductionBaselinePredictions()
    {
        var result = new BaselineBacktestResult();
        foreach (var match in GetProductionMatches())
        {
            result.MatchesChecked++;
            BaselinePredictionRecord prediction;
            try
            {
                prediction = CreateBaselinePrediction(match.Id);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("Cannot create", StringComparison.OrdinalIgnoreCase)
                && ex.Message.Contains("prediction", StringComparison.OrdinalIgnoreCase))
            {
                result.Notes.Add($"Skipped {match.Id}: {ex.Message}");
                continue;
            }

            result.PredictionsCreated++;
            var probs = new[] { prediction.HomeWinProbability, prediction.DrawProbability, prediction.AwayWinProbability };
            if (probs.Any(p => p < 0 || p > 1 || double.IsNaN(p) || double.IsInfinity(p)))
            {
                result.InvalidProbabilityCount++;
                result.Notes.Add($"Invalid probability in match {match.Id}.");
            }

            var sumError = Math.Abs(1.0 - probs.Sum());
            result.MaxProbabilitySumError = Math.Max(result.MaxProbabilitySumError, sumError);
            if (sumError > 0.000001)
            {
                result.InvalidProbabilityCount++;
                result.Notes.Add($"Probability sum error {sumError:0.########} in match {match.Id}.");
            }

            if (prediction.StrategyVersion == BaselinePredictionStrategy.Version
                && prediction.Method == "snapshot_aware_factor"
                && PredictionHasFactorPayload(prediction.InputSnapshotIdsJson))
            {
                result.FactorPredictions++;
            }
            if (PredictionHasSnapshotAwarePayload(prediction.InputSnapshotIdsJson))
            {
                result.SnapshotAwarePredictions++;
            }
        }

        result.FactorPayloadsValid = result.PredictionsCreated > 0 && result.FactorPredictions == result.PredictionsCreated;
        result.SnapshotPayloadsValid = result.PredictionsCreated > 0 && result.SnapshotAwarePredictions == result.PredictionsCreated;
        if (result.MatchesChecked == 0) result.Notes.Add("No production matches are available. Run public data bootstrap first.");
        if (!result.FactorPayloadsValid) result.Notes.Add($"Expected factor payloads for {result.PredictionsCreated} predictions, got {result.FactorPredictions}.");
        if (!result.SnapshotPayloadsValid) result.Notes.Add($"Expected snapshot-aware payloads for {result.PredictionsCreated} predictions, got {result.SnapshotAwarePredictions}.");
        result.Passed = result.MatchesChecked > 0
            && result.PredictionsCreated == result.MatchesChecked
            && result.InvalidProbabilityCount == 0
            && result.FactorPayloadsValid
            && result.SnapshotPayloadsValid;
        return result;
    }

    public WorldCupMatch RecordMatchResult(MatchResultRequest request)
    {
        ValidateMatchResultRequest(request);
        using var connection = OpenConnection();
        var match = GetMatch(connection, request.MatchId) ?? throw new InvalidOperationException($"Match not found: {request.MatchId}");
        match.HomeScore = request.HomeScore;
        match.AwayScore = request.AwayScore;
        match.Status = "finished";

        using var transaction = connection.BeginTransaction();
        UpsertMatch(connection, transaction, match);
        transaction.Commit();
        return match;
    }

    public WorldCupLifecycleResult ApplyMatchLifecycle(string matchId)
    {
        using var connection = OpenConnection();
        var match = GetMatch(connection, matchId) ?? throw new InvalidOperationException($"Match not found: {matchId}");
        var result = new WorldCupLifecycleResult
        {
            MatchId = match.Id,
            Stage = match.Stage
        };

        if (match.Status != "finished" || match.HomeScore == null || match.AwayScore == null)
        {
            result.Notes.Add("Match is not finished; lifecycle was not applied.");
            return result;
        }

        if (!IsKnockoutStage(match.Stage))
        {
            result.Notes.Add($"Stage '{match.Stage}' is not a knockout stage; no team elimination is applied.");
            return result;
        }

        if (match.HomeScore == match.AwayScore)
        {
            result.Notes.Add("Knockout match ended with a draw in recorded score; winner must be resolved before elimination.");
            return result;
        }

        var winnerObjectId = match.HomeScore > match.AwayScore ? match.HomeObjectId : match.AwayObjectId;
        var loserObjectId = match.HomeScore > match.AwayScore ? match.AwayObjectId : match.HomeObjectId;
        var winner = GetWatchObject(connection, winnerObjectId) ?? throw new InvalidOperationException($"Winner team not found: {winnerObjectId}");
        var loser = GetWatchObject(connection, loserObjectId) ?? throw new InvalidOperationException($"Loser team not found: {loserObjectId}");
        var loserEmployeeId = FindPrimaryEmployeeId(connection, loser.Id);
        var loserEmployee = string.IsNullOrWhiteSpace(loserEmployeeId) ? null : GetEmployee(connection, loserEmployeeId);
        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        winner.Status = "active";
        winner.UpdatedAt = now;
        loser.Status = "eliminated";
        loser.UpdatedAt = now;
        if (loserEmployee != null)
        {
            loserEmployee.Status = "offboarded";
            loserEmployee.UpdatedAt = now;
        }

        using var transaction = connection.BeginTransaction();
        UpsertWatchObject(connection, transaction, winner);
        UpsertWatchObject(connection, transaction, loser);
        if (loserEmployee != null)
        {
            UpsertEmployee(connection, transaction, loserEmployee);
        }
        EndActiveAssignmentsForObject(connection, transaction, loser.Id, now, $$"""{"reason":"knockout_elimination","match_id":"{{match.Id}}"}""");
        transaction.Commit();

        var memory = AddMemory(new MemoryCreateRequest
        {
            Scope = "object",
            OwnerId = loserEmployee?.Id,
            ObjectId = loser.Id,
            MemoryType = "lifecycle",
            Content = $"{loser.DisplayName} was eliminated by {winner.DisplayName} in {match.Stage} ({match.HomeScore}:{match.AwayScore}). The primary researcher was offboarded from this team assignment.",
            Summary = $"{loser.DisplayName} eliminated in {match.Stage}; assigned employee offboarded.",
            TagsJson = """["worldcup","lifecycle","elimination"]""",
            SourceType = "match_lifecycle",
            SourceId = match.Id,
            Importance = 0.9,
            Confidence = 0.95
        });

        result.Applied = true;
        result.WinnerObjectId = winner.Id;
        result.LoserObjectId = loser.Id;
        result.OffboardedEmployeeId = loserEmployee?.Id;
        result.MemoryId = memory.Id;
        return result;
    }

    public MatchReviewRecord CreateMatchReview(string matchId)
    {
        using var connection = OpenConnection();
        var match = GetMatch(connection, matchId) ?? throw new InvalidOperationException($"Match not found: {matchId}");
        if (match.HomeScore == null || match.AwayScore == null)
        {
            throw new InvalidOperationException($"Match result not recorded: {matchId}");
        }

        var home = GetWatchObject(connection, match.HomeObjectId) ?? throw new InvalidOperationException($"Home team not found: {match.HomeObjectId}");
        var away = GetWatchObject(connection, match.AwayObjectId) ?? throw new InvalidOperationException($"Away team not found: {match.AwayObjectId}");
        var prediction = GetBaselinePredictions(matchId).FirstOrDefault() ?? CreateBaselinePrediction(matchId);
        var actual = ResolveOutcome(match.HomeScore.Value, match.AwayScore.Value);
        var predicted = ResolvePredictedOutcome(prediction);
        var brier = CalculateBrierScore(prediction, actual);
        var hit = actual == predicted;

        var content = BuildMatchReviewReport(match, home, away, prediction, actual, predicted, hit, brier);
        var artifact = new ArtifactRecord
        {
            Id = $"artifact_review_{match.Id}",
            Type = "markdown",
            Title = $"{home.DisplayName} vs {away.DisplayName} 赛后复盘报告",
            ObjectId = home.Id,
            FilePath = Path.Combine("artifacts", $"review_{match.Id}.md"),
            Summary = $"赛后复盘：实际={actual}, 预测={predicted}, 命中={hit}, Brier={brier:0.000}",
            MetadataJson = $$"""{"match_id":"{{match.Id}}","actual_outcome":"{{actual}}","predicted_outcome":"{{predicted}}","brier_score":{{brier:0.000000}}}"""
        };

        using var transaction = connection.BeginTransaction();
        var savedArtifact = SaveArtifact(connection, transaction, artifact, content);
        transaction.Commit();

        var homeMemory = AddMemory(new MemoryCreateRequest
        {
            Scope = "strategy",
            ObjectId = home.Id,
            MemoryType = "review",
            Content = content,
            Summary = $"Review {match.Id}: actual={actual}, predicted={predicted}, hit={hit}, brier={brier:0.000}",
            SourceType = "match_review",
            SourceId = match.Id,
            Importance = hit ? 0.55 : 0.8,
            Confidence = 0.8
        });
        _ = AddMemory(new MemoryCreateRequest
        {
            Scope = "strategy",
            ObjectId = away.Id,
            MemoryType = "review",
            Content = content,
            Summary = $"Review {match.Id}: actual={actual}, predicted={predicted}, hit={hit}, brier={brier:0.000}",
            SourceType = "match_review",
            SourceId = match.Id,
            Importance = hit ? 0.55 : 0.8,
            Confidence = 0.8
        });

        return new MatchReviewRecord
        {
            Match = match,
            Prediction = prediction,
            ActualOutcome = actual,
            PredictedOutcome = predicted,
            Hit = hit,
            BrierScore = brier,
            Artifact = savedArtifact,
            Memory = homeMemory
        };
    }

    public StrategyEvaluationSummary GetStrategyEvaluation(bool includeTestData = false)
    {
        var matches = (includeTestData ? GetMatches() : GetProductionMatches())
            .Where(match => match.Status == "finished" && match.HomeScore != null && match.AwayScore != null)
            .ToList();
        var items = new List<StrategyEvaluationItem>();

        foreach (var match in matches)
        {
            var prediction = GetBaselinePredictions(match.Id).FirstOrDefault();
            if (prediction == null)
            {
                continue;
            }

            var home = GetWatchObjectById(match.HomeObjectId);
            var away = GetWatchObjectById(match.AwayObjectId);
            var actual = ResolveOutcome(match.HomeScore!.Value, match.AwayScore!.Value);
            var predicted = ResolvePredictedOutcome(prediction);
            var brier = CalculateBrierScore(prediction, actual);

            items.Add(new StrategyEvaluationItem
            {
                MatchId = match.Id,
                HomeTeam = home?.DisplayName ?? match.HomeObjectId,
                AwayTeam = away?.DisplayName ?? match.AwayObjectId,
                Score = $"{match.HomeScore}:{match.AwayScore}",
                ActualOutcome = actual,
                PredictedOutcome = predicted,
                Hit = actual == predicted,
                BrierScore = brier,
                ReviewedAt = prediction.CreatedAt
            });
        }

        items = items
            .OrderByDescending(item => item.ReviewedAt)
            .ThenBy(item => item.MatchId)
            .ToList();

        var reviewed = items.Count;
        var hitCount = items.Count(item => item.Hit);
        return new StrategyEvaluationSummary
        {
            StrategyVersion = items.Count > 0
                ? GetBaselinePredictions(items[0].MatchId).FirstOrDefault()?.StrategyVersion ?? "baseline_rank_v0"
                : "baseline_rank_v0",
            ReviewedMatches = reviewed,
            HitCount = hitCount,
            HitRate = reviewed == 0 ? 0 : (double)hitCount / reviewed,
            AverageBrierScore = reviewed == 0 ? 0 : items.Average(item => item.BrierScore),
            LatestReviewedAt = items.FirstOrDefault()?.ReviewedAt,
            Items = items.Take(12).ToList()
        };
    }
}

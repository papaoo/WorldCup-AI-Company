using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace PiPiClaw.Team;

public sealed partial class WorldCupStore
{
    public BaselineBacktestResult RunBaselineHarness()
    {
        var result = new BaselineBacktestResult();
        var matches = GetMatches();
        foreach (var match in matches)
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

        if (result.MatchesChecked == 0)
        {
            result.Notes.Add("No matches available. Run /api/worldcup/seed first.");
        }
        result.FactorPayloadsValid = result.PredictionsCreated > 0 && result.FactorPredictions == result.PredictionsCreated;
        result.SnapshotPayloadsValid = result.PredictionsCreated > 0 && result.SnapshotAwarePredictions == result.PredictionsCreated;
        if (!result.FactorPayloadsValid) result.Notes.Add($"Expected factor payloads for {result.PredictionsCreated} predictions, got {result.FactorPredictions}.");
        if (!result.SnapshotPayloadsValid) result.Notes.Add($"Expected snapshot-aware payloads for {result.PredictionsCreated} predictions, got {result.SnapshotAwarePredictions}.");
        result.Passed = result.MatchesChecked > 0 && result.PredictionsCreated > 0 && result.InvalidProbabilityCount == 0 && result.FactorPayloadsValid && result.SnapshotPayloadsValid;
        return result;
    }

    public MatchReviewHarnessResult RunMatchReviewHarness()
    {
        var matchId = "match_001"; // 使用新生成的赛程第一场
        var result = new MatchReviewHarnessResult { MatchId = matchId };
        CreateBaselinePrediction(matchId);
        var match = RecordMatchResult(new MatchResultRequest
        {
            MatchId = matchId,
            HomeScore = 2,
            AwayScore = 1
        });
        result.ResultRecorded = match.Status == "finished" && match.HomeScore == 2 && match.AwayScore == 1;

        var review = CreateMatchReview(matchId);
        result.ReviewCreated = !string.IsNullOrWhiteSpace(review.ActualOutcome);
        result.ArtifactCreated = !string.IsNullOrWhiteSpace(review.Artifact.FilePath)
            && File.Exists(Path.Combine(System.AppContext.BaseDirectory, review.Artifact.FilePath));
        result.MemoryWritten = !string.IsNullOrWhiteSpace(review.Memory.Id)
            && RecallRelevantMemories("emp_mex", "team_mex", 12).Any(memory => memory.SourceType == "match_review" && memory.SourceId == matchId);
        result.BrierScoreValid = review.BrierScore >= 0 && review.BrierScore <= 2;

        if (!result.ResultRecorded) result.Notes.Add("Match result was not recorded.");
        if (!result.ReviewCreated) result.Notes.Add("Review record was not created.");
        if (!result.ArtifactCreated) result.Notes.Add("Review artifact file was not created.");
        if (!result.MemoryWritten) result.Notes.Add("Review memory was not recalled.");
        if (!result.BrierScoreValid) result.Notes.Add($"Invalid Brier score: {review.BrierScore}.");

        result.Passed = result.ResultRecorded && result.ReviewCreated && result.ArtifactCreated && result.MemoryWritten && result.BrierScoreValid;
        return result;
    }

    public StrategyEvaluationHarnessResult RunStrategyEvaluationHarness()
    {
        RunMatchReviewHarness();
        var summary = GetStrategyEvaluation(includeTestData: true);
        var result = new StrategyEvaluationHarnessResult
        {
            ReviewedMatches = summary.ReviewedMatches,
            HitRateValid = summary.HitRate >= 0 && summary.HitRate <= 1,
            AverageBrierValid = summary.AverageBrierScore >= 0 && summary.AverageBrierScore <= 2,
            ContainsDemoMatch = summary.Items.Any(item => item.MatchId == "match_001")
        };

        if (result.ReviewedMatches == 0) result.Notes.Add("No reviewed matches were available.");
        if (!result.HitRateValid) result.Notes.Add($"Invalid hit rate: {summary.HitRate}.");
        if (!result.AverageBrierValid) result.Notes.Add($"Invalid average Brier score: {summary.AverageBrierScore}.");
        if (!result.ContainsDemoMatch) result.Notes.Add("Demo match match_001 was not included in the strategy evaluation.");

        result.Passed = result.ReviewedMatches > 0
            && result.HitRateValid
            && result.AverageBrierValid
            && result.ContainsDemoMatch;
        return result;
    }

    public DemoResultsHarnessResult RunDemoResultsHarness()
    {
        var result = new DemoResultsHarnessResult();
        var demoScores = new (string MatchId, int HomeScore, int AwayScore)[]
        {
            ("match_001", 2, 1),
            ("match_002", 2, 0),
            ("match_003", 1, 1),
            ("match_004", 1, 2)
        };

        foreach (var score in demoScores)
        {
            try
            {
                CreateBaselinePrediction(score.MatchId);
                var match = RecordMatchResult(new MatchResultRequest
                {
                    MatchId = score.MatchId,
                    HomeScore = score.HomeScore,
                    AwayScore = score.AwayScore
                });
                if (match.Status == "finished")
                {
                    result.ResultsRecorded++;
                }

                var review = CreateMatchReview(score.MatchId);
                if (!string.IsNullOrWhiteSpace(review.ActualOutcome))
                {
                    result.ReviewsCreated++;
                }
            }
            catch (Exception ex)
            {
                result.Notes.Add($"{score.MatchId}: {ex.Message}");
            }
        }

        result.Evaluation = GetStrategyEvaluation(includeTestData: true);
        if (result.ResultsRecorded != demoScores.Length) result.Notes.Add($"Expected {demoScores.Length} recorded results, got {result.ResultsRecorded}.");
        if (result.ReviewsCreated != demoScores.Length) result.Notes.Add($"Expected {demoScores.Length} reviews, got {result.ReviewsCreated}.");
        if (result.Evaluation.ReviewedMatches < demoScores.Length) result.Notes.Add($"Expected at least {demoScores.Length} reviewed matches, got {result.Evaluation.ReviewedMatches}.");
        if (result.Evaluation.HitRate < 0 || result.Evaluation.HitRate > 1) result.Notes.Add($"Invalid hit rate: {result.Evaluation.HitRate}.");
        if (result.Evaluation.AverageBrierScore < 0 || result.Evaluation.AverageBrierScore > 2) result.Notes.Add($"Invalid average Brier score: {result.Evaluation.AverageBrierScore}.");

        result.Passed = result.ResultsRecorded == demoScores.Length
            && result.ReviewsCreated == demoScores.Length
            && result.Evaluation.ReviewedMatches >= demoScores.Length
            && result.Evaluation.HitRate >= 0
            && result.Evaluation.HitRate <= 1
            && result.Evaluation.AverageBrierScore >= 0
            && result.Evaluation.AverageBrierScore <= 2;
        return result;
    }

    public WorldCupLifecycleHarnessResult RunLifecycleHarness()
    {
        SeedDemoWorldCupCompany();
        using (var connection = OpenConnection())
        using (var transaction = connection.BeginTransaction())
        {
            UpsertMatch(connection, transaction, new WorldCupMatch
            {
                Id = "match_lifecycle_001",
                Stage = "round_of_16",
                GroupName = "Lifecycle Demo",
                HomeObjectId = "team_mex",
                AwayObjectId = "team_ned",
                KickoffTime = "2026-07-01 20:00:00",
                Venue = "Lifecycle Harness Stadium",
                Status = "scheduled"
            });
            transaction.Commit();
        }

        CreateBaselinePrediction("match_lifecycle_001");
        RecordMatchResult(new MatchResultRequest
        {
            MatchId = "match_lifecycle_001",
            HomeScore = 2,
            AwayScore = 0
        });

        var lifecycle = ApplyMatchLifecycle("match_lifecycle_001");
        var loserTeam = GetWatchObjectById("team_ned");
        var winnerTeam = GetWatchObjectById("team_mex");
        var employee = GetEmployeeById("emp_ned");
        var assignment = GetAssignments().FirstOrDefault(item => item.Id == "assign_ned");
        var memoryWritten = RecallRelevantMemories("emp_ned", "team_ned", 12)
            .Any(memory => memory.SourceType == "match_lifecycle" && memory.SourceId == "match_lifecycle_001");

        var result = new WorldCupLifecycleHarnessResult
        {
            Lifecycle = lifecycle,
            LoserTeamEliminated = loserTeam?.Status == "eliminated",
            WinnerTeamActive = winnerTeam?.Status == "active",
            EmployeeOffboarded = employee?.Status == "offboarded",
            AssignmentEnded = assignment?.Status == "ended" && !string.IsNullOrWhiteSpace(assignment.EndedAt),
            MemoryWritten = memoryWritten
        };

        if (!lifecycle.Applied) result.Notes.Add("Lifecycle was not applied.");
        if (!result.LoserTeamEliminated) result.Notes.Add("Loser team was not marked eliminated.");
        if (!result.WinnerTeamActive) result.Notes.Add("Winner team was not kept active.");
        if (!result.EmployeeOffboarded) result.Notes.Add("Loser team's primary employee was not offboarded.");
        if (!result.AssignmentEnded) result.Notes.Add("Loser team's assignment was not ended.");
        if (!result.MemoryWritten) result.Notes.Add("Lifecycle memory was not written or recalled.");
        result.Passed = lifecycle.Applied
            && result.LoserTeamEliminated
            && result.WinnerTeamActive
            && result.EmployeeOffboarded
            && result.AssignmentEnded
            && result.MemoryWritten;
        return result;
    }

    public void SeedDemoWorldCupCompany()
    {
        // 2026世界杯48支球队，12个小组(A-L)
        var teams = new[]
        {
            // A组
            ("mex", "MEX", "Mexico", "\u58a8\u897f\u54e5", 15, "A"),
            ("ned", "NED", "Netherlands", "\u8377\u5170", 8, "A"),
            ("qatar", "QAT", "Qatar", "\u5361\u5854\u5c14", 58, "A"),
            ("ecu", "ECU", "Ecuador", "\u5384\u74dc\u591a\u5c14", 40, "A"),
            // B组
            ("usa", "USA", "United States", "\u7f8e\u56fd", 16, "B"),
            ("ger", "GER", "Germany", "\u5fb7\u56fd", 6, "B"),
            ("cmr", "CMR", "Cameroon", "\u5580\u9ea6\u9686", 49, "B"),
            ("wal", "WAL", "Wales", "\u5a01\u5c14\u58eb", 34, "B"),
            // C组
            ("arg", "ARG", "Argentina", "\u963f\u6839\u5ef7", 1, "C"),
            ("cro", "CRO", "Croatia", "\u514b\u7f57\u5730\u4e9a", 14, "C"),
            ("ksa", "KSA", "Saudi Arabia", "\u6c99\u7279\u963f\u62c9\u4f2f", 53, "C"),
            ("slo", "SLO", "Slovenia", "\u65af\u6d1b\u6587\u5c3c\u4e9a", 65, "C"),
            // D组
            ("bra", "BRA", "Brazil", "\u5df4\u897f", 3, "D"),
            ("den", "DEN", "Denmark", "\u4e39\u9ea6", 19, "D"),
            ("chn", "CHN", "China", "\u4e2d\u56fd", 88, "D"),
            ("tun", "TUN", "Tunisia", "\u7a81\u5c3c\u65af", 41, "D"),
            // E组
            ("fra", "FRA", "France", "\u6cd5\u56fd", 2, "E"),
            ("uru", "URU", "Uruguay", "\u4e4c\u62c9\u572d", 13, "E"),
            ("civ", "CIV", "Ivory Coast", "\u79d1\u7279\u8fea\u74e6", 51, "E"),
            ("nzl", "NZL", "New Zealand", "\u65b0\u897f\u5170", 104, "E"),
            // F组
            ("esp", "ESP", "Spain", "\u897f\u73ed\u7259", 5, "F"),
            ("srb", "SRB", "Serbia", "\u585e\u5c14\u7ef4\u4e9a", 30, "F"),
            ("mar", "MAR", "Morocco", "\u6469\u6d1b\u54e5", 17, "F"),
            ("can", "CAN", "Canada", "\u52a0\u62ff\u5927", 48, "F"),
            // G组
            ("eng", "ENG", "England", "\u82f1\u683c\u5170", 4, "G"),
            ("sui", "SUI", "Switzerland", "\u745e\u58eb", 20, "G"),
            ("sen", "SEN", "Senegal", "\u585e\u5185\u52a0\u5c14", 22, "G"),
            ("pan", "PAN", "Panama", "\u5df4\u62ff\u9a6c", 78, "G"),
            // H组
            ("por", "POR", "Portugal", "\u8461\u8404\u7259", 7, "H"),
            ("alg", "ALG", "Algeria", "\u963f\u5c14\u53ca\u5229\u4e9a", 43, "H"),
            ("jpn", "JPN", "Japan", "\u65e5\u672c", 18, "H"),
            ("hon", "HON", "Honduras", "\u6d2a\u90fd\u62c9\u65af", 76, "H"),
            // I组
            ("ita", "ITA", "Italy", "\u610f\u5927\u5229", 9, "I"),
            ("gha", "GHA", "Ghana", "\u52a0\u7eb3", 68, "I"),
            ("irn", "IRN", "Iran", "\u4f0a\u6717", 21, "I"),
            ("ukr", "UKR", "Ukraine", "\u4e4c\u514b\u5170", 32, "I"),
            // J组
            ("bel", "BEL", "Belgium", "\u6bd4\u5229\u65f6", 10, "J"),
            ("kor", "KOR", "South Korea", "\u97e9\u56fd", 28, "J"),
            ("par", "PAR", "Paraguay", "\u5df4\u62c9\u572d", 35, "J"),
            ("ang", "ANG", "Angola", "\u5b89\u54e5\u62c9", 93, "J"),
            // K组
            ("col", "COL", "Colombia", "\u54e5\u4f26\u6bd4\u4e9a", 12, "K"),
            ("aus", "AUS", "Australia", "\u6fb3\u5927\u5229\u4e9a", 24, "K"),
            ("crc", "CRC", "Costa Rica", "\u54e5\u65af\u8fbe\u9ece\u52a0", 46, "K"),
            ("tga", "TGA", "Tonga", "\u6c64\u52a0", 198, "K"),
            // L组
            ("arg2", "ARG2", "Argentina II", "\u963f\u6839\u5ef7II", 1, "L"),
            ("tur", "TUR", "Turkey", "\u571f\u8033\u5176", 29, "L"),
            ("ngr", "NGR", "Nigeria", "\u5c3c\u65e5\u5229\u4e9a", 38, "L"),
            ("isl", "ISL", "Iceland", "\u51b0\u5c9b", 70, "L")
        };

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        foreach (var team in teams)
        {
            var objectId = $"team_{team.Item1}";
            var employeeId = $"emp_{team.Item1}";
            var existingTeam = GetWatchObject(connection, objectId);
            var hasProductionTeam = existingTeam != null && !IsTestWatchObject(existingTeam);

            if (hasProductionTeam)
            {
                continue;
            }

            UpsertWatchObject(connection, transaction, new WorldCupWatchObject
            {
                Id = objectId,
                Type = "football_team",
                Symbol = team.Item2,
                Name = team.Item3,
                DisplayName = team.Item4,
                Status = "active",
                MetadataJson = $$"""{"fifa_rank":{{team.Item5}},"group":"{{team.Item6}}","demo":true}"""
            });

            var employee = new WorldCupEmployee
            {
                Id = employeeId,
                Name = $"{team.Item4}\u961f\u7814\u7a76\u5458",
                Role = "\u7403\u961f\u7814\u7a76\u5458",
                Specialty = $"{team.Item4}\u961f\u72b6\u6001\u3001\u9635\u5bb9\u3001\u6218\u672f\u548c\u98ce\u9669\u89c2\u5bdf",
                Status = "active",
                PromptProfile = $"\u4f60\u662f{team.Item4}\u961f\u7684\u957f\u671f AI \u7814\u7a76\u5458\uff0c\u5fc5\u987b\u57fa\u4e8e\u6570\u636e\u548c\u5386\u53f2\u8bb0\u5fc6\u8f93\u51fa\u514b\u5236\u3001\u53ef\u590d\u76d8\u7684\u5206\u6790\u3002",
                ContactsJson = """["\u6570\u636e\u5206\u6790\u5e08","\u98ce\u9669\u5b98","CEO"]"""
            };
            UpsertEmployee(connection, transaction, employee);
            UpsertAssignment(connection, transaction, new EmployeeAssignment
            {
                Id = $"assign_{team.Item1}",
                EmployeeId = employee.Id,
                ObjectId = objectId,
                AssignmentRole = "primary_researcher",
                Status = "active"
            });
        }

        foreach (var employee in new[]
        {
            new WorldCupEmployee { Id = "emp_data", Name = "\u6570\u636e\u5206\u6790\u5e08", Role = "\u6570\u636e\u5206\u6790\u5e08", Specialty = "\u57fa\u7ebf\u80dc\u7387\u3001\u5386\u53f2\u6570\u636e\u3001\u6982\u7387\u6821\u51c6", PromptProfile = "\u4f60\u8d1f\u8d23\u7528\u53ef\u89e3\u91ca\u6570\u636e\u6a21\u578b\u7ed9\u51fa\u57fa\u7ebf\u6982\u7387\uff0c\u4e0d\u505a\u60c5\u7eea\u5316\u5224\u65ad\u3002" },
            new WorldCupEmployee { Id = "emp_risk", Name = "\u98ce\u9669\u5b98", Role = "\u98ce\u9669\u5b98", Specialty = "\u51b7\u95e8\u98ce\u9669\u3001\u4f24\u75c5\u98ce\u9669\u3001\u6a21\u578b\u76f2\u533a", PromptProfile = "\u4f60\u8d1f\u8d23\u6311\u51fa\u9884\u6d4b\u4e2d\u7684\u4e0d\u786e\u5b9a\u6027\u3001\u6a21\u578b\u76f2\u533a\u548c\u51b7\u95e8\u98ce\u9669\u3002" },
            new WorldCupEmployee { Id = "emp_hr", Name = "HR", Role = "hr", Specialty = "\u5458\u5de5\u914d\u7f6e\u3001\u5c97\u4f4d\u72b6\u6001\u3001\u7403\u961f\u6dd8\u6c70\u540e\u7684\u4eba\u5458\u751f\u547d\u5468\u671f", PromptProfile = "\u4f60\u8d1f\u8d23\u7ef4\u62a4 AI \u5458\u5de5\u540d\u518c\u3001\u5c97\u4f4d\u7f3a\u53e3\u548c\u6dd8\u6c70\u540e\u7684\u4eba\u5458\u72b6\u6001\uff0c\u5e2e CEO \u5224\u65ad\u7ec4\u7ec7\u8fd0\u884c\u662f\u5426\u5065\u5eb7\u3002" },
            new WorldCupEmployee { Id = "emp_ceo", Name = "CEO", Role = "CEO", Specialty = "\u6700\u7ec8\u9884\u6d4b\u3001\u4efb\u52a1\u9a8c\u6536\u3001\u590d\u76d8\u51b3\u7b56", PromptProfile = "\u4f60\u8d1f\u8d23\u7efc\u5408\u5458\u5de5\u610f\u89c1\u548c\u57fa\u7ebf\u7b56\u7565\uff0c\u8f93\u51fa\u6700\u7ec8\u514b\u5236\u5224\u65ad\u3002" }
        })
        {
            UpsertEmployee(connection, transaction, employee);
        }

        // 生成小组赛赛程（每组6场，12组共72场）
        var groupMatches = new List<WorldCupMatch>();
        var groups = teams.GroupBy(t => t.Item6).OrderBy(g => g.Key);
        int matchIndex = 1;
        foreach (var group in groups)
        {
            var groupTeams = group.ToList();
            var groupName = $"Group {group.Key}";
            // 每组4队，循环赛6场
            for (int i = 0; i < groupTeams.Count; i++)
            {
                for (int j = i + 1; j < groupTeams.Count; j++)
                {
                    var home = groupTeams[i];
                    var away = groupTeams[j];
                    var kickoff = new DateTime(2026, 6, 11, 12, 0, 0).AddHours(matchIndex * 3);
                    groupMatches.Add(new WorldCupMatch
                    {
                        Id = $"match_{matchIndex:D3}",
                        Stage = "group",
                        GroupName = groupName,
                        HomeObjectId = $"team_{home.Item1}",
                        AwayObjectId = $"team_{away.Item1}",
                        KickoffTime = kickoff.ToString("yyyy-MM-dd HH:mm:ss"),
                        Venue = $"Stadium {((matchIndex - 1) % 16) + 1}"
                    });
                    matchIndex++;
                }
            }
        }

        foreach (var match in groupMatches)
        {
            UpsertMatch(connection, transaction, match);
        }

        transaction.Commit();
        SeedDemoDataSnapshots();
    }
}

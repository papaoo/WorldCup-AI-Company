using System.Text.Json;
using System.Text.Json.Nodes;

namespace PiPiClaw.Team;

public sealed partial class WorldCupStore
{
    public WorldCupDataReadinessAuditResult AuditWorldCupDataReadiness(bool includeTestData = false)
    {
        var result = new WorldCupDataReadinessAuditResult();
        var snapshots = includeTestData ? GetDataSnapshots() : GetProductionDataSnapshots();
        var matches = includeTestData ? GetMatches() : GetProductionMatches();
        var teams = (includeTestData ? GetWatchObjects() : GetProductionWatchObjects())
            .Where(item => item.Type == "football_team")
            .ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
        var configuredSources = AutoCollectionService.LoadAutoCollectionConfig()
            .Sources
            .ToDictionary(source => source.SourceName, StringComparer.OrdinalIgnoreCase);

        foreach (var definition in BuildSourceDefinitions())
        {
            configuredSources.TryGetValue(definition.SourceName, out var configured);
            var sourceSnapshots = snapshots
                .Where(snapshot => string.Equals(snapshot.Source, definition.SourceName, StringComparison.OrdinalIgnoreCase))
                .ToList();
            var uniqueHashes = sourceSnapshots
                .Select(snapshot => snapshot.ContentHash)
                .Where(hash => !string.IsNullOrWhiteSpace(hash))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
            var missingTargetCount = sourceSnapshots.Count(snapshot =>
                string.IsNullOrWhiteSpace(snapshot.ObjectId) && string.IsNullOrWhiteSpace(snapshot.MatchId));

            var profile = definition;
            profile.Enabled = configured?.Enabled ?? sourceSnapshots.Count > 0;
            profile.Snapshots = sourceSnapshots.Count;
            profile.UniqueHashes = uniqueHashes;
            profile.MissingTargetCount = missingTargetCount;
            profile.LatestCapturedAt = sourceSnapshots
                .OrderByDescending(snapshot => snapshot.CapturedAt)
                .FirstOrDefault()
                ?.CapturedAt;
            profile.ReliabilityScore = CalculateReliabilityScore(profile);
            AddSourceQualityNotes(profile);
            result.RegisteredSources.Add(profile);
        }

        foreach (var match in matches.OrderBy(match => match.KickoffTime, StringComparer.OrdinalIgnoreCase))
        {
            teams.TryGetValue(match.HomeObjectId, out var home);
            teams.TryGetValue(match.AwayObjectId, out var away);
            var eligible = BaselinePredictionStrategy.CanPredict(match, home, away, out var reason);
            var item = new MatchPredictionEligibility
            {
                MatchId = match.Id,
                HomeObjectId = match.HomeObjectId,
                AwayObjectId = match.AwayObjectId,
                HomeDisplayName = home?.DisplayName ?? match.HomeObjectId,
                AwayDisplayName = away?.DisplayName ?? match.AwayObjectId,
                Eligible = eligible,
                Reason = eligible ? "resolved teams with non-demo metadata" : reason
            };
            result.MatchEligibility.Add(item);
        }

        result.TotalMatches = matches.Count;
        result.EligibleMatches = result.MatchEligibility.Count(item => item.Eligible);
        result.BlockedMatches = result.TotalMatches - result.EligibleMatches;
        result.DemoOrHarnessMatches = includeTestData ? matches.Count(IsDemoOrHarnessMatch) : 0;
        result.SourceHealthPassed = result.RegisteredSources.Any(source =>
            source.SourceName is "worldcup26_games" or "fixturedownload_schedule" or "openfootball_schedule"
            && source.Snapshots > 0
            && source.ReliabilityScore >= 0.55);
        result.PredictionReadinessPassed = result.EligibleMatches > 0 && result.BlockedMatches < result.TotalMatches;
        result.Passed = result.SourceHealthPassed && result.PredictionReadinessPassed;

        if (!result.SourceHealthPassed)
        {
            result.Notes.Add("No stable fixture source has enough loaded snapshots for readiness.");
        }
        if (result.BlockedMatches > 0)
        {
            result.Notes.Add($"{result.BlockedMatches} matches are blocked from prediction because teams are unresolved, demo-only, or harness data.");
        }
        if (result.DemoOrHarnessMatches > 0)
        {
            result.Notes.Add($"{result.DemoOrHarnessMatches} demo/harness matches are present; production views should filter them out.");
        }
        result.Notes.Add("LLM output should be used for explanation and review, not as the primary source of factual match data.");
        return result;
    }

    public WorldCupModelReviewResult SaveWorldCupModelReview(
        WorldCupDataReadinessAuditResult audit,
        WorldCupEmployee employee,
        string content,
        LlmCallRecord llmCall)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        var workflow = new WorkflowRunRecord
        {
            Id = $"workflow_worldcup_model_review_{DateTime.Now:yyyyMMddHHmmss}",
            WorkflowType = "worldcup_model_review",
            Status = llmCall.Status == "success" ? "completed" : "needs_review",
            StartedBy = "manual_model_review",
            StartedAt = now,
            CompletedAt = now,
            ErrorMessage = llmCall.Status == "success" ? null : llmCall.ErrorMessage,
            MetadataJson = new JsonObject
            {
                ["llm_call_id"] = llmCall.Id,
                ["eligible_matches"] = audit.EligibleMatches,
                ["blocked_matches"] = audit.BlockedMatches,
                ["source_health_passed"] = audit.SourceHealthPassed,
                ["prediction_readiness_passed"] = audit.PredictionReadinessPassed
            }.ToJsonString()
        };
        UpsertWorkflowRun(connection, transaction, workflow);
        UpsertLlmCall(connection, transaction, llmCall);

        var artifact = SaveArtifact(connection, transaction, new ArtifactRecord
        {
            Id = $"artifact_{workflow.Id}",
            Type = "markdown",
            Title = "世界杯模型审查报告",
            OwnerEmployeeId = employee.Id,
            WorkflowRunId = workflow.Id,
            FilePath = Path.Combine("artifacts", $"{workflow.Id}.md"),
            Summary = $"数据源健康：{audit.SourceHealthPassed}；可预测比赛：{audit.EligibleMatches}；拦截比赛：{audit.BlockedMatches}。",
            MetadataJson = new JsonObject
            {
                ["llm_call_id"] = llmCall.Id,
                ["eligible_matches"] = audit.EligibleMatches,
                ["blocked_matches"] = audit.BlockedMatches
            }.ToJsonString()
        }, content);

        var step = new WorkflowStepRecord
        {
            Id = $"step_{workflow.Id}_model_review",
            WorkflowRunId = workflow.Id,
            StepType = "worldcup_model_review",
            Status = workflow.Status,
            AssigneeEmployeeId = employee.Id,
            ArtifactId = artifact.Id,
            StartedAt = now,
            CompletedAt = now,
            InputJson = JsonSerializer.Serialize(audit, AppJsonContext.Default.WorldCupDataReadinessAuditResult),
            OutputJson = new JsonObject
            {
                ["content"] = content,
                ["llm_call_id"] = llmCall.Id,
                ["status"] = llmCall.Status
            }.ToJsonString(),
            ErrorMessage = workflow.ErrorMessage
        };
        UpsertWorkflowStep(connection, transaction, step);

        SaveSystemEventLog(connection, transaction, new WorldCupSystemEventLog
        {
            EventType = "worldcup_model_review_created",
            Category = "llm",
            Severity = llmCall.Status == "success" ? "info" : "warning",
            Source = "model_review",
            EmployeeId = employee.Id,
            WorkflowRunId = workflow.Id,
            LlmCallId = llmCall.Id,
            ArtifactId = artifact.Id,
            Title = "世界杯模型审查报告已生成",
            Message = $"{employee.Name} reviewed source readiness and prediction eligibility; eligible={audit.EligibleMatches}, blocked={audit.BlockedMatches}.",
            PayloadJson = new JsonObject
            {
                ["artifact_id"] = artifact.Id,
                ["llm_call_id"] = llmCall.Id,
                ["eligible_matches"] = audit.EligibleMatches,
                ["blocked_matches"] = audit.BlockedMatches,
                ["llm_status"] = llmCall.Status
            }.ToJsonString()
        });

        transaction.Commit();
        return new WorldCupModelReviewResult
        {
            WorkflowRun = workflow,
            Artifact = artifact,
            LlmCall = llmCall,
            Audit = audit,
            Content = content,
            Passed = llmCall.Status == "success",
            Notes = llmCall.Status == "success" ? [] : ["LLM review failed; fallback content was persisted for review."]
        };
    }

    private static List<DataSourceQualityProfile> BuildSourceDefinitions()
    {
        return
        [
            new DataSourceQualityProfile
            {
                SourceName = "fifa_official_reference",
                Provider = "fifa.com",
                AuthorityTier = "official_reference",
                StabilityTier = "web_page",
                LicenseNote = "Use as authoritative human-readable reference; automated reuse should respect FIFA website terms.",
                BestFor = ["official schedule reference", "stadium naming", "tournament dates"],
                NotFor = ["bulk automated scraping", "injury data", "lineups", "odds"],
                RequiresApiKey = false
            },
            new DataSourceQualityProfile
            {
                SourceName = "fifa_official_mens_ranking",
                Provider = "FIFA official FDCP",
                AuthorityTier = "official_data",
                StabilityTier = "high",
                LicenseNote = "Official FIFA ranking endpoint discovered from the public ranking page; store normalized rank snapshots and keep the raw payload for audit.",
                BestFor = ["current FIFA ranking", "ranking points", "previous rank comparison", "team strength baseline"],
                NotFor = ["injury data", "lineups", "tactical style", "market odds"],
                RequiresApiKey = false
            },
            new DataSourceQualityProfile
            {
                SourceName = "world_football_elo",
                Provider = "World Football Elo Ratings",
                AuthorityTier = "rating_model",
                StabilityTier = "high",
                LicenseNote = "Public no-key Elo rating table; use as a model-derived strength signal and retain raw rows for audit.",
                BestFor = ["team strength calibration", "cross-checking FIFA rank", "longer-horizon relative quality"],
                NotFor = ["official ranking truth", "injury data", "lineups", "betting odds"],
                RequiresApiKey = false
            },
            new DataSourceQualityProfile
            {
                SourceName = "international_results_recent_form",
                Provider = "martj42/international_results",
                AuthorityTier = "open_dataset",
                StabilityTier = "high",
                LicenseNote = "Versioned open CSV of international match results; aggregate only recent national-team form and keep the source URL.",
                BestFor = ["recent form", "goals for/against trend", "sample tournament context"],
                NotFor = ["live match updates", "confirmed squads", "injury data", "market odds"],
                RequiresApiKey = false
            },
            new DataSourceQualityProfile
            {
                SourceName = "worldcup26_games",
                Provider = "worldcup26.ir",
                AuthorityTier = "community_api",
                StabilityTier = "medium",
                LicenseNote = "No-key public endpoint; verify against official and secondary schedule sources before using as truth.",
                BestFor = ["fixture bootstrap", "teams", "stadiums", "scores when available"],
                NotFor = ["injury data", "lineups", "market odds"],
                RequiresApiKey = false
            },
            new DataSourceQualityProfile
            {
                SourceName = "worldcup26_teams",
                Provider = "worldcup26.ir",
                AuthorityTier = "community_api",
                StabilityTier = "medium",
                LicenseNote = "No-key public endpoint; team identities should be cross-checked.",
                BestFor = ["team bootstrap", "FIFA code mapping"],
                NotFor = ["current FIFA ranking", "squad quality"],
                RequiresApiKey = false
            },
            new DataSourceQualityProfile
            {
                SourceName = "openfootball_schedule",
                Provider = "openfootball/worldcup.json",
                AuthorityTier = "open_dataset",
                StabilityTier = "high",
                LicenseNote = "Open source dataset; good for cross-checking fixture shape.",
                BestFor = ["fixture cross-check", "version-controlled schedule snapshots"],
                NotFor = ["live updates", "injury data", "lineups"],
                RequiresApiKey = false
            },
            new DataSourceQualityProfile
            {
                SourceName = "fixturedownload_schedule",
                Provider = "FixtureDownload",
                AuthorityTier = "public_feed",
                StabilityTier = "high",
                LicenseNote = "Public JSON feed; use as secondary schedule cross-check.",
                BestFor = ["fixture cross-check", "calendar export", "kickoff time verification"],
                NotFor = ["injury data", "lineups", "team strength"],
                RequiresApiKey = false
            },
            new DataSourceQualityProfile
            {
                SourceName = "espn_scoreboard",
                Provider = "ESPN site API",
                AuthorityTier = "public_feed",
                StabilityTier = "medium",
                LicenseNote = "Undocumented public JSON used by ESPN pages; use as no-key scoreboard cross-check and retain raw payload for audit.",
                BestFor = ["live match updates", "scores", "match events", "team match statistics", "source links"],
                NotFor = ["confirmed injury database", "official lineups unless present in match payload", "bulk automated scraping"],
                RequiresApiKey = false
            },
            new DataSourceQualityProfile
            {
                SourceName = "espn_summary",
                Provider = "ESPN site API summary",
                AuthorityTier = "public_feed",
                StabilityTier = "medium",
                LicenseNote = "Undocumented public JSON used by ESPN match pages; fetch only near-term events and store normalized summaries plus raw source links.",
                BestFor = ["match events", "lineups when present", "player-level events", "recent form context", "source links"],
                NotFor = ["confirmed injury database", "official squad truth", "bulk automated scraping"],
                RequiresApiKey = false
            },
            new DataSourceQualityProfile
            {
                SourceName = "rss_soccer_news",
                Provider = "ESPN/Guardian RSS",
                AuthorityTier = "editorial_feed",
                StabilityTier = "medium",
                LicenseNote = "Store title, URL, timestamp and short excerpt only; link back to original articles.",
                BestFor = ["news discovery", "human/LLM triage candidates", "source links"],
                NotFor = ["confirmed injury database", "automatic team attribution", "prediction probability input without review"],
                RequiresApiKey = false
            },
            new DataSourceQualityProfile
            {
                SourceName = "football_data_worldcup",
                Provider = "football-data.org",
                AuthorityTier = "api_provider",
                StabilityTier = "high",
                LicenseNote = "Requires API token; free tier may be rate-limited.",
                BestFor = ["fixtures", "scores", "competition metadata"],
                NotFor = ["rich player injuries", "odds"],
                RequiresApiKey = true
            },
            new DataSourceQualityProfile
            {
                SourceName = "the_odds_api_worldcup",
                Provider = "The Odds API",
                AuthorityTier = "market_api",
                StabilityTier = "high",
                LicenseNote = "Requires API key; odds are high-value predictive signals and should be cost-controlled.",
                BestFor = ["market-implied probabilities", "model calibration", "line movement"],
                NotFor = ["official truth", "injury confirmation"],
                RequiresApiKey = true
            }
        ];
    }

    private static double CalculateReliabilityScore(DataSourceQualityProfile profile)
    {
        var score = profile.AuthorityTier switch
        {
            "official_reference" => 0.95,
            "official_data" => 0.93,
            "rating_model" => 0.82,
            "api_provider" => 0.86,
            "market_api" => 0.84,
            "open_dataset" => 0.78,
            "public_feed" => 0.72,
            "community_api" => 0.62,
            "editorial_feed" => 0.48,
            _ => 0.35
        };

        if (profile.Snapshots == 0 && profile.SourceName != "fifa_official_reference") score -= 0.22;
        if (profile.Snapshots > 0 && profile.UniqueHashes == 0) score -= 0.15;
        if (profile.Snapshots > 0)
        {
            var missingRatio = profile.MissingTargetCount / (double)profile.Snapshots;
            if (missingRatio > 0.8 && profile.AuthorityTier != "editorial_feed") score -= 0.12;
            if (missingRatio > 0.8 && profile.AuthorityTier == "editorial_feed") score -= 0.05;
        }
        if (profile.RequiresApiKey && profile.Snapshots == 0) score -= 0.08;
        return Math.Round(Math.Clamp(score, 0, 1), 3, MidpointRounding.AwayFromZero);
    }

    private static void AddSourceQualityNotes(DataSourceQualityProfile profile)
    {
        if (profile.Snapshots == 0 && profile.SourceName != "fifa_official_reference")
        {
            profile.Notes.Add("No local snapshots are loaded for this source.");
        }
        if (profile.SourceName == "rss_soccer_news")
        {
            profile.Notes.Add("RSS items must be entity-linked and reviewed before they become injury or lineup facts.");
        }
        if (profile.SourceName is "openfootball_schedule" or "fixturedownload_schedule")
        {
            profile.Notes.Add("Use this as a cross-check source; do not infer team strength from schedule payloads.");
        }
        if (profile.SourceName == "world_football_elo")
        {
            profile.Notes.Add("Elo is useful for strength calibration, but it is still a model signal rather than official truth.");
        }
        if (profile.SourceName == "international_results_recent_form")
        {
            profile.Notes.Add("Recent-form aggregates should be interpreted with opponent strength and competition context.");
        }
        if (profile.RequiresApiKey)
        {
            profile.Notes.Add("Optional paid/keyed upgrade source. Keep adapter pluggable and budget-controlled.");
        }
    }

    private static bool IsDemoOrHarnessMatch(WorldCupMatch match)
    {
        return match.Id.Contains("demo", StringComparison.OrdinalIgnoreCase)
            || match.Id.Contains("harness", StringComparison.OrdinalIgnoreCase)
            || match.GroupName.Contains("Demo", StringComparison.OrdinalIgnoreCase)
            || match.GroupName.Contains("Harness", StringComparison.OrdinalIgnoreCase)
            || match.Venue.Contains("Demo", StringComparison.OrdinalIgnoreCase)
            || match.Venue.Contains("Harness", StringComparison.OrdinalIgnoreCase);
    }
}

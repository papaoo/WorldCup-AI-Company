using System.Text.Json;
using System.Text.Json.Nodes;

namespace PiPiClaw.Team;

public sealed partial class WorldCupStore
{
    public TeamIntelligenceContextPack BuildTeamIntelligenceContextPack(string objectId, int maxEvidence = 12, bool includeTestData = false)
    {
        var team = GetWatchObjectById(objectId);
        var result = new TeamIntelligenceContextPack { ObjectId = objectId };
        if (team == null)
        {
            result.Notes.Add($"Team not found: {objectId}");
            return result;
        }

        result.TeamName = team.DisplayName;
        result.Symbol = team.Symbol;
        ReadTeamMetadata(team, result);
        var employeeId = GetPrimaryEmployeeIdForObject(objectId);
        if (!string.IsNullOrWhiteSpace(employeeId))
        {
            var employee = GetEmployeeById(employeeId);
            result.EmployeeId = employee?.Id;
            result.EmployeeName = employee?.Name;
        }

        var teams = (includeTestData ? GetWatchObjects() : GetProductionWatchObjects())
            .Where(item => item.Type == "football_team")
            .ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
        var matches = (includeTestData ? GetMatches() : GetProductionMatches())
            .Where(match => match.HomeObjectId == objectId || match.AwayObjectId == objectId)
            .OrderBy(match => match.KickoffTime, StringComparer.OrdinalIgnoreCase)
            .Take(6)
            .ToList();
        foreach (var match in matches)
        {
            teams.TryGetValue(match.HomeObjectId, out var home);
            teams.TryGetValue(match.AwayObjectId, out var away);
            var eligible = BaselinePredictionStrategy.CanPredict(match, home, away, out var reason);
            result.UpcomingMatches.Add(new MatchPredictionEligibility
            {
                MatchId = match.Id,
                HomeObjectId = match.HomeObjectId,
                AwayObjectId = match.AwayObjectId,
                HomeDisplayName = home?.DisplayName ?? match.HomeObjectId,
                AwayDisplayName = away?.DisplayName ?? match.AwayObjectId,
                Eligible = eligible,
                Reason = eligible ? $"scheduled at {match.KickoffTime}, venue {match.Venue}" : reason
            });
        }

        var evidence = new List<CompactEvidenceItem>();
        var snapshotsById = (includeTestData
                ? GetDataSnapshots(objectId: objectId)
                : GetProductionDataSnapshots(objectId: objectId))
            .Take(80)
            .ToDictionary(snapshot => snapshot.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var snapshot in snapshotsById.Values)
        {
            evidence.Add(BuildSnapshotEvidence(snapshot));
        }

        var staleSignalCount = 0;
        var signals = includeTestData
            ? GetIntelligenceSignals(objectId: objectId, limit: 120)
            : GetProductionIntelligenceSignals(objectId: objectId, limit: 120);
        foreach (var signal in signals)
        {
            if (!SignalEvidenceMatchesTeam(signal, team))
            {
                staleSignalCount++;
                continue;
            }
            snapshotsById.TryGetValue(signal.SourceSnapshotId, out var snapshot);
            evidence.Add(BuildSignalEvidence(signal, snapshot));
        }

        result.Evidence = evidence
            .GroupBy(item => EvidenceDedupeKey(item), StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(item => EvidenceKindPriority(item.Kind))
                .ThenByDescending(item => SourceReliability(item.Source))
                .ThenByDescending(item => item.Confidence)
                .ThenByDescending(item => item.CapturedAt, StringComparer.OrdinalIgnoreCase)
                .First())
            .OrderByDescending(item => EvidenceKindPriority(item.Kind))
            .ThenByDescending(item => SourceReliability(item.Source))
            .ThenByDescending(item => item.Confidence)
            .ThenByDescending(item => item.CapturedAt, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Clamp(maxEvidence, 4, 24))
            .ToList();

        result.SourceNotes = BuildContextSourceNotes(result.Evidence);
        result.Risks = BuildContextRisks(result);
        result.EstimatedTokens = LlmGateway.EstimateTokens(JsonSerializer.Serialize(result, AppJsonContext.Default.TeamIntelligenceContextPack));
        result.Passed = result.Evidence.Count > 0 && result.UpcomingMatches.Count > 0;
        if (result.Evidence.Count == 0) result.Notes.Add("No compact evidence is available for this team.");
        if (result.UpcomingMatches.Count == 0) result.Notes.Add("No upcoming or related matches are attached to this team.");
        if (staleSignalCount > 0) result.Notes.Add($"Filtered {staleSignalCount} stale RSS/news signals that did not explicitly mention {team.DisplayName}.");
        return result;
    }

    public TeamContextLlmReviewResult SaveTeamContextLlmReview(
        TeamIntelligenceContextPack context,
        WorldCupEmployee employee,
        string content,
        LlmCallRecord llmCall)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        var workflow = new WorkflowRunRecord
        {
            Id = $"workflow_{context.ObjectId}_context_review_{DateTime.Now:yyyyMMddHHmmss}",
            WorkflowType = "team_context_llm_review",
            Status = llmCall.Status == "success" ? "completed" : "needs_review",
            ObjectId = context.ObjectId,
            StartedBy = "manual_context_review",
            StartedAt = now,
            CompletedAt = now,
            ErrorMessage = llmCall.Status == "success" ? null : llmCall.ErrorMessage,
            MetadataJson = new JsonObject
            {
                ["llm_call_id"] = llmCall.Id,
                ["evidence_count"] = context.Evidence.Count,
                ["estimated_context_tokens"] = context.EstimatedTokens
            }.ToJsonString()
        };
        UpsertWorkflowRun(connection, transaction, workflow);
        UpsertLlmCall(connection, transaction, llmCall);

        var artifact = SaveArtifact(connection, transaction, new ArtifactRecord
        {
            Id = $"artifact_{workflow.Id}",
            Type = "markdown",
            Title = $"{context.TeamName} 球队上下文审查",
            OwnerEmployeeId = employee.Id,
            ObjectId = context.ObjectId,
            WorkflowRunId = workflow.Id,
            FilePath = Path.Combine("artifacts", $"{workflow.Id}.md"),
            Summary = $"{context.TeamName}: 基于 {context.Evidence.Count} 条压缩证据生成的上下文审查。",
            MetadataJson = new JsonObject
            {
                ["object_id"] = context.ObjectId,
                ["employee_id"] = employee.Id,
                ["llm_call_id"] = llmCall.Id,
                ["evidence_count"] = context.Evidence.Count,
                ["estimated_context_tokens"] = context.EstimatedTokens
            }.ToJsonString()
        }, content);

        var step = new WorkflowStepRecord
        {
            Id = $"step_{workflow.Id}_team_context_llm_review",
            WorkflowRunId = workflow.Id,
            StepType = "team_context_llm_review",
            Status = workflow.Status,
            AssigneeEmployeeId = employee.Id,
            StartedAt = now,
            CompletedAt = now,
            InputJson = JsonSerializer.Serialize(context, AppJsonContext.Default.TeamIntelligenceContextPack),
            OutputJson = new JsonObject
            {
                ["content"] = content,
                ["llm_call_id"] = llmCall.Id,
                ["status"] = llmCall.Status
            }.ToJsonString(),
            ArtifactId = artifact.Id,
            ErrorMessage = workflow.ErrorMessage
        };
        UpsertWorkflowStep(connection, transaction, step);
        SaveSystemEventLog(connection, transaction, new WorldCupSystemEventLog
        {
            EventType = "team_context_llm_review_created",
            Category = "llm",
            Severity = llmCall.Status == "success" ? "info" : "warning",
            Source = "team_context_review",
            EmployeeId = employee.Id,
            ObjectId = context.ObjectId,
            WorkflowRunId = workflow.Id,
            LlmCallId = llmCall.Id,
            ArtifactId = artifact.Id,
            Title = $"{context.TeamName} 球队上下文审查已生成",
            Message = $"{employee.Name} reviewed {context.Evidence.Count} compact evidence items; context tokens={context.EstimatedTokens}.",
            PayloadJson = new JsonObject
            {
                ["artifact_id"] = artifact.Id,
                ["llm_call_id"] = llmCall.Id,
                ["evidence_count"] = context.Evidence.Count,
                ["estimated_context_tokens"] = context.EstimatedTokens,
                ["llm_status"] = llmCall.Status
            }.ToJsonString()
        });

        transaction.Commit();
        return new TeamContextLlmReviewResult
        {
            ContextPack = context,
            WorkflowRun = workflow,
            Artifact = artifact,
            LlmCall = llmCall,
            Content = content,
            Passed = llmCall.Status == "success",
            Notes = llmCall.Status == "success" ? [] : ["LLM review failed; fallback content was persisted for review."]
        };
    }

    private static CompactEvidenceItem BuildSnapshotEvidence(DataSnapshotRecord snapshot)
    {
        var extracted = ExtractEvidenceText(snapshot.ContentJson);
        return new CompactEvidenceItem
        {
            Id = snapshot.Id,
            Source = snapshot.Source,
            Kind = snapshot.SnapshotType,
            Confidence = SourceReliability(snapshot.Source),
            CapturedAt = snapshot.CapturedAt,
            Summary = TruncateForContext(extracted.Text, 260),
            Url = extracted.Url
        };
    }

    private static CompactEvidenceItem BuildSignalEvidence(IntelligenceSignalRecord signal, DataSnapshotRecord? snapshot)
    {
        var extracted = ExtractEvidenceText(signal.EvidenceJson);
        return new CompactEvidenceItem
        {
            Id = signal.Id,
            Source = snapshot?.Source ?? extracted.Source ?? "intelligence_signal",
            Kind = signal.SignalType,
            Confidence = Math.Round(Math.Max(signal.Confidence, SourceReliability(snapshot?.Source ?? "")), 3),
            CapturedAt = signal.CreatedAt,
            Summary = TruncateForContext(string.IsNullOrWhiteSpace(extracted.Text) ? signal.Summary : extracted.Text, 280),
            Url = extracted.Url
        };
    }

    private static (string Text, string? Url, string? Source) ExtractEvidenceText(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var url = ReadProperty(root, "url");
            var source = ReadProperty(root, "source");
            if (root.TryGetProperty("excerpt", out var excerpt))
            {
                var usage = ReadProperty(root, "prediction_usage");
                var keywords = ReadStringArray(root, "matched_keywords");
                var prefix = usage == "requires_review_before_prediction_input" ? "需复核情报" : "";
                var keywordText = keywords.Count > 0 ? $"关键词：{string.Join("、", keywords.Take(5))}" : "";
                return (string.Join(" / ", new[] { prefix, keywordText, excerpt.ToString() }.Where(item => !string.IsNullOrWhiteSpace(item))), url, source);
            }
            if (root.TryGetProperty("title", out var title))
            {
                var description = ReadProperty(root, "description") ?? "";
                return ($"{title.GetString()} {description}".Trim(), url, source);
            }
            if (root.TryGetProperty("payload", out var payload))
            {
                return (payload.ToString(), url, source);
            }
            if (root.TryGetProperty("articles", out var articles) && articles.ValueKind == JsonValueKind.Array)
            {
                var snippets = articles.EnumerateArray()
                    .Take(3)
                    .Select(item => $"{ReadProperty(item, "title")} {ReadProperty(item, "description")}".Trim())
                    .Where(text => !string.IsNullOrWhiteSpace(text));
                return (string.Join(" | ", snippets), url, source);
            }
            return (root.ToString(), url, source);
        }
        catch
        {
            return (json, null, null);
        }
    }

    private static List<string> ReadStringArray(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return value.EnumerateArray()
            .Select(item => item.ToString())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToList();
    }

    private static string? ReadProperty(JsonElement item, string propertyName)
    {
        return item.TryGetProperty(propertyName, out var value) && value.ValueKind != JsonValueKind.Null
            ? value.ToString()
            : null;
    }

    private static void ReadTeamMetadata(WorldCupWatchObject team, TeamIntelligenceContextPack result)
    {
        try
        {
            using var doc = JsonDocument.Parse(team.MetadataJson);
            if (doc.RootElement.TryGetProperty("fifa_rank", out var rank) && rank.TryGetInt32(out var fifaRank))
            {
                result.FifaRank = fifaRank;
            }
            if (doc.RootElement.TryGetProperty("group", out var group))
            {
                result.Group = group.GetString();
            }
        }
        catch
        {
            result.Notes.Add("Team metadata JSON could not be parsed.");
        }
    }

    private static List<string> BuildContextSourceNotes(IReadOnlyList<CompactEvidenceItem> evidence)
    {
        var notes = new List<string>();
        if (evidence.Any(item => item.Source.Contains("rss", StringComparison.OrdinalIgnoreCase)))
        {
            notes.Add("RSS evidence is a discovery signal only; verify entity linking before treating it as an injury or lineup fact.");
        }
        if (evidence.Any(item => item.Source.Contains("openfootball", StringComparison.OrdinalIgnoreCase)
            || item.Source.Contains("fixturedownload", StringComparison.OrdinalIgnoreCase)))
        {
            notes.Add("Schedule cross-check evidence can validate fixtures, but cannot support team strength or injury claims.");
        }
        if (evidence.Any(item => item.Source.Contains("worldcup26", StringComparison.OrdinalIgnoreCase)))
        {
            notes.Add("worldcup26 public data is useful for bootstrap but should be cross-checked against official schedule references.");
        }
        return notes;
    }

    private static List<string> BuildContextRisks(TeamIntelligenceContextPack context)
    {
        var risks = new List<string>();
        if (context.UpcomingMatches.Any(match => !match.Eligible))
        {
            risks.Add("Some attached matches are not prediction-eligible because an opponent is unresolved or demo-only.");
        }
        if (context.Evidence.Any(item => item.Kind is "injury_risk" or "lineup_news" && item.Confidence < 0.75))
        {
            risks.Add("Actionable team news exists but confidence is below the threshold for direct prediction input.");
        }
        if (context.Evidence.All(item => item.Kind.Contains("fixture", StringComparison.OrdinalIgnoreCase)))
        {
            risks.Add("The current context is fixture-heavy and lacks independent form, injury, or squad evidence.");
        }
        return risks;
    }

    private static string EvidenceDedupeKey(CompactEvidenceItem item)
    {
        var normalized = item.Summary.ToLowerInvariant();
        normalized = new string(normalized.Where(ch => !char.IsPunctuation(ch)).ToArray());
        return $"{item.Source}|{item.Kind}|{TruncateForContext(normalized, 120)}";
    }

    private static double SourceReliability(string source)
    {
        if (source.Contains("fifa", StringComparison.OrdinalIgnoreCase)) return 0.95;
        if (source.Contains("openfootball", StringComparison.OrdinalIgnoreCase)) return 0.78;
        if (source.Contains("fixturedownload", StringComparison.OrdinalIgnoreCase)) return 0.72;
        if (source.Contains("worldcup26", StringComparison.OrdinalIgnoreCase)) return 0.62;
        if (source.Contains("rss", StringComparison.OrdinalIgnoreCase)) return 0.43;
        if (source.Contains("harness", StringComparison.OrdinalIgnoreCase) || source.Contains("demo", StringComparison.OrdinalIgnoreCase)) return 0.2;
        return 0.35;
    }

    private static int EvidenceKindPriority(string kind)
    {
        return kind switch
        {
            "injury_risk" => 100,
            "lineup_news" => 90,
            "news_update" => 60,
            "team_profile" => 45,
            "team_intel" => 42,
            "fixture_update" => 30,
            "fixture_intel" => 28,
            "fixture_crosscheck" => 25,
            "group_update" => 20,
            _ => 10
        };
    }

    private static string TruncateForContext(string text, int maxLength)
    {
        var cleaned = string.Join(" ", (text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return cleaned.Length <= maxLength ? cleaned : cleaned[..Math.Max(0, maxLength - 1)] + "…";
    }
}

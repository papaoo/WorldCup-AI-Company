using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Diagnostics;
using Microsoft.Data.Sqlite;

namespace PiPiClaw.Team;

public sealed partial class WorldCupStore
{
    public async Task<DataSourceImportResult> ImportDataSourceAsync(DataSourceImportRequest request, CancellationToken cancellationToken = default)
    {
        var batch = await DataSourceAdapter.LoadSnapshotsAsync(request, cancellationToken);
        var result = new DataSourceImportResult
        {
            SourceName = batch.Source,
            SourceKind = string.IsNullOrWhiteSpace(request.FilePath) ? "url" : "file",
            RawItems = batch.Items.Count
        };
        result.Notes.AddRange(batch.Notes);
        result.ImportResult = ImportDataSnapshots(batch);
        var syncedMatches = SyncFixtureStatusFromSnapshots(batch.Items, result.Notes);
        if (syncedMatches > 0)
        {
            result.Notes.Add($"Synced fixture status/score into match table for {syncedMatches} matches.");
        }
        var rankingMetadataUpdated = SyncFifaRankingMetadata(batch.Items);
        if (rankingMetadataUpdated > 0)
        {
            result.Notes.Add($"Updated FIFA ranking metadata for {rankingMetadataUpdated} teams.");
        }
        result.AffectedMatchIds = batch.Items
            .Select(item => item.MatchId)
            .Where(matchId => !string.IsNullOrWhiteSpace(matchId))
            .Select(matchId => matchId!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        foreach (var matchId in result.AffectedMatchIds)
        {
            try
            {
                CreateBaselinePrediction(matchId);
                result.BaselinePredictionsRefreshed++;
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("Cannot create", StringComparison.OrdinalIgnoreCase)
                && ex.Message.Contains("prediction", StringComparison.OrdinalIgnoreCase))
            {
                result.Notes.Add($"Baseline skipped for {matchId}: {ex.Message}");
            }
            catch (Exception ex)
            {
                result.Notes.Add($"Baseline refresh failed for {matchId}: {ex.Message}");
            }
        }
        var emptySuccess = batch.AllowEmptySuccess && result.RawItems == 0;
        result.Passed = emptySuccess || (result.ImportResult.Passed && result.RawItems > 0);
        if (result.RawItems == 0 && emptySuccess)
        {
            result.Notes.Add("Data source returned no new snapshots in degraded mode; existing cached snapshots remain active.");
        }
        else if (result.RawItems == 0)
        {
            result.Notes.Add("Data source contained no snapshot items.");
        }
        result.Notes.AddRange(result.ImportResult.Notes);
        AddSystemEventLog(new WorldCupSystemEventLog
        {
            EventType = "data_source_imported",
            Category = "data",
            Severity = result.Passed ? "info" : "warning",
            Source = result.SourceName,
            Title = "Data source import completed",
            Message = $"{result.SourceName}: raw={result.RawItems}, imported={result.ImportResult.Imported}, duplicates={result.ImportResult.SkippedDuplicates}.",
            PayloadJson = new JsonObject
            {
                ["source_kind"] = result.SourceKind,
                ["raw_items"] = result.RawItems,
                ["imported"] = result.ImportResult.Imported,
                ["skipped_duplicates"] = result.ImportResult.SkippedDuplicates,
                ["affected_match_ids"] = JsonSerializer.SerializeToNode(result.AffectedMatchIds, AppJsonContext.Default.ListString),
                ["baseline_predictions_refreshed"] = result.BaselinePredictionsRefreshed,
                ["passed"] = result.Passed
            }.ToJsonString()
        });
        return result;
    }

    private int SyncFifaRankingMetadata(IReadOnlyList<DataSnapshotCreateRequest> items)
    {
        var rankingItems = items
            .Where(item => string.Equals(item.SnapshotType, "team_ranking", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(item.ObjectId)
                && !string.IsNullOrWhiteSpace(item.ContentJson))
            .ToList();
        if (rankingItems.Count == 0)
        {
            return 0;
        }

        var updated = 0;
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        foreach (var item in rankingItems)
        {
            try
            {
                using var content = JsonDocument.Parse(item.ContentJson);
                var root = content.RootElement;
                if (!root.TryGetProperty("fifa_rank", out var rankElement) || !rankElement.TryGetInt32(out var rank))
                {
                    continue;
                }

                using var select = connection.CreateCommand();
                select.Transaction = transaction;
                select.CommandText = "SELECT metadata_json FROM watch_objects WHERE id = $id LIMIT 1";
                Add(select, "$id", item.ObjectId!);
                var existing = select.ExecuteScalar() as string;
                if (string.IsNullOrWhiteSpace(existing))
                {
                    continue;
                }

                var metadata = new JsonObject();
                try
                {
                    if (JsonNode.Parse(existing) is JsonObject parsed)
                    {
                        metadata = parsed;
                    }
                }
                catch
                {
                    metadata = new JsonObject();
                }

                metadata["fifa_rank"] = rank;
                metadata["fifa_ranking_source"] = "FIFA official FDCP";
                metadata["fifa_ranking_source_url"] = ReadJsonText(root, "source_url");
                metadata["fifa_ranking_schedule_id"] = ReadJsonText(root, "schedule_id");
                metadata["fifa_ranking_last_update"] = ReadJsonText(root, "last_update_date");
                metadata["fifa_ranking_next_update"] = ReadJsonText(root, "next_update_date");
                if (root.TryGetProperty("total_points", out var points) && points.ValueKind == JsonValueKind.Number)
                {
                    metadata["fifa_points"] = points.GetDecimal();
                }
                if (root.TryGetProperty("previous_rank", out var previousRank) && previousRank.ValueKind == JsonValueKind.Number)
                {
                    metadata["fifa_previous_rank"] = previousRank.GetInt32();
                }

                using var update = connection.CreateCommand();
                update.Transaction = transaction;
                update.CommandText = "UPDATE watch_objects SET metadata_json = $metadata_json, updated_at = $updated_at WHERE id = $id";
                Add(update, "$metadata_json", metadata.ToJsonString());
                Add(update, "$updated_at", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                Add(update, "$id", item.ObjectId!);
                updated += update.ExecuteNonQuery();
            }
            catch
            {
                // A malformed ranking item should not block the rest of the official ranking import.
            }
        }

        transaction.Commit();
        return updated;
    }

    private int SyncFixtureStatusFromSnapshots(IReadOnlyList<DataSnapshotCreateRequest> items, List<string> notes)
    {
        var fixtureItems = items
            .Where(item => !string.IsNullOrWhiteSpace(item.MatchId)
                && !string.IsNullOrWhiteSpace(item.ContentJson)
                && (item.SnapshotType.Contains("fixture", StringComparison.OrdinalIgnoreCase)
                    || item.SnapshotType.Contains("match_status", StringComparison.OrdinalIgnoreCase)
                    || item.SnapshotType.Contains("score", StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (fixtureItems.Count == 0)
        {
            return 0;
        }

        var updated = 0;
        var finishedMatches = new List<(string MatchId, string Stage)>();
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        foreach (var item in fixtureItems)
        {
            try
            {
                var existing = GetMatch(connection, item.MatchId!);
                if (existing == null)
                {
                    continue;
                }

                using var doc = JsonDocument.Parse(item.ContentJson);
                var root = doc.RootElement;
                var provider = ReadJsonText(root, "provider");
                var payload = root.TryGetProperty("payload", out var payloadElement) && payloadElement.ValueKind == JsonValueKind.Object
                    ? payloadElement
                    : root;
                var nextStatus = ResolveFixtureSnapshotStatus(provider, payload, existing.Status);
                var nextHomeScore = TryReadFixtureSnapshotScore(provider, payload, home: true);
                var nextAwayScore = TryReadFixtureSnapshotScore(provider, payload, home: false);
                if (nextStatus == "scheduled"
                    && nextHomeScore != null
                    && nextAwayScore != null
                    && HasFinishedScoreSignal(provider, payload))
                {
                    nextStatus = "finished";
                }

                if (!ShouldApplyMatchStatus(existing.Status, nextStatus))
                {
                    continue;
                }

                var next = new WorldCupMatch
                {
                    Id = existing.Id,
                    Stage = existing.Stage,
                    GroupName = existing.GroupName,
                    HomeObjectId = existing.HomeObjectId,
                    AwayObjectId = existing.AwayObjectId,
                    KickoffTime = existing.KickoffTime,
                    Venue = existing.Venue,
                    Status = nextStatus,
                    HomeScore = ShouldApplyFixtureScore(nextStatus, nextHomeScore, nextAwayScore) ? nextHomeScore : existing.HomeScore,
                    AwayScore = ShouldApplyFixtureScore(nextStatus, nextHomeScore, nextAwayScore) ? nextAwayScore : existing.AwayScore
                };

                if (MatchCoreEquals(existing, next))
                {
                    continue;
                }

                UpsertMatch(connection, transaction, next);
                var becameFinished = NormalizeFixtureStatus(existing.Status) != "finished" && next.Status == "finished";
                if (becameFinished)
                {
                    finishedMatches.Add((next.Id, next.Stage));
                }
                SaveSystemEventLog(connection, transaction, new WorldCupSystemEventLog
                {
                    EventType = "fixture_status_synced",
                    Category = "data",
                    Severity = "info",
                    Source = item.Source,
                    MatchId = item.MatchId,
                    Title = "Fixture status synced",
                    Message = $"{item.MatchId}: {existing.Status} -> {next.Status}, score {ScoreLabel(existing)} -> {ScoreLabel(next)}.",
                    PayloadJson = new JsonObject
                    {
                        ["source"] = item.Source,
                        ["provider"] = provider,
                        ["previous_status"] = existing.Status,
                        ["next_status"] = next.Status,
                        ["previous_score"] = ScoreLabel(existing),
                        ["next_score"] = ScoreLabel(next)
                    }.ToJsonString()
                });
                updated++;
            }
            catch
            {
                // Fixture status sync is best-effort; import notes and raw snapshots still preserve the source evidence.
            }
        }
        transaction.Commit();

        foreach (var finishedMatch in finishedMatches)
        {
            try
            {
                var review = CreateMatchReview(finishedMatch.MatchId);
                notes.Add($"Created post-match review for {finishedMatch.MatchId}: actual={review.ActualOutcome}, predicted={review.PredictedOutcome}, hit={review.Hit}.");
            }
            catch (Exception ex)
            {
                notes.Add($"Post-match review failed for {finishedMatch.MatchId}: {ex.Message}");
            }

            if (IsKnockoutStage(finishedMatch.Stage))
            {
                try
                {
                    var lifecycle = ApplyMatchLifecycle(finishedMatch.MatchId);
                    if (!lifecycle.Applied && lifecycle.Notes.Count > 0)
                    {
                        notes.AddRange(lifecycle.Notes.Select(note => $"{finishedMatch.MatchId}: {note}"));
                    }
                }
                catch (Exception ex)
                {
                    notes.Add($"Lifecycle update failed for {finishedMatch.MatchId}: {ex.Message}");
                }
            }
        }
        return updated;
    }

    private static string ResolveFixtureSnapshotStatus(string provider, JsonElement payload, string existingStatus)
    {
        var finished = ReadJsonText(payload, "finished");
        if (finished.Equals("TRUE", StringComparison.OrdinalIgnoreCase)
            || finished.Equals("true", StringComparison.OrdinalIgnoreCase)
            || finished.Equals("1", StringComparison.OrdinalIgnoreCase))
        {
            return "finished";
        }

        var status = ReadJsonText(payload, "status");
        if (!string.IsNullOrWhiteSpace(status))
        {
            return NormalizeFixtureStatus(status);
        }

        var elapsed = ReadJsonText(payload, "time_elapsed");
        if (!string.IsNullOrWhiteSpace(elapsed)
            && !elapsed.Equals("notstarted", StringComparison.OrdinalIgnoreCase)
            && !elapsed.Equals("null", StringComparison.OrdinalIgnoreCase))
        {
            return "running";
        }

        return NormalizeFixtureStatus(existingStatus) == "finished" ? "finished" : "scheduled";
    }

    private static int? TryReadFixtureSnapshotScore(string provider, JsonElement payload, bool home)
    {
        var key = home ? "home_score" : "away_score";
        var altKey = home ? "HomeTeamScore" : "AwayTeamScore";
        return TryReadScore(payload, key) ?? TryReadScore(payload, altKey);
    }

    private static bool HasFinishedScoreSignal(string provider, JsonElement payload)
    {
        if (provider.Equals("espn_scoreboard", StringComparison.OrdinalIgnoreCase))
        {
            return ReadJsonText(payload, "status").Equals("finished", StringComparison.OrdinalIgnoreCase);
        }

        if (provider.Equals("fixturedownload", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(ReadJsonText(payload, "Winner")))
        {
            return true;
        }

        var statusDetail = ReadJsonText(payload, "status_detail");
        return statusDetail.Equals("FT", StringComparison.OrdinalIgnoreCase)
            || statusDetail.Contains("Full Time", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveFixtureSnapshotStage(string provider, JsonElement payload, string existingStage)
    {
        var raw = ReadJsonText(payload, provider.Equals("fixturedownload", StringComparison.OrdinalIgnoreCase) ? "RoundNumber" : "type");
        if (string.IsNullOrWhiteSpace(raw)) return existingStage;
        return provider.Equals("fixturedownload", StringComparison.OrdinalIgnoreCase)
            ? NormalizeFixtureDownloadStage(raw)
            : NormalizeWorldCup26Stage(raw);
    }

    private static string ResolveFixtureSnapshotGroup(string provider, JsonElement payload, string existingGroup)
    {
        var group = ReadJsonText(payload, provider.Equals("fixturedownload", StringComparison.OrdinalIgnoreCase) ? "Group" : "group");
        return string.IsNullOrWhiteSpace(group) ? existingGroup : group;
    }

    private static string ResolveFixtureSnapshotKickoff(string provider, JsonElement payload, string existingKickoff)
    {
        if (provider.Equals("worldcup26", StringComparison.OrdinalIgnoreCase))
        {
            return existingKickoff;
        }
        var kickoff = ReadJsonText(payload, provider.Equals("fixturedownload", StringComparison.OrdinalIgnoreCase) ? "DateUtc" : "local_date");
        return string.IsNullOrWhiteSpace(kickoff) ? existingKickoff : kickoff;
    }

    private static string ResolveFixtureSnapshotVenue(string provider, JsonElement payload, string existingVenue)
    {
        var venue = ReadJsonText(payload, "Location");
        return string.IsNullOrWhiteSpace(venue) ? existingVenue : venue;
    }

    private static bool ShouldApplyMatchStatus(string existingStatus, string nextStatus)
    {
        var existing = NormalizeFixtureStatus(existingStatus);
        var next = NormalizeFixtureStatus(nextStatus);
        if (existing == "finished" && next != "finished") return false;
        if (existing == "running" && next == "scheduled") return false;
        if (next == "scheduled") return false;
        return next is "scheduled" or "running" or "finished" or "postponed";
    }

    private static bool ShouldApplyFixtureScore(string status, int? homeScore, int? awayScore)
    {
        var normalized = NormalizeFixtureStatus(status);
        return (normalized is "running" or "finished") && homeScore != null && awayScore != null;
    }

    private static string NormalizeFixtureStatus(string status)
    {
        return status.Trim().ToLowerInvariant() switch
        {
            "live" => "running",
            "in_progress" => "running",
            "playing" => "running",
            "1h" or "2h" or "ht" or "et" => "running",
            "completed" => "finished",
            "final" => "finished",
            "ft" => "finished",
            "notstarted" => "scheduled",
            _ => string.IsNullOrWhiteSpace(status) ? "scheduled" : status.Trim().ToLowerInvariant()
        };
    }

    private static bool MatchCoreEquals(WorldCupMatch left, WorldCupMatch right)
    {
        return left.Stage == right.Stage
            && left.GroupName == right.GroupName
            && left.KickoffTime == right.KickoffTime
            && left.Venue == right.Venue
            && left.Status == right.Status
            && left.HomeScore == right.HomeScore
            && left.AwayScore == right.AwayScore;
    }

    private static string ScoreLabel(WorldCupMatch match)
    {
        return match.HomeScore == null || match.AwayScore == null ? "-:-" : $"{match.HomeScore}:{match.AwayScore}";
    }

    public async Task<DataSourceAutoCollectionRunResult> RunAutoCollectionAsync(DataSourceAutoCollectionConfig config)
    {
        var result = new DataSourceAutoCollectionRunResult
        {
            StartedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            Running = true
        };
        AppContext.CurrentAutoCollectionRun = result;
        if (!config.Enabled)
        {
            result.CompletedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            result.Running = false;
            result.Passed = true;
            result.Notes.Add("Auto collection is disabled.");
            return result;
        }

        var sources = PrioritizeAutoCollectionSources(
            ExpandAutoCollectionSources(config.Sources.Where(source => source.Enabled)).ToList(),
            result.Notes);
        foreach (var source in sources)
        {
            result.SourcesChecked++;
            var sourceRun = new DataSourceAutoCollectionSourceRun
            {
                Id = source.Id,
                SourceName = string.IsNullOrWhiteSpace(source.SourceName) ? source.Id : source.SourceName,
                Provider = source.Provider ?? "",
                StartedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                TimeoutSeconds = Math.Clamp(source.TimeoutSeconds, 10, 300)
            };
            result.CurrentSourceId = sourceRun.Id;
            result.CurrentSourceName = sourceRun.SourceName;
            UpdateAutoCollectionElapsed(result);
            var stopwatch = Stopwatch.StartNew();
            try
            {
                using var sourceTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(sourceRun.TimeoutSeconds));
                var importResult = await ImportDataSourceAsync(new DataSourceImportRequest
                {
                    SourceName = string.IsNullOrWhiteSpace(source.SourceName) ? source.Id : source.SourceName,
                    Provider = source.Provider,
                    FilePath = source.FilePath,
                    Url = source.Url,
                    MatchId = source.MatchId,
                    ObjectId = source.ObjectId,
                    SnapshotType = source.SnapshotType,
                    Query = source.Query,
                    SportKey = source.SportKey,
                    CompetitionCode = source.CompetitionCode,
                    DateFrom = source.DateFrom,
                    DateTo = source.DateTo
                }, sourceTimeout.Token);
                importResult.ImportResult.ImportedItems.Clear();
                importResult.ImportResult.DuplicateItems.Clear();
                result.Results.Add(importResult);
                result.Imported += importResult.ImportResult.Imported;
                result.SkippedDuplicates += importResult.ImportResult.SkippedDuplicates;
                result.BaselinePredictionsRefreshed += importResult.BaselinePredictionsRefreshed;
                sourceRun.RawItems = importResult.RawItems;
                sourceRun.Imported = importResult.ImportResult.Imported;
                sourceRun.SkippedDuplicates = importResult.ImportResult.SkippedDuplicates;
                sourceRun.BaselinePredictionsRefreshed = importResult.BaselinePredictionsRefreshed;
                sourceRun.Passed = importResult.Passed;
                sourceRun.Notes = importResult.Notes.Take(8).ToList();
                if (importResult.Passed)
                {
                    result.SourcesSucceeded++;
                }
                else
                {
                    sourceRun.ErrorMessage = string.Join("; ", importResult.Notes.Take(3));
                    result.Notes.Add($"{source.Id}: {string.Join("; ", importResult.Notes)}");
                }
            }
            catch (OperationCanceledException ex)
            {
                sourceRun.Passed = false;
                sourceRun.ErrorMessage = $"source_timeout: exceeded {sourceRun.TimeoutSeconds} seconds.";
                sourceRun.Notes.Add(sourceRun.ErrorMessage);
                result.Notes.Add($"{source.Id}: {sourceRun.ErrorMessage} {ex.Message}");
            }
            catch (TimeoutException ex)
            {
                sourceRun.Passed = false;
                sourceRun.ErrorMessage = ex.Message;
                sourceRun.Notes.Add(ex.Message);
                result.Notes.Add($"{source.Id}: {ex.Message}");
            }
            catch (Exception ex)
            {
                sourceRun.Passed = false;
                sourceRun.ErrorMessage = ex.Message;
                sourceRun.Notes.Add(ex.Message);
                result.Notes.Add($"{source.Id}: {ex.Message}");
            }
            finally
            {
                stopwatch.Stop();
                sourceRun.ElapsedMs = stopwatch.ElapsedMilliseconds;
                sourceRun.CompletedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                result.SourceRuns.Add(sourceRun);
                UpdateAutoCollectionElapsed(result);
            }
        }

        result.CompletedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        result.Running = false;
        result.CurrentSourceId = "";
        result.CurrentSourceName = "";
        UpdateAutoCollectionElapsed(result);
        result.Passed = result.SourcesChecked > 0 && result.SourcesSucceeded == result.SourcesChecked;
        if (result.SourcesChecked == 0) result.Notes.Add("No enabled auto collection sources.");

        if (config.RunIntelligenceTriage)
        {
            result.IntelligenceTriage = RunIntelligenceTriage(config.TriageSnapshotLimit, includeTestData: false);
            if (!result.IntelligenceTriage.Passed)
            {
                result.Notes.AddRange(result.IntelligenceTriage.Notes.Select(note => $"triage: {note}"));
            }
        }

        var hasNewData = result.Imported > 0;
        if (config.TriggerEmployeeReports
            && result.IntelligenceTriage != null
            && (!config.TriggerReportsOnlyWhenNewData || hasNewData))
        {
            result.EmployeeReportTrigger = TriggerEmployeeReportsFromSignals(config.MaxReportTeams, includeTestData: false);
            if (!result.EmployeeReportTrigger.Passed)
            {
                result.Notes.AddRange(result.EmployeeReportTrigger.Notes.Select(note => $"employee_trigger: {note}"));
            }
        }
        else if (config.TriggerEmployeeReports && config.TriggerReportsOnlyWhenNewData && !hasNewData)
        {
            result.Notes.Add("employee_trigger: skipped because no new snapshots were imported.");
        }

        var changedSources = result.Results
            .Where(item => item.ImportResult.Imported > 0)
            .Select(item => item.SourceName)
            .ToList();
        result.SnapshotQuality = changedSources.Count == 0
            ? new DataSnapshotQualityResult
            {
                Passed = true,
                Notes = ["No new snapshots were imported; existing duplicate data was reused."]
            }
            : AuditDataSnapshotQualityForSources(changedSources, 250);
        result.IntelligenceQueueQuality = AuditIntelligenceQueueQuality(800);
        if (!result.SnapshotQuality.Passed)
        {
            result.Notes.AddRange(result.SnapshotQuality.Notes.Select(note => $"snapshot_quality: {note}"));
        }
        if (!result.IntelligenceQueueQuality.Passed)
        {
            result.Notes.AddRange(result.IntelligenceQueueQuality.Notes.Select(note => $"intelligence_queue: {note}"));
        }
        result.Passed = result.Passed
            && result.SnapshotQuality.Passed
            && result.IntelligenceQueueQuality.Passed;

        var workflow = new WorkflowRunRecord
        {
            Id = $"workflow_auto_collection_{DateTime.Now:yyyyMMddHHmmssfff}",
            WorkflowType = "auto_collection",
            Status = result.Passed ? "completed" : "needs_review",
            StartedBy = "auto_collection",
            StartedAt = result.StartedAt,
            CompletedAt = result.CompletedAt,
            ErrorMessage = result.Passed ? null : string.Join("; ", result.Notes.Take(6)),
            MetadataJson = new JsonObject
            {
                ["sources_checked"] = result.SourcesChecked,
                ["sources_succeeded"] = result.SourcesSucceeded,
                ["imported"] = result.Imported,
                ["skipped_duplicates"] = result.SkippedDuplicates,
                ["source_runs"] = JsonSerializer.SerializeToNode(result.SourceRuns, AppJsonContext.Default.ListDataSourceAutoCollectionSourceRun)
            }.ToJsonString()
        };
        using (var connection = OpenConnection())
        using (var transaction = connection.BeginTransaction())
        {
            UpsertWorkflowRun(connection, transaction, workflow);
            var step = new WorkflowStepRecord
            {
                Id = $"step_{workflow.Id}_auto_collection",
                WorkflowRunId = workflow.Id,
                StepType = "auto_collection",
                Status = workflow.Status,
                StartedAt = result.StartedAt,
                CompletedAt = result.CompletedAt,
                InputJson = new JsonObject
                {
                    ["enabled_sources"] = JsonSerializer.SerializeToNode(sources.Select(source => source.Id).ToList(), AppJsonContext.Default.ListString),
                    ["source_order_reason"] = result.Notes.FirstOrDefault(note => note.StartsWith("source_order:", StringComparison.OrdinalIgnoreCase)) ?? "",
                    ["run_intelligence_triage"] = config.RunIntelligenceTriage,
                    ["trigger_employee_reports"] = config.TriggerEmployeeReports,
                    ["trigger_reports_only_when_new_data"] = config.TriggerReportsOnlyWhenNewData,
                    ["triage_snapshot_limit"] = config.TriageSnapshotLimit,
                    ["max_report_teams"] = config.MaxReportTeams
                }.ToJsonString(),
                OutputJson = new JsonObject
                {
                    ["sources_checked"] = result.SourcesChecked,
                    ["sources_succeeded"] = result.SourcesSucceeded,
                    ["imported"] = result.Imported,
                    ["skipped_duplicates"] = result.SkippedDuplicates,
                    ["baseline_predictions_refreshed"] = result.BaselinePredictionsRefreshed,
                    ["triage_signals_created"] = result.IntelligenceTriage?.SignalsCreated ?? 0,
                    ["triage_duplicates_skipped"] = result.IntelligenceTriage?.DuplicatesSkipped ?? 0,
                    ["employee_reports_created"] = result.EmployeeReportTrigger?.ReportsCreated ?? 0,
                    ["snapshot_quality_passed"] = result.SnapshotQuality.Passed,
                    ["intelligence_queue_quality_passed"] = result.IntelligenceQueueQuality.Passed,
                    ["source_runs"] = JsonSerializer.SerializeToNode(result.SourceRuns, AppJsonContext.Default.ListDataSourceAutoCollectionSourceRun),
                    ["passed"] = result.Passed
                }.ToJsonString(),
                ErrorMessage = workflow.ErrorMessage
            };
            UpsertWorkflowStep(connection, transaction, step);
            transaction.Commit();
        }

        AddSystemEventLog(new WorldCupSystemEventLog
        {
            EventType = "auto_collection_run",
            Category = "data",
            Severity = result.Passed ? "info" : "warning",
            Source = "auto_collection",
            WorkflowRunId = workflow.Id,
            Title = "Auto collection run completed",
            Message = $"Checked {result.SourcesChecked} sources, succeeded {result.SourcesSucceeded}, imported {result.Imported}, signals {result.IntelligenceTriage?.SignalsCreated ?? 0}, reports {result.EmployeeReportTrigger?.ReportsCreated ?? 0}.",
            PayloadJson = new JsonObject
            {
                ["started_at"] = result.StartedAt,
                ["completed_at"] = result.CompletedAt,
                ["sources_checked"] = result.SourcesChecked,
                ["sources_succeeded"] = result.SourcesSucceeded,
                ["imported"] = result.Imported,
                ["skipped_duplicates"] = result.SkippedDuplicates,
                ["baseline_predictions_refreshed"] = result.BaselinePredictionsRefreshed,
                ["triage_signals_created"] = result.IntelligenceTriage?.SignalsCreated ?? 0,
                ["triage_duplicates_skipped"] = result.IntelligenceTriage?.DuplicatesSkipped ?? 0,
                ["triage_needs_ai_review"] = result.IntelligenceTriage?.NeedsAiReview ?? 0,
                ["employee_reports_created"] = result.EmployeeReportTrigger?.ReportsCreated ?? 0,
                ["employee_teams_triggered"] = result.EmployeeReportTrigger?.TeamsTriggered ?? 0,
                ["snapshot_quality_passed"] = result.SnapshotQuality.Passed,
                ["intelligence_queue_quality_passed"] = result.IntelligenceQueueQuality.Passed,
                ["pending_actionable_signals"] = result.IntelligenceQueueQuality.PendingActionableSignals,
                ["source_runs"] = JsonSerializer.SerializeToNode(result.SourceRuns, AppJsonContext.Default.ListDataSourceAutoCollectionSourceRun),
                ["passed"] = result.Passed,
                ["notes"] = JsonSerializer.SerializeToNode(result.Notes, AppJsonContext.Default.ListString)
            }.ToJsonString()
        });
        return result;
    }

    private static void UpdateAutoCollectionElapsed(DataSourceAutoCollectionRunResult result)
    {
        if (!DateTime.TryParse(result.StartedAt, out var startedAt))
        {
            return;
        }

        result.ElapsedSeconds = Math.Max(0, (int)Math.Round((DateTime.Now - startedAt).TotalSeconds));
    }

    private List<DataSourceAutoCollectionSource> PrioritizeAutoCollectionSources(
        List<DataSourceAutoCollectionSource> sources,
        List<string> notes)
    {
        if (sources.Count <= 1)
        {
            return sources;
        }

        var now = DateTimeOffset.Now;
        var matches = GetProductionMatches()
            .Select(match => new
            {
                Match = match,
                Status = NormalizeAutoCollectionMatchStatus(match.Status),
                Kickoff = ParseAutoCollectionKickoff(match.KickoffTime)
            })
            .Where(item => item.Kickoff != null)
            .ToList();

        var needsResultUpdate = matches.Any(item =>
            item.Status == "scheduled"
            && item.Kickoff!.Value <= now.AddMinutes(-90)
            && (item.Match.HomeScore == null || item.Match.AwayScore == null));
        var inLiveWindow = matches.Any(item =>
            item.Status == "running"
            || (item.Kickoff!.Value >= now.AddHours(-2) && item.Kickoff.Value <= now.AddHours(3)));
        var hasNearKickoff = matches.Any(item =>
            item.Status == "scheduled"
            && item.Kickoff!.Value >= now
            && item.Kickoff.Value <= now.AddHours(48));

        var ordered = sources
            .Select((source, index) => new { Source = source, Index = index, Rank = RankAutoCollectionSource(source, needsResultUpdate, inLiveWindow, hasNearKickoff) })
            .OrderBy(item => item.Rank)
            .ThenBy(item => item.Index)
            .Select(item => item.Source)
            .ToList();

        if (!ordered.Select(source => source.Id).SequenceEqual(sources.Select(source => source.Id), StringComparer.OrdinalIgnoreCase))
        {
            var reason = needsResultUpdate
                ? "存在已过开赛时间但比分未回填的比赛，优先同步赛程比分与赛后摘要源。"
                : inLiveWindow
                    ? "当前处于比赛开赛前后窗口，优先同步记分牌、事件和赛后摘要源。"
                    : hasNearKickoff
                        ? "48 小时内存在待赛比赛，优先同步本场新闻线索和赛程比分源。"
                        : "按常规稳定数据源顺序采集。";
            notes.Add($"source_order: {reason}");
        }

        return ordered;
    }

    private static int RankAutoCollectionSource(
        DataSourceAutoCollectionSource source,
        bool needsResultUpdate,
        bool inLiveWindow,
        bool hasNearKickoff)
    {
        var provider = source.Provider?.Trim().ToLowerInvariant() ?? "";
        var id = source.Id.Trim().ToLowerInvariant();
        var isScoreboard = provider == "espn_scoreboard";
        var isSummary = provider == "espn_summary";
        var isNews = provider == "rss_news";
        var isSchedule = provider.Contains("schedule", StringComparison.OrdinalIgnoreCase)
            || id.Contains("games", StringComparison.OrdinalIgnoreCase)
            || id.Contains("scoreboard", StringComparison.OrdinalIgnoreCase);

        if (needsResultUpdate)
        {
            if (isScoreboard) return 0;
            if (isSummary) return 1;
            if (isSchedule) return 2;
            if (isNews) return 3;
        }

        if (inLiveWindow)
        {
            if (isScoreboard) return 0;
            if (isSummary) return 1;
            if (isNews) return 2;
        }

        if (hasNearKickoff)
        {
            if (isNews && !string.IsNullOrWhiteSpace(source.MatchId)) return 0;
            if (isNews) return 1;
            if (isScoreboard) return 2;
            if (isSummary) return 3;
        }

        return 10;
    }

    private IEnumerable<DataSourceAutoCollectionSource> ExpandAutoCollectionSources(IEnumerable<DataSourceAutoCollectionSource> sources)
    {
        foreach (var source in sources)
        {
            yield return source;
        }

        var upcomingMatches = GetProductionMatches()
            .Where(match => NormalizeAutoCollectionMatchStatus(match.Status) == "scheduled")
            .Select(match => new { Match = match, Kickoff = ParseAutoCollectionKickoff(match.KickoffTime) })
            .Where(item => item.Kickoff != null)
            .OrderBy(item => item.Kickoff)
            .Take(4)
            .ToList();
        if (upcomingMatches.Count == 0) yield break;

        var teams = GetProductionWatchObjects()
            .Where(item => item.Type == "football_team")
            .ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);

        foreach (var item in upcomingMatches)
        {
            if (!teams.TryGetValue(item.Match.HomeObjectId, out var home)
                || !teams.TryGetValue(item.Match.AwayObjectId, out var away))
            {
                continue;
            }

            yield return new DataSourceAutoCollectionSource
            {
                Id = $"rss_match_watch_{item.Match.Id}",
                Enabled = true,
                SourceName = $"rss_match_watch_{item.Match.Id}",
                Provider = "rss_news",
                SnapshotType = "news_intel",
                MatchId = item.Match.Id,
                Query = BuildMatchNewsQuery(home, away),
                TimeoutSeconds = 12
            };
        }
    }

    private static DateTimeOffset? ParseAutoCollectionKickoff(string value)
    {
        return DateTimeOffset.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
    }

    private static string NormalizeAutoCollectionMatchStatus(string status)
    {
        return status.Trim().ToLowerInvariant() switch
        {
            "live" or "in_progress" or "playing" => "running",
            "completed" or "final" => "finished",
            _ => string.IsNullOrWhiteSpace(status) ? "scheduled" : status.Trim().ToLowerInvariant()
        };
    }

    private static string BuildMatchNewsQuery(WorldCupWatchObject home, WorldCupWatchObject away)
    {
        var names = new[]
        {
            home.DisplayName,
            home.Name,
            home.Symbol,
            away.DisplayName,
            away.Name,
            away.Symbol
        }
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        return $"{string.Join(" ", names)} injury lineup squad roster";
    }

    public async Task<DataSourceProviderHarnessResult> RunDataSourceProviderHarnessAsync()
    {
        var result = new DataSourceProviderHarnessResult();
        result.GnewsGuarded = await ProviderReturnsNeedsApiKeyAsync(new DataSourceImportRequest
        {
            SourceName = "provider_harness_gnews",
            Provider = "gnews",
            Query = "FIFA World Cup injury lineup",
            MatchId = "match_arg_jpn",
            ObjectId = "team_arg"
        });
        result.OddsGuarded = await ProviderReturnsNeedsApiKeyAsync(new DataSourceImportRequest
        {
            SourceName = "provider_harness_odds",
            Provider = "the_odds_api",
            SportKey = "soccer_fifa_world_cup",
            MatchId = "match_arg_jpn"
        });
        result.FootballDataGuarded = await ProviderReturnsNeedsApiKeyAsync(new DataSourceImportRequest
        {
            SourceName = "provider_harness_football_data",
            Provider = "football_data",
            CompetitionCode = "WC",
            MatchId = "match_arg_jpn"
        });
        result.Worldcup26Loaded = (await DataSourceAdapter.LoadSnapshotsAsync(new DataSourceImportRequest
        {
            SourceName = "provider_harness_worldcup26",
            Provider = "worldcup26",
            Query = "teams"
        })).Items.Count >= 40;
        result.FifaRankingLoaded = (await DataSourceAdapter.LoadSnapshotsAsync(new DataSourceImportRequest
        {
            SourceName = "provider_harness_fifa_ranking",
            Provider = "fifa_ranking"
        })).Items.Count >= 200;
        result.WorldFootballEloLoaded = (await DataSourceAdapter.LoadSnapshotsAsync(new DataSourceImportRequest
        {
            SourceName = "provider_harness_world_football_elo",
            Provider = "world_football_elo"
        })).Items.Count >= 40;
        result.InternationalResultsLoaded = (await DataSourceAdapter.LoadSnapshotsAsync(new DataSourceImportRequest
        {
            SourceName = "provider_harness_international_results",
            Provider = "international_results"
        })).Items.Count >= 40;
        result.OpenfootballLoaded = (await DataSourceAdapter.LoadSnapshotsAsync(new DataSourceImportRequest
        {
            SourceName = "provider_harness_openfootball",
            Provider = "openfootball_schedule"
        })).Items.Count >= 100;
        result.FixtureDownloadLoaded = (await DataSourceAdapter.LoadSnapshotsAsync(new DataSourceImportRequest
        {
            SourceName = "provider_harness_fixturedownload",
            Provider = "fixturedownload_schedule"
        })).Items.Count >= 100;
        result.EspnScoreboardLoaded = (await DataSourceAdapter.LoadSnapshotsAsync(new DataSourceImportRequest
        {
            SourceName = "provider_harness_espn_scoreboard",
            Provider = "espn_scoreboard",
            Query = "1:2"
        })).Items.Count >= 1;
        result.EspnSummaryLoaded = (await DataSourceAdapter.LoadSnapshotsAsync(new DataSourceImportRequest
        {
            SourceName = "provider_harness_espn_summary",
            Provider = "espn_summary",
            Query = "1:2"
        })).Items.Count >= 1;

        if (!result.GnewsGuarded) result.Notes.Add("GNews provider did not guard missing API key.");
        if (!result.OddsGuarded) result.Notes.Add("The Odds API provider did not guard missing API key.");
        if (!result.FootballDataGuarded) result.Notes.Add("football-data.org provider did not guard missing API key.");
        if (!result.Worldcup26Loaded) result.Notes.Add("worldcup26 no-key provider did not load enough teams.");
        if (!result.FifaRankingLoaded) result.Notes.Add("FIFA official ranking provider did not load enough teams.");
        if (!result.WorldFootballEloLoaded) result.Notes.Add("World Football Elo no-key provider did not load enough team ratings.");
        if (!result.InternationalResultsLoaded) result.Notes.Add("International results no-key provider did not load enough recent form snapshots.");
        if (!result.OpenfootballLoaded) result.Notes.Add("openfootball no-key provider did not load enough fixtures.");
        if (!result.FixtureDownloadLoaded) result.Notes.Add("FixtureDownload no-key provider did not load enough fixtures.");
        if (!result.EspnScoreboardLoaded) result.Notes.Add("ESPN scoreboard no-key provider did not load recent fixture status snapshots.");
        if (!result.EspnSummaryLoaded) result.Notes.Add("ESPN summary no-key provider did not load recent match summary snapshots.");
        result.Passed = result.GnewsGuarded
            && result.OddsGuarded
            && result.FootballDataGuarded
            && result.Worldcup26Loaded
            && result.FifaRankingLoaded
            && result.WorldFootballEloLoaded
            && result.InternationalResultsLoaded
            && result.OpenfootballLoaded
            && result.FixtureDownloadLoaded
            && result.EspnScoreboardLoaded
            && result.EspnSummaryLoaded;
        return result;
    }

    private static async Task<bool> ProviderReturnsNeedsApiKeyAsync(DataSourceImportRequest request)
    {
        try
        {
            await DataSourceAdapter.LoadSnapshotsAsync(request);
            return false;
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("needs_api_key", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
    }

    public async Task<DataSourceImportResult> RunDataSourceImportHarnessAsync()
    {
        SeedDemoWorldCupCompany();
        var marker = $"adapter_harness_{Guid.NewGuid():N}";
        var path = Path.Combine(Path.GetTempPath(), $"worldcup-data-source-{Guid.NewGuid():N}.json");
        var json = $$"""
            {
              "source": "adapter_harness_file",
              "items": [
                {
                  "snapshot_type": "team_intel",
                  "object_id": "team_arg",
                  "match_id": "match_arg_jpn",
                  "content": {
                    "adapter_marker": "{{marker}}",
                    "form_note": "Adapter harness says Argentina pressure signal is positive.",
                    "confidence": "high"
                  }
                },
                {
                  "snapshot_type": "match_intel",
                  "match_id": "match_arg_jpn",
                  "content": {
                    "adapter_marker": "{{marker}}",
                    "venue_note": "Adapter harness match-level note."
                  }
                }
              ]
            }
            """;
        await File.WriteAllTextAsync(path, json);

        try
        {
            var result = await ImportDataSourceAsync(new DataSourceImportRequest
            {
                SourceName = "adapter_harness",
                FilePath = path
            });
            var recalled = GetDataSnapshots("match_arg_jpn")
                .Count(snapshot => snapshot.ContentJson.Contains(marker, StringComparison.Ordinal));
            var context = BuildDataSnapshotContext("match_arg_jpn", "team_arg");
            if (recalled < 2) result.Notes.Add($"Expected at least 2 adapter snapshots, got {recalled}.");
            if (!context.Contains(marker, StringComparison.Ordinal)) result.Notes.Add("Adapter-imported evidence was not recalled into context.");
            result.Passed = result.Passed && recalled >= 2 && context.Contains(marker, StringComparison.Ordinal);
            return result;
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    public async Task<WorldCupPublicDataBootstrapResult> BootstrapPublicWorldCupDataAsync()
    {
        var result = new WorldCupPublicDataBootstrapResult
        {
            StartedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
        };

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
        var teamsDoc = JsonDocument.Parse(await client.GetStringAsync("https://worldcup26.ir/get/teams"));
        result.SourcesChecked++;
        var gamesDoc = JsonDocument.Parse(await client.GetStringAsync("https://worldcup26.ir/get/games"));
        result.SourcesChecked++;
        var groupsDoc = JsonDocument.Parse(await client.GetStringAsync("https://worldcup26.ir/get/groups"));
        result.SourcesChecked++;
        var stadiumsDoc = JsonDocument.Parse(await client.GetStringAsync("https://worldcup26.ir/get/stadiums"));
        result.SourcesChecked++;
        var fixtureDownloadDoc = JsonDocument.Parse(await client.GetStringAsync("https://fixturedownload.com/feed/json/fifa-world-cup-2026"));
        result.SourcesChecked++;

        var teamIdByWorldCup26Id = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var teamIdByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var stadiumNameById = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var snapshots = new DataSnapshotBatchImportRequest { Source = "worldcup26_bootstrap" };
        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        using (var connection = OpenConnection())
        using (var transaction = connection.BeginTransaction())
        {
            if (teamsDoc.RootElement.TryGetProperty("teams", out var teams) && teams.ValueKind == JsonValueKind.Array)
            {
                foreach (var team in teams.EnumerateArray())
                {
                    var sourceId = ReadJsonText(team, "id");
                    var fifaCode = ReadJsonText(team, "fifa_code");
                    var nameEn = ReadJsonText(team, "name_en");
                    if (string.IsNullOrWhiteSpace(sourceId) || string.IsNullOrWhiteSpace(fifaCode) || string.IsNullOrWhiteSpace(nameEn))
                    {
                        result.Notes.Add("Skipped a worldcup26 team with missing id/code/name.");
                        continue;
                    }

                    var objectId = $"team_{fifaCode.ToLowerInvariant()}";
                    teamIdByWorldCup26Id[sourceId] = objectId;
                    teamIdByName[nameEn] = objectId;
                    UpsertWatchObject(connection, transaction, new WorldCupWatchObject
                    {
                        Id = objectId,
                        Type = "football_team",
                        Symbol = fifaCode,
                        Name = nameEn,
                        DisplayName = nameEn,
                        Status = "active",
                        MetadataJson = new JsonObject
                        {
                            ["source"] = "worldcup26",
                            ["source_team_id"] = sourceId,
                            ["fifa_code"] = fifaCode,
                            ["iso2"] = ReadJsonText(team, "iso2"),
                            ["group"] = ReadJsonText(team, "groups"),
                            ["flag"] = ReadJsonText(team, "flag"),
                            ["fifa_rank"] = TryInferExistingRank(objectId)
                        }.ToJsonString(),
                        CreatedAt = now,
                        UpdatedAt = now
                    });
                    result.TeamsUpserted++;

                    var employeeId = $"emp_{fifaCode.ToLowerInvariant()}";
                    UpsertEmployee(connection, transaction, new WorldCupEmployee
                    {
                        Id = employeeId,
                        Name = $"{nameEn} Team Researcher",
                        Role = "team_researcher",
                        Specialty = $"{nameEn} team form, squad, tactics, news and risk monitoring",
                        Status = "active",
                        PromptProfile = $"You are the dedicated AI researcher for {nameEn}. Distinguish facts, news, inference and uncertainty. Cite snapshot ids and give the CEO actionable signals.",
                        ContactsJson = """["emp_data","emp_risk","emp_ceo"]""",
                        CreatedAt = now,
                        UpdatedAt = now
                    });
                    result.EmployeesUpserted++;

                    UpsertAssignment(connection, transaction, new EmployeeAssignment
                    {
                        Id = $"assign_{fifaCode.ToLowerInvariant()}",
                        EmployeeId = employeeId,
                        ObjectId = objectId,
                        AssignmentRole = "primary_researcher",
                        Status = "active",
                        StartedAt = now,
                        MetadataJson = $$"""{"source":"worldcup26_bootstrap","source_team_id":"{{EscapeJson(sourceId)}}"}"""
                    });
                    result.AssignmentsUpserted++;

                    snapshots.Items.Add(new DataSnapshotCreateRequest
                    {
                        Source = "worldcup26_bootstrap",
                        SnapshotType = "team_profile",
                        ObjectId = objectId,
                        ContentJson = BuildProviderPayload("worldcup26", "teams", team)
                    });
                }
            }

            if (stadiumsDoc.RootElement.TryGetProperty("stadiums", out var stadiums) && stadiums.ValueKind == JsonValueKind.Array)
            {
                foreach (var stadium in stadiums.EnumerateArray())
                {
                    var id = ReadJsonText(stadium, "id");
                    if (string.IsNullOrWhiteSpace(id)) continue;
                    var fifaName = ReadJsonText(stadium, "fifa_name");
                    var name = string.IsNullOrWhiteSpace(fifaName) ? ReadJsonText(stadium, "name_en") : fifaName;
                    var city = ReadJsonText(stadium, "city_en");
                    stadiumNameById[id] = string.IsNullOrWhiteSpace(city) ? name : $"{name}, {city}";
                    snapshots.Items.Add(new DataSnapshotCreateRequest
                    {
                        Source = "worldcup26_bootstrap",
                        SnapshotType = "stadium_profile",
                        ObjectId = $"stadium_{id}",
                        ContentJson = BuildProviderPayload("worldcup26", "stadiums", stadium)
                    });
                }
            }

            if (groupsDoc.RootElement.TryGetProperty("groups", out var groups) && groups.ValueKind == JsonValueKind.Array)
            {
                foreach (var group in groups.EnumerateArray())
                {
                    var groupName = ReadJsonText(group, "name");
                    snapshots.Items.Add(new DataSnapshotCreateRequest
                    {
                        Source = "worldcup26_bootstrap",
                        SnapshotType = "group_standing",
                        ObjectId = string.IsNullOrWhiteSpace(groupName) ? null : $"group_{groupName.ToLowerInvariant()}",
                        ContentJson = BuildProviderPayload("worldcup26", "groups", group)
                    });
                }
            }

            if (gamesDoc.RootElement.TryGetProperty("games", out var games) && games.ValueKind == JsonValueKind.Array)
            {
                foreach (var game in games.EnumerateArray())
                {
                    var gameId = ReadJsonText(game, "id");
                    var homeSourceId = ReadJsonText(game, "home_team_id");
                    var awaySourceId = ReadJsonText(game, "away_team_id");
                    if (string.IsNullOrWhiteSpace(gameId)
                        || !teamIdByWorldCup26Id.TryGetValue(homeSourceId, out var homeObjectId)
                        || !teamIdByWorldCup26Id.TryGetValue(awaySourceId, out var awayObjectId))
                    {
                        result.Notes.Add($"Skipped worldcup26 game {gameId} because a team mapping is missing.");
                        continue;
                    }

                    var status = ReadJsonText(game, "finished").Equals("TRUE", StringComparison.OrdinalIgnoreCase)
                        ? "finished"
                        : "scheduled";
                    var stadiumId = ReadJsonText(game, "stadium_id");
                    UpsertMatch(connection, transaction, new WorldCupMatch
                    {
                        Id = $"match_wc26_{gameId}",
                        Stage = NormalizeWorldCup26Stage(ReadJsonText(game, "type")),
                        GroupName = ReadJsonText(game, "group"),
                        HomeObjectId = homeObjectId,
                        AwayObjectId = awayObjectId,
                        KickoffTime = ReadJsonText(game, "local_date"),
                        Venue = stadiumNameById.TryGetValue(stadiumId, out var venue) ? venue : $"stadium_{stadiumId}",
                        Status = status,
                        HomeScore = TryReadScore(game, "home_score"),
                        AwayScore = TryReadScore(game, "away_score")
                    });
                    result.MatchesUpserted++;

                    snapshots.Items.Add(new DataSnapshotCreateRequest
                    {
                        Source = "worldcup26_bootstrap",
                        SnapshotType = "fixture_intel",
                        MatchId = $"match_wc26_{gameId}",
                        ContentJson = BuildProviderPayload("worldcup26", "games", game)
                    });
                }
            }

            UpsertWatchObject(connection, transaction, new WorldCupWatchObject
            {
                Id = "slot_tba",
                Type = "tournament_slot",
                Symbol = "TBA",
                Name = "To be announced",
                DisplayName = "To be announced",
                Status = "pending",
                MetadataJson = """{"source":"fixturedownload","placeholder":true}""",
                CreatedAt = now,
                UpdatedAt = now
            });

            var fixtureDownloadMatches = 0;
            if (fixtureDownloadDoc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var match in fixtureDownloadDoc.RootElement.EnumerateArray())
                {
                    var matchNumber = ReadJsonText(match, "MatchNumber");
                    if (string.IsNullOrWhiteSpace(matchNumber)) continue;
                    var homeName = ReadJsonText(match, "HomeTeam");
                    var awayName = ReadJsonText(match, "AwayTeam");
                    var homeObjectId = ResolveFixtureTeamObjectId(connection, transaction, teamIdByName, homeName, ReadJsonText(match, "Group"), now, result);
                    var awayObjectId = ResolveFixtureTeamObjectId(connection, transaction, teamIdByName, awayName, ReadJsonText(match, "Group"), now, result);
                    UpsertMatch(connection, transaction, new WorldCupMatch
                    {
                        Id = $"match_wc26_{matchNumber}",
                        Stage = NormalizeFixtureDownloadStage(ReadJsonText(match, "RoundNumber")),
                        GroupName = ReadJsonText(match, "Group") ?? "",
                        HomeObjectId = homeObjectId,
                        AwayObjectId = awayObjectId,
                        KickoffTime = ReadJsonText(match, "DateUtc"),
                        Venue = ReadJsonText(match, "Location"),
                        Status = "scheduled",
                        HomeScore = TryReadScore(match, "HomeTeamScore"),
                        AwayScore = TryReadScore(match, "AwayTeamScore")
                    });
                    fixtureDownloadMatches++;

                    snapshots.Items.Add(new DataSnapshotCreateRequest
                    {
                        Source = "fixturedownload_bootstrap",
                        SnapshotType = "fixture_crosscheck",
                        MatchId = $"match_wc26_{matchNumber}",
                        ContentJson = BuildProviderPayload("fixturedownload", "fifa-world-cup-2026", match)
                    });
                }
            }
            result.MatchesUpserted = Math.Max(result.MatchesUpserted, fixtureDownloadMatches);

            foreach (var employee in new[]
            {
                new WorldCupEmployee { Id = "emp_data", Name = "Data Analyst", Role = "data_analyst", Specialty = "baseline probability, historical data and calibration", PromptProfile = "You provide explainable baseline probabilities and avoid emotional claims.", Status = "active" },
                new WorldCupEmployee { Id = "emp_risk", Name = "Risk Officer", Role = "risk_officer", Specialty = "upset risk, injury risk and model blind spots", PromptProfile = "You challenge prediction uncertainty, weak evidence and upset exposure.", Status = "active" },
                new WorldCupEmployee { Id = "emp_hr", Name = "HR", Role = "hr", Specialty = "employee roster, role gaps and team lifecycle staffing", PromptProfile = "You maintain the AI employee roster, role gaps and staffing state after team elimination, helping CEO audit company health.", Status = "active" },
                new WorldCupEmployee { Id = "emp_ceo", Name = "CEO", Role = "ceo", Specialty = "final synthesis, decision review and workflow acceptance", PromptProfile = "You synthesize employee reports and baseline strategy into a cautious final judgement.", Status = "active" }
            })
            {
                employee.CreatedAt = now;
                employee.UpdatedAt = now;
                UpsertEmployee(connection, transaction, employee);
            }

            transaction.Commit();
        }

        var importResult = ImportDataSnapshots(snapshots);
        result.SnapshotsImported = importResult.Imported;
        result.SnapshotDuplicates = importResult.SkippedDuplicates;
        result.Notes.AddRange(importResult.Notes);
        result.CompletedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        result.Passed = result.TeamsUpserted >= 40
            && result.MatchesUpserted >= 100
            && importResult.Passed
            && result.AssignmentsUpserted == result.TeamsUpserted;
        if (result.TeamsUpserted < 40) result.Notes.Add($"Expected at least 40 teams, got {result.TeamsUpserted}.");
        if (result.MatchesUpserted < 100) result.Notes.Add($"Expected at least 100 matches, got {result.MatchesUpserted}.");
        if (result.AssignmentsUpserted != result.TeamsUpserted) result.Notes.Add("Not every team received a primary researcher assignment.");
        return result;
    }
}

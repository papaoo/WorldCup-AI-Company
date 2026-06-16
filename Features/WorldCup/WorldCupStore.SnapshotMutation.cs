using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace PiPiClaw.Team;

public sealed partial class WorldCupStore
{
    public DataSnapshotRecord AddDataSnapshot(DataSnapshotCreateRequest request)
    {
        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        var content = string.IsNullOrWhiteSpace(request.ContentJson) ? "{}" : request.ContentJson.Trim();
        var contentHash = Sha256(content);
        using var connection = OpenConnection();
        var duplicate = FindDuplicateDataSnapshot(
            connection,
            null,
            string.IsNullOrWhiteSpace(request.Source) ? "manual_demo" : request.Source,
            request.MatchId,
            request.ObjectId,
            string.IsNullOrWhiteSpace(request.SnapshotType) ? "team_intel" : request.SnapshotType,
            contentHash);
        if (duplicate != null)
        {
            return duplicate;
        }

        var snapshot = new DataSnapshotRecord
        {
            Id = $"snapshot_{Guid.NewGuid():N}",
            Source = string.IsNullOrWhiteSpace(request.Source) ? "manual_demo" : request.Source,
            SnapshotType = string.IsNullOrWhiteSpace(request.SnapshotType) ? "team_intel" : request.SnapshotType,
            ObjectId = request.ObjectId,
            MatchId = request.MatchId,
            ContentJson = content,
            ContentHash = contentHash,
            CapturedAt = now
        };
        SaveDataSnapshot(connection, null, snapshot);
        SaveSystemEventLog(connection, null, new WorldCupSystemEventLog
        {
            EventType = "snapshot_added",
            Category = "data",
            Source = snapshot.Source,
            ObjectId = snapshot.ObjectId,
            MatchId = snapshot.MatchId,
            SnapshotId = snapshot.Id,
            Title = "Data snapshot added",
            Message = $"{snapshot.Source}/{snapshot.SnapshotType} added.",
            PayloadJson = new JsonObject
            {
                ["snapshot_type"] = snapshot.SnapshotType,
                ["content_hash"] = snapshot.ContentHash,
                ["content_json"] = snapshot.ContentJson
            }.ToJsonString()
        });
        return snapshot;
    }

    public DataSnapshotBatchImportResult ImportDataSnapshots(DataSnapshotBatchImportRequest request)
    {
        var result = new DataSnapshotBatchImportResult
        {
            Requested = request.Items.Count
        };
        if (request.Items.Count == 0)
        {
            result.Notes.Add("No snapshots were provided for import.");
            return result;
        }

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        foreach (var item in request.Items)
        {
            if (string.IsNullOrWhiteSpace(item.ContentJson))
            {
                result.Notes.Add($"Skipped empty snapshot for match '{item.MatchId ?? "unknown"}' and object '{item.ObjectId ?? "match"}'.");
                continue;
            }

            var content = item.ContentJson.Trim();
            var snapshotType = string.IsNullOrWhiteSpace(item.SnapshotType) ? "team_intel" : item.SnapshotType;
            var contentHash = Sha256(content);
            var source = string.IsNullOrWhiteSpace(item.Source)
                ? (string.IsNullOrWhiteSpace(request.Source) ? "manual_import" : request.Source)
                : item.Source;
            var duplicate = FindDuplicateDataSnapshot(connection, transaction, source, item.MatchId, item.ObjectId, snapshotType, contentHash);
            if (duplicate != null)
            {
                result.DuplicateItems.Add(duplicate);
                continue;
            }

            var snapshot = new DataSnapshotRecord
            {
                Id = $"snapshot_{Guid.NewGuid():N}",
                Source = source,
                SnapshotType = snapshotType,
                ObjectId = item.ObjectId,
                MatchId = item.MatchId,
                ContentJson = content,
                ContentHash = contentHash,
                CapturedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            };
            SaveDataSnapshot(connection, transaction, snapshot);
            result.ImportedItems.Add(snapshot);
            SaveSystemEventLog(connection, transaction, new WorldCupSystemEventLog
            {
                EventType = "snapshot_imported",
                Category = "data",
                Source = snapshot.Source,
                ObjectId = snapshot.ObjectId,
                MatchId = snapshot.MatchId,
                SnapshotId = snapshot.Id,
                Title = "Data snapshot imported",
                Message = $"{snapshot.Source}/{snapshot.SnapshotType} imported.",
                PayloadJson = new JsonObject
                {
                    ["snapshot_type"] = snapshot.SnapshotType,
                    ["content_hash"] = snapshot.ContentHash,
                    ["content_json"] = snapshot.ContentJson
                }.ToJsonString()
            });
        }
        transaction.Commit();

        result.Imported = result.ImportedItems.Count;
        result.SkippedDuplicates = result.DuplicateItems.Count;
        var persistedItems = result.ImportedItems.Concat(result.DuplicateItems).ToList();
        result.HashesPopulated = persistedItems.Count > 0 && persistedItems.All(s => !string.IsNullOrWhiteSpace(s.ContentHash));
        result.Passed = result.Imported + result.SkippedDuplicates == result.Requested && result.HashesPopulated;
        if (result.Imported + result.SkippedDuplicates != result.Requested) result.Notes.Add($"Persisted or reused {result.Imported + result.SkippedDuplicates} of {result.Requested} requested snapshots.");
        if (result.SkippedDuplicates > 0) result.Notes.Add($"Skipped {result.SkippedDuplicates} duplicate snapshots.");
        if (!result.HashesPopulated) result.Notes.Add("One or more imported snapshots do not have content hashes.");
        return result;
    }

    public void SeedDemoDataSnapshots()
    {
        var samples = new[]
        {
            new DataSnapshotCreateRequest
            {
                Source = "manual_demo",
                SnapshotType = "team_intel",
                ObjectId = "team_arg",
                MatchId = "match_arg_jpn",
                ContentJson = """{"form":"Last 5 competitive matches: W-W-D-W-W","injuries":"No major confirmed injury in demo data","news_summary":"Squad continuity is strong; risk is complacency against a compact opponent.","market_signal":"Market leans Argentina, but draw protection remains relevant."}"""
            },
            new DataSnapshotCreateRequest
            {
                Source = "manual_demo",
                SnapshotType = "team_intel",
                ObjectId = "team_jpn",
                MatchId = "match_arg_jpn",
                ContentJson = """{"form":"Last 5 competitive matches: W-D-W-L-W","injuries":"One rotation defender flagged as doubtful in demo data","news_summary":"Japan profile favors transition speed and disciplined pressing.","market_signal":"Underdog price implies upset probability is small but non-zero."}"""
            },
            new DataSnapshotCreateRequest
            {
                Source = "manual_demo",
                SnapshotType = "match_intel",
                MatchId = "match_arg_jpn",
                ContentJson = """{"venue_note":"Neutral venue in demo data","weather_note":"Mild evening conditions","tactical_note":"Argentina possession control vs Japan transition defense","data_quality":"demo_static"}"""
            }
        };

        foreach (var sample in samples)
        {
            var exists = GetDataSnapshots(sample.MatchId, sample.ObjectId, sample.SnapshotType)
                .Any(s => s.ContentHash == Sha256(sample.ContentJson));
            if (!exists)
            {
                AddDataSnapshot(sample);
            }
        }
    }

    public string BuildDataSnapshotContext(string matchId, string? objectId = null)
    {
        var snapshots = GetDataSnapshots(matchId, objectId)
            .Concat(GetDataSnapshots(matchId, null, "match_intel"))
            .GroupBy(snapshot => snapshot.Id)
            .Select(group => group.First())
            .Take(8)
            .ToList();
        if (snapshots.Count == 0) return "No structured data snapshots.";

        var sb = new StringBuilder();
        foreach (var snapshot in snapshots)
        {
            var target = string.IsNullOrWhiteSpace(snapshot.ObjectId) ? "match" : snapshot.ObjectId;
            sb.AppendLine($"- [{snapshot.SnapshotType} from {snapshot.Source}, target {target}] {snapshot.ContentJson}");
        }
        return sb.ToString().Trim();
    }

    public DataSnapshotHarnessResult RunDataSnapshotHarness()
    {
        var before = GetDataSnapshots("match_arg_jpn").Count;
        SeedDemoDataSnapshots();
        var snapshots = GetDataSnapshots("match_arg_jpn");
        var context = BuildDataSnapshotContext("match_arg_jpn", "team_arg");
        var result = new DataSnapshotHarnessResult
        {
            SnapshotsCreated = Math.Max(0, snapshots.Count - before),
            SnapshotsRecalled = snapshots.Count,
            ContextContainsTeamIntel = context.Contains("Argentina", StringComparison.OrdinalIgnoreCase)
                && context.Contains("tactical_note", StringComparison.OrdinalIgnoreCase),
            HashesPopulated = snapshots.Count > 0 && snapshots.All(s => !string.IsNullOrWhiteSpace(s.ContentHash))
        };

        if (snapshots.Count < 3) result.Notes.Add($"Expected at least 3 demo snapshots, got {snapshots.Count}.");
        if (!result.ContextContainsTeamIntel) result.Notes.Add("Snapshot context did not include expected team and match intel.");
        if (!result.HashesPopulated) result.Notes.Add("One or more snapshots do not have content hashes.");
        result.Passed = snapshots.Count >= 3 && result.ContextContainsTeamIntel && result.HashesPopulated;
        return result;
    }

    public DataSnapshotBatchImportResult RunDataSnapshotImportHarness()
    {
        SeedDemoWorldCupCompany();
        SeedDemoDataSnapshots();
        var marker = $"argentina_pressing_2026_{Guid.NewGuid():N}";
        var request = new DataSnapshotBatchImportRequest
        {
            Source = "harness_import",
            Items =
            [
                new DataSnapshotCreateRequest
                {
                    SnapshotType = "team_intel",
                    ObjectId = "team_arg",
                    MatchId = "match_arg_jpn",
                    ContentJson = $$"""{"imported_evidence_marker":"{{marker}}","form_note":"Argentina rehearsal data indicates coordinated pressing after possession loss.","confidence":"medium"}"""
                },
                new DataSnapshotCreateRequest
                {
                    SnapshotType = "team_intel",
                    ObjectId = "team_jpn",
                    MatchId = "match_arg_jpn",
                    ContentJson = $$"""{"imported_evidence_marker":"{{marker}}","transition_note":"Japan rehearsal data highlights fast wide counterattacks.","confidence":"medium"}"""
                },
                new DataSnapshotCreateRequest
                {
                    SnapshotType = "match_intel",
                    MatchId = "match_arg_jpn",
                    ContentJson = $$"""{"imported_evidence_marker":"{{marker}}","venue_note":"Harness neutral venue note for recall validation.","data_quality":"synthetic_harness"}"""
                }
            ]
        };

        var result = ImportDataSnapshots(request);
        var recalled = GetDataSnapshots("match_arg_jpn")
            .Where(snapshot => snapshot.ContentJson.Contains(marker, StringComparison.Ordinal))
            .ToList();
        var context = BuildDataSnapshotContext("match_arg_jpn", "team_arg");
        result.Recalled = recalled.Count;
        result.ContextContainsImportedEvidence = context.Contains(marker, StringComparison.Ordinal);
        result.HashesPopulated = result.HashesPopulated && recalled.Count > 0 && recalled.All(s => !string.IsNullOrWhiteSpace(s.ContentHash));
        if (result.Recalled < request.Items.Count) result.Notes.Add($"Expected to recall {request.Items.Count} imported snapshots, got {result.Recalled}.");
        if (!result.ContextContainsImportedEvidence) result.Notes.Add("Imported evidence was not included in data snapshot context.");
        result.Passed = result.Imported == request.Items.Count
            && result.Recalled >= request.Items.Count
            && result.ContextContainsImportedEvidence
            && result.HashesPopulated;
        return result;
    }

    public DataSnapshotMaintenanceResult PruneDuplicateDataSnapshots()
    {
        using var connection = OpenConnection();
        var before = Count(connection, "data_snapshots");
        using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM data_snapshots
            WHERE id IN (
                SELECT id
                FROM (
                    SELECT
                        id,
                        ROW_NUMBER() OVER (
                            PARTITION BY IFNULL(match_id, ''), IFNULL(object_id, ''), snapshot_type, content_hash
                            ORDER BY captured_at DESC, id DESC
                        ) AS row_number
                    FROM data_snapshots
                )
                WHERE row_number > 1
            )
            """;
        var removed = command.ExecuteNonQuery();
        var after = Count(connection, "data_snapshots");
        return new DataSnapshotMaintenanceResult
        {
            BeforeCount = before,
            AfterCount = after,
            DuplicatesRemoved = removed,
            Passed = before - after == removed
        };
    }

    public DataSnapshotMaintenanceResult RunDataSnapshotMaintenanceHarness()
    {
        SeedDemoWorldCupCompany();
        var marker = $"dedupe_harness_{Guid.NewGuid():N}";
        var duplicateContent = $$"""{"dedupe_marker":"{{marker}}","note":"same content should not be stored twice"}""";
        var request = new DataSnapshotBatchImportRequest
        {
            Source = "dedupe_harness",
            Items =
            [
                new DataSnapshotCreateRequest
                {
                    SnapshotType = "team_intel",
                    ObjectId = "team_arg",
                    MatchId = "match_arg_jpn",
                    ContentJson = duplicateContent
                },
                new DataSnapshotCreateRequest
                {
                    SnapshotType = "team_intel",
                    ObjectId = "team_arg",
                    MatchId = "match_arg_jpn",
                    ContentJson = duplicateContent
                }
            ]
        };

        var importResult = ImportDataSnapshots(request);
        var pruneResult = PruneDuplicateDataSnapshots();
        pruneResult.DuplicateImportSkipped = importResult.Imported == 1 && importResult.SkippedDuplicates == 1;
        var matching = GetDataSnapshots("match_arg_jpn", "team_arg", "team_intel")
            .Count(snapshot => snapshot.ContentJson.Contains(marker, StringComparison.Ordinal));
        if (!pruneResult.DuplicateImportSkipped)
        {
            pruneResult.Notes.Add($"Expected one import and one duplicate skip, got imported={importResult.Imported}, skipped={importResult.SkippedDuplicates}.");
        }
        if (matching != 1)
        {
            pruneResult.Notes.Add($"Expected exactly one matching dedupe harness snapshot, got {matching}.");
        }
        if (!pruneResult.Passed)
        {
            pruneResult.Notes.Add("Duplicate prune count did not match before/after difference.");
        }
        pruneResult.Passed = pruneResult.Passed && pruneResult.DuplicateImportSkipped && matching == 1;
        return pruneResult;
    }

    public DataSnapshotQualityResult AuditDataSnapshotQuality(
        string? source = null,
        string? matchId = null,
        string? objectId = null,
        string? snapshotType = null,
        int limit = 200)
    {
        var snapshots = GetDataSnapshots(matchId, objectId, snapshotType)
            .Where(snapshot => string.IsNullOrWhiteSpace(source) || string.Equals(snapshot.Source, source, StringComparison.OrdinalIgnoreCase))
            .Take(Math.Clamp(limit, 1, 1000))
            .ToList();
        var result = new DataSnapshotQualityResult { SnapshotsChecked = snapshots.Count };

        foreach (var snapshot in snapshots)
        {
            if (string.IsNullOrWhiteSpace(snapshot.Source)) result.MissingSourceCount++;
            if (string.IsNullOrWhiteSpace(snapshot.MatchId) && string.IsNullOrWhiteSpace(snapshot.ObjectId)) result.MissingTargetCount++;
            if (string.IsNullOrWhiteSpace(snapshot.ContentHash) || !string.Equals(snapshot.ContentHash, Sha256(snapshot.ContentJson), StringComparison.OrdinalIgnoreCase))
            {
                result.HashMismatchCount++;
            }

            try
            {
                using var document = JsonDocument.Parse(snapshot.ContentJson);
                result.ValidJsonCount++;
                if (snapshot.SnapshotType.Contains("news", StringComparison.OrdinalIgnoreCase)
                    && !HasUsableNewsShape(document.RootElement))
                {
                    result.NewsShapeErrors++;
                }
            }
            catch (JsonException)
            {
                result.InvalidJsonCount++;
            }
        }

        if (result.SnapshotsChecked == 0) result.Notes.Add("No snapshots matched the quality audit filters.");
        if (result.InvalidJsonCount > 0) result.Notes.Add($"Found {result.InvalidJsonCount} snapshots with invalid JSON.");
        if (result.HashMismatchCount > 0) result.Notes.Add($"Found {result.HashMismatchCount} snapshots with missing or mismatched hashes.");
        if (result.MissingSourceCount > 0) result.Notes.Add($"Found {result.MissingSourceCount} snapshots with missing source.");
        if (result.MissingTargetCount > 0) result.Notes.Add($"Found {result.MissingTargetCount} snapshots without match_id or object_id.");
        if (result.NewsShapeErrors > 0) result.Notes.Add($"Found {result.NewsShapeErrors} news snapshots without article-like fields.");
        result.Passed = result.SnapshotsChecked > 0
            && result.InvalidJsonCount == 0
            && result.HashMismatchCount == 0
            && result.MissingSourceCount == 0
            && result.NewsShapeErrors == 0;
        return result;
    }

    public DataSnapshotQualityResult AuditDataSnapshotQualityForSources(IEnumerable<string> sources, int limitPerSource = 200)
    {
        var result = new DataSnapshotQualityResult();
        var sourceList = sources
            .Where(source => !string.IsNullOrWhiteSpace(source))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        foreach (var source in sourceList)
        {
            var sourceResult = AuditDataSnapshotQuality(source: source, limit: limitPerSource);
            result.SnapshotsChecked += sourceResult.SnapshotsChecked;
            result.ValidJsonCount += sourceResult.ValidJsonCount;
            result.InvalidJsonCount += sourceResult.InvalidJsonCount;
            result.HashMismatchCount += sourceResult.HashMismatchCount;
            result.MissingSourceCount += sourceResult.MissingSourceCount;
            result.MissingTargetCount += sourceResult.MissingTargetCount;
            result.NewsShapeErrors += sourceResult.NewsShapeErrors;
            result.Notes.AddRange(sourceResult.Notes.Select(note => $"{source}: {note}"));
        }

        if (sourceList.Count == 0) result.Notes.Add("No source names were supplied for snapshot quality audit.");
        if (result.SnapshotsChecked == 0 && sourceList.Count > 0) result.Notes.Add("No snapshots matched the supplied source names.");
        result.Passed = result.InvalidJsonCount == 0
            && result.HashMismatchCount == 0
            && result.MissingSourceCount == 0
            && result.NewsShapeErrors == 0;
        return result;
    }

    public DataSnapshotQualityHarnessResult RunDataSnapshotQualityHarness()
    {
        SeedDemoWorldCupCompany();
        var marker = $"snapshot_quality_{Guid.NewGuid():N}";
        var validSource = $"snapshot_quality_valid_{Guid.NewGuid():N}";
        var invalidSource = $"snapshot_quality_invalid_{Guid.NewGuid():N}";
        ImportDataSnapshots(new DataSnapshotBatchImportRequest
        {
            Source = validSource,
            Items =
            [
                new DataSnapshotCreateRequest
                {
                    Source = validSource,
                    SnapshotType = "news_intel",
                    ObjectId = "team_can",
                    ContentJson = $$"""{"provider":"harness","articles":[{"title":"Canada lineup quality {{marker}}","description":"Canada squad selection update before the 2026 World Cup.","url":"https://example.com/{{marker}}"}]}"""
                },
                new DataSnapshotCreateRequest
                {
                    Source = validSource,
                    SnapshotType = "team_intel",
                    ObjectId = "team_can",
                    MatchId = "match_wc26_1",
                    ContentJson = $$"""{"marker":"{{marker}}","team":"Canada","note":"valid structured team evidence"}"""
                }
            ]
        });

        AddDataSnapshot(new DataSnapshotCreateRequest
        {
            Source = invalidSource,
            SnapshotType = "news_intel",
            ObjectId = "team_can",
            ContentJson = "{\"broken_json\":\"" + marker + "\""
        });

        var validQuality = AuditDataSnapshotQuality(source: validSource, limit: 20);
        var invalidQuality = AuditDataSnapshotQuality(source: invalidSource, limit: 20);
        var result = new DataSnapshotQualityHarnessResult
        {
            ValidSourceQuality = validQuality,
            InvalidJsonDetected = invalidQuality.InvalidJsonCount > 0 && !invalidQuality.Passed,
            NewsShapeValidated = validQuality.NewsShapeErrors == 0,
            HashesValidated = validQuality.HashMismatchCount == 0,
        };
        if (!validQuality.Passed) result.Notes.AddRange(validQuality.Notes.Select(note => $"Valid source: {note}"));
        if (!result.InvalidJsonDetected) result.Notes.Add("Invalid JSON snapshot was not detected.");
        if (!result.NewsShapeValidated) result.Notes.Add("Valid news snapshot shape was not accepted.");
        if (!result.HashesValidated) result.Notes.Add("Snapshot hashes were not validated.");
        result.Passed = validQuality.Passed
            && result.InvalidJsonDetected
            && result.NewsShapeValidated
            && result.HashesValidated;
        return result;
    }

    private static bool HasUsableNewsShape(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) return false;
        if (root.TryGetProperty("articles", out var articles) && articles.ValueKind == JsonValueKind.Array)
        {
            return articles.GetArrayLength() == 0 || articles.EnumerateArray().Any(HasArticleFields);
        }
        return HasArticleFields(root);
    }

    private static bool HasArticleFields(JsonElement article)
    {
        return article.ValueKind == JsonValueKind.Object
            && (article.TryGetProperty("title", out _)
                || article.TryGetProperty("description", out _)
                || article.TryGetProperty("url", out _)
                || article.TryGetProperty("raw", out _));
    }
}

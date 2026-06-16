using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace PiPiClaw.Team;

public sealed partial class WorldCupStore
{
    private readonly string _databasePath;
    private readonly string _connectionString;

    public WorldCupStore(string databasePath)
    {
        _databasePath = Path.GetFullPath(databasePath);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();
    }

    public string DatabasePath => _databasePath;

    public void Initialize()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_databasePath) ?? ".");
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = SchemaSql;
        command.ExecuteNonQuery();
        EnsureColumn(connection, "memories", "content_hash", "TEXT NOT NULL DEFAULT ''");
    }

    public WorldCupStoreStatus GetStatus()
    {
        using var connection = OpenConnection();
        return new WorldCupStoreStatus
        {
            DatabasePath = _databasePath,
            WatchObjects = Count(connection, "watch_objects"),
            Employees = Count(connection, "employees"),
            Assignments = Count(connection, "employee_assignments"),
            WorkflowRuns = Count(connection, "workflow_runs"),
            LlmCalls = Count(connection, "llm_calls"),
            DataSnapshots = Count(connection, "data_snapshots"),
            Matches = Count(connection, "matches"),
            BaselinePredictions = Count(connection, "baseline_predictions")
        };
    }

    public List<WorldCupWatchObject> GetWatchObjects()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, type, symbol, name, display_name, status, metadata_json, created_at, updated_at
            FROM watch_objects
            ORDER BY display_name, name
            """;
        using var reader = command.ExecuteReader();
        var rows = new List<WorldCupWatchObject>();
        while (reader.Read())
        {
            rows.Add(new WorldCupWatchObject
            {
                Id = reader.GetString(0),
                Type = reader.GetString(1),
                Symbol = reader.GetString(2),
                Name = reader.GetString(3),
                DisplayName = reader.GetString(4),
                Status = reader.GetString(5),
                MetadataJson = reader.GetString(6),
                CreatedAt = reader.GetString(7),
                UpdatedAt = reader.GetString(8)
            });
        }
        return rows;
    }

    public List<WorldCupEmployee> GetEmployees()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, name, role, specialty, status, prompt_profile, model_index, contacts_json, created_at, updated_at
            FROM employees
            ORDER BY role, name
            """;
        using var reader = command.ExecuteReader();
        var rows = new List<WorldCupEmployee>();
        while (reader.Read())
        {
            rows.Add(new WorldCupEmployee
            {
                Id = reader.GetString(0),
                Name = reader.GetString(1),
                Role = reader.GetString(2),
                Specialty = reader.GetString(3),
                Status = reader.GetString(4),
                PromptProfile = reader.GetString(5),
                ModelIndex = reader.GetInt32(6),
                ContactsJson = reader.GetString(7),
                CreatedAt = reader.GetString(8),
                UpdatedAt = reader.GetString(9)
            });
        }
        return rows;
    }

    public WorldCupEmployee? GetEmployeeById(string employeeId)
    {
        using var connection = OpenConnection();
        return GetEmployee(connection, employeeId);
    }

    public WorldCupMatch? GetMatchById(string matchId)
    {
        using var connection = OpenConnection();
        return GetMatch(connection, matchId);
    }

    public WorldCupWatchObject? GetWatchObjectById(string objectId)
    {
        using var connection = OpenConnection();
        return GetWatchObject(connection, objectId);
    }

    public string? GetPrimaryEmployeeIdForObject(string objectId)
    {
        using var connection = OpenConnection();
        return FindPrimaryEmployeeId(connection, objectId);
    }

    public List<EmployeeAssignment> GetAssignments()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, employee_id, object_id, assignment_role, status, started_at, ended_at, metadata_json
            FROM employee_assignments
            ORDER BY started_at
            """;
        using var reader = command.ExecuteReader();
        var rows = new List<EmployeeAssignment>();
        while (reader.Read())
        {
            rows.Add(new EmployeeAssignment
            {
                Id = reader.GetString(0),
                EmployeeId = reader.GetString(1),
                ObjectId = reader.GetString(2),
                AssignmentRole = reader.GetString(3),
                Status = reader.GetString(4),
                StartedAt = reader.GetString(5),
                EndedAt = reader.IsDBNull(6) ? null : reader.GetString(6),
                MetadataJson = reader.GetString(7)
            });
        }
        return rows;
    }

    public List<WorldCupMatch> GetMatches()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, stage, group_name, home_object_id, away_object_id, kickoff_time, venue, status, home_score, away_score
            FROM matches
            ORDER BY kickoff_time, id
            """;
        using var reader = command.ExecuteReader();
        var rows = new List<WorldCupMatch>();
        while (reader.Read())
        {
            rows.Add(ReadMatch(reader));
        }
        return rows;
    }

    public string GetCurrentTournamentStage()
    {
        var matches = GetMatches();
        if (matches.Count == 0)
        {
            return "pre_tournament";
        }

        var now = DateTime.UtcNow;
        var hasGroupStageMatches = matches.Any(m => m.Stage == "group");
        var hasKnockoutStageMatches = matches.Any(m => m.Stage != "group");
        
        // 检查是否有进行中的比赛
        var liveMatches = matches.Where(m => m.Status == "live").ToList();
        if (liveMatches.Any())
        {
            return liveMatches.First().Stage;
        }

        // 检查是否有已完成的淘汰赛
        var finishedKnockoutMatches = matches.Where(m => m.Stage != "group" && m.Status == "finished").ToList();
        if (finishedKnockoutMatches.Any())
        {
            return "knockout";
        }

        // 检查是否有已完成的小组赛
        var finishedGroupMatches = matches.Where(m => m.Stage == "group" && m.Status == "finished").ToList();
        if (finishedGroupMatches.Any())
        {
            return "group";
        }

        // 检查下一场比赛
        var nextMatch = matches.Where(m => m.Status == "scheduled").OrderBy(m => m.KickoffTime).FirstOrDefault();
        if (nextMatch != null && DateTime.TryParse(nextMatch.KickoffTime, out var kickoffTime))
        {
            var timeUntilKickoff = kickoffTime - now;
            // 如果比赛在一周内开始，显示对应阶段
            if (timeUntilKickoff.TotalDays < 7)
            {
                return nextMatch.Stage;
            }
        }

        return "pre_tournament";
    }

    public List<BaselinePredictionRecord> GetBaselinePredictions(string? matchId = null)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        if (string.IsNullOrWhiteSpace(matchId))
        {
            command.CommandText = """
                SELECT id, match_id, strategy_version, home_win_probability, draw_probability, away_win_probability,
                       method, input_snapshot_ids_json, explanation, created_at
                FROM baseline_predictions
                ORDER BY created_at DESC
                """;
        }
        else
        {
            command.CommandText = """
                SELECT id, match_id, strategy_version, home_win_probability, draw_probability, away_win_probability,
                       method, input_snapshot_ids_json, explanation, created_at
                FROM baseline_predictions
                WHERE match_id = $match_id
                ORDER BY created_at DESC
                """;
            Add(command, "$match_id", matchId);
        }

        using var reader = command.ExecuteReader();
        var rows = new List<BaselinePredictionRecord>();
        while (reader.Read())
        {
            rows.Add(new BaselinePredictionRecord
            {
                Id = reader.GetString(0),
                MatchId = reader.GetString(1),
                StrategyVersion = reader.GetString(2),
                HomeWinProbability = reader.GetDouble(3),
                DrawProbability = reader.GetDouble(4),
                AwayWinProbability = reader.GetDouble(5),
                Method = reader.GetString(6),
                InputSnapshotIdsJson = reader.GetString(7),
                Explanation = reader.GetString(8),
                CreatedAt = reader.GetString(9)
            });
        }
        return rows;
    }

    public List<WorkflowRunRecord> GetWorkflowRuns()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, workflow_type, status, object_id, match_id, started_by, started_at, completed_at, error_message, metadata_json
            FROM workflow_runs
            ORDER BY started_at DESC
            """;
        using var reader = command.ExecuteReader();
        var rows = new List<WorkflowRunRecord>();
        while (reader.Read())
        {
            rows.Add(ReadWorkflowRun(reader));
        }
        return rows;
    }

    public List<WorkflowStepRecord> GetWorkflowSteps(string workflowRunId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, workflow_run_id, step_type, status, assignee_employee_id, started_at, completed_at,
                   input_json, output_json, artifact_id, error_message
            FROM workflow_steps
            WHERE workflow_run_id = $workflow_run_id
            ORDER BY id
            """;
        Add(command, "$workflow_run_id", workflowRunId);
        using var reader = command.ExecuteReader();
        var rows = new List<WorkflowStepRecord>();
        while (reader.Read())
        {
            rows.Add(ReadWorkflowStep(reader));
        }
        return rows;
    }

    public List<ArtifactRecord> GetArtifacts(string? workflowRunId = null)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        if (string.IsNullOrWhiteSpace(workflowRunId))
        {
            command.CommandText = """
                SELECT id, type, title, owner_employee_id, object_id, workflow_run_id, file_path, summary,
                       mime_type, content_hash, version, metadata_json, parent_artifact_id, created_at
                FROM artifacts
                ORDER BY created_at DESC
                """;
        }
        else
        {
            command.CommandText = """
                SELECT id, type, title, owner_employee_id, object_id, workflow_run_id, file_path, summary,
                       mime_type, content_hash, version, metadata_json, parent_artifact_id, created_at
                FROM artifacts
                WHERE workflow_run_id = $workflow_run_id
                ORDER BY created_at DESC
                """;
            Add(command, "$workflow_run_id", workflowRunId);
        }

        using var reader = command.ExecuteReader();
        var rows = new List<ArtifactRecord>();
        while (reader.Read())
        {
            rows.Add(ReadArtifact(reader));
        }
        return rows;
    }

    public ArtifactContent? GetArtifactContent(string artifactId)
    {
        var artifact = GetArtifacts().FirstOrDefault(a => a.Id == artifactId);
        if (artifact == null) return null;

        var baseDir = Path.GetFullPath(System.AppContext.BaseDirectory);
        var fullPath = Path.GetFullPath(Path.Combine(baseDir, artifact.FilePath));
        if (!fullPath.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Artifact path escapes application directory.");
        }
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Artifact file not found.", artifact.FilePath);
        }

        return new ArtifactContent
        {
            Artifact = artifact,
            Content = File.ReadAllText(fullPath, Encoding.UTF8)
        };
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys = ON;";
        command.ExecuteNonQuery();
        return connection;
    }

    private static int Count(SqliteConnection connection, string tableName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {tableName}";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private int CountLlmCallsByIds(IEnumerable<string> ids)
    {
        using var connection = OpenConnection();
        var count = 0;
        foreach (var id in ids)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM llm_calls WHERE id = $id";
            Add(command, "$id", id);
            count += Convert.ToInt32(command.ExecuteScalar());
        }
        return count;
    }

    private static void EnsureColumn(SqliteConnection connection, string tableName, string columnName, string columnDefinition)
    {
        using var check = connection.CreateCommand();
        check.CommandText = $"PRAGMA table_info({tableName})";
        using var reader = check.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnDefinition}";
        alter.ExecuteNonQuery();
    }

    private static void UpsertWatchObject(SqliteConnection connection, SqliteTransaction transaction, WorldCupWatchObject item)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO watch_objects (id, type, symbol, name, display_name, status, metadata_json, created_at, updated_at)
            VALUES ($id, $type, $symbol, $name, $display_name, $status, $metadata_json, $created_at, $updated_at)
            ON CONFLICT(id) DO UPDATE SET
                type = excluded.type,
                symbol = excluded.symbol,
                name = excluded.name,
                display_name = excluded.display_name,
                status = excluded.status,
                metadata_json = excluded.metadata_json,
                updated_at = excluded.updated_at
            """;
        Add(command, "$id", item.Id);
        Add(command, "$type", item.Type);
        Add(command, "$symbol", item.Symbol);
        Add(command, "$name", item.Name);
        Add(command, "$display_name", item.DisplayName);
        Add(command, "$status", item.Status);
        Add(command, "$metadata_json", item.MetadataJson);
        Add(command, "$created_at", item.CreatedAt);
        Add(command, "$updated_at", item.UpdatedAt);
        command.ExecuteNonQuery();
    }

    private static void UpsertEmployee(SqliteConnection connection, SqliteTransaction transaction, WorldCupEmployee item)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO employees (id, name, role, specialty, status, prompt_profile, model_index, contacts_json, created_at, updated_at)
            VALUES ($id, $name, $role, $specialty, $status, $prompt_profile, $model_index, $contacts_json, $created_at, $updated_at)
            ON CONFLICT(id) DO UPDATE SET
                name = excluded.name,
                role = excluded.role,
                specialty = excluded.specialty,
                status = excluded.status,
                prompt_profile = excluded.prompt_profile,
                model_index = excluded.model_index,
                contacts_json = excluded.contacts_json,
                updated_at = excluded.updated_at
            """;
        Add(command, "$id", item.Id);
        Add(command, "$name", item.Name);
        Add(command, "$role", item.Role);
        Add(command, "$specialty", item.Specialty);
        Add(command, "$status", item.Status);
        Add(command, "$prompt_profile", item.PromptProfile);
        Add(command, "$model_index", item.ModelIndex);
        Add(command, "$contacts_json", item.ContactsJson);
        Add(command, "$created_at", item.CreatedAt);
        Add(command, "$updated_at", item.UpdatedAt);
        command.ExecuteNonQuery();
    }

    private static void UpsertAssignment(SqliteConnection connection, SqliteTransaction transaction, EmployeeAssignment item)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO employee_assignments (id, employee_id, object_id, assignment_role, status, started_at, ended_at, metadata_json)
            VALUES ($id, $employee_id, $object_id, $assignment_role, $status, $started_at, $ended_at, $metadata_json)
            ON CONFLICT(id) DO UPDATE SET
                employee_id = excluded.employee_id,
                object_id = excluded.object_id,
                assignment_role = excluded.assignment_role,
                status = excluded.status,
                ended_at = excluded.ended_at,
                metadata_json = excluded.metadata_json
            """;
        Add(command, "$id", item.Id);
        Add(command, "$employee_id", item.EmployeeId);
        Add(command, "$object_id", item.ObjectId);
        Add(command, "$assignment_role", item.AssignmentRole);
        Add(command, "$status", item.Status);
        Add(command, "$started_at", item.StartedAt);
        Add(command, "$ended_at", item.EndedAt);
        Add(command, "$metadata_json", item.MetadataJson);
        command.ExecuteNonQuery();
    }

    private static void EndActiveAssignmentsForObject(SqliteConnection connection, SqliteTransaction transaction, string objectId, string endedAt, string metadataJson)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE employee_assignments
            SET status = 'ended',
                ended_at = $ended_at,
                metadata_json = $metadata_json
            WHERE object_id = $object_id
              AND status = 'active'
            """;
        Add(command, "$ended_at", endedAt);
        Add(command, "$metadata_json", metadataJson);
        Add(command, "$object_id", objectId);
        command.ExecuteNonQuery();
    }

    private static void UpsertMatch(SqliteConnection connection, SqliteTransaction transaction, WorldCupMatch item)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO matches (id, stage, group_name, home_object_id, away_object_id, kickoff_time, venue, status, home_score, away_score)
            VALUES ($id, $stage, $group_name, $home_object_id, $away_object_id, $kickoff_time, $venue, $status, $home_score, $away_score)
            ON CONFLICT(id) DO UPDATE SET
                stage = excluded.stage,
                group_name = excluded.group_name,
                home_object_id = excluded.home_object_id,
                away_object_id = excluded.away_object_id,
                kickoff_time = excluded.kickoff_time,
                venue = excluded.venue,
                status = excluded.status,
                home_score = excluded.home_score,
                away_score = excluded.away_score
            """;
        Add(command, "$id", item.Id);
        Add(command, "$stage", item.Stage);
        Add(command, "$group_name", item.GroupName);
        Add(command, "$home_object_id", item.HomeObjectId);
        Add(command, "$away_object_id", item.AwayObjectId);
        Add(command, "$kickoff_time", item.KickoffTime);
        Add(command, "$venue", item.Venue);
        Add(command, "$status", item.Status);
        Add(command, "$home_score", item.HomeScore);
        Add(command, "$away_score", item.AwayScore);
        command.ExecuteNonQuery();
    }

    private static void UpsertBaselinePrediction(SqliteConnection connection, BaselinePredictionRecord item)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO baseline_predictions (id, match_id, strategy_version, home_win_probability, draw_probability,
                away_win_probability, method, input_snapshot_ids_json, explanation, created_at)
            VALUES ($id, $match_id, $strategy_version, $home_win_probability, $draw_probability,
                $away_win_probability, $method, $input_snapshot_ids_json, $explanation, $created_at)
            ON CONFLICT(id) DO UPDATE SET
                match_id = excluded.match_id,
                strategy_version = excluded.strategy_version,
                home_win_probability = excluded.home_win_probability,
                draw_probability = excluded.draw_probability,
                away_win_probability = excluded.away_win_probability,
                method = excluded.method,
                input_snapshot_ids_json = excluded.input_snapshot_ids_json,
                explanation = excluded.explanation,
                created_at = excluded.created_at
            """;
        Add(command, "$id", item.Id);
        Add(command, "$match_id", item.MatchId);
        Add(command, "$strategy_version", item.StrategyVersion);
        Add(command, "$home_win_probability", item.HomeWinProbability);
        Add(command, "$draw_probability", item.DrawProbability);
        Add(command, "$away_win_probability", item.AwayWinProbability);
        Add(command, "$method", item.Method);
        Add(command, "$input_snapshot_ids_json", item.InputSnapshotIdsJson);
        Add(command, "$explanation", item.Explanation);
        Add(command, "$created_at", item.CreatedAt);
        command.ExecuteNonQuery();
    }

    private void SaveDataSnapshot(DataSnapshotRecord item)
    {
        using var connection = OpenConnection();
        SaveDataSnapshot(connection, null, item);
    }

    private static DataSnapshotRecord? FindDuplicateDataSnapshot(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string source,
        string? matchId,
        string? objectId,
        string snapshotType,
        string contentHash)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT id, source, snapshot_type, object_id, match_id, content_json, content_hash, captured_at
            FROM data_snapshots
            WHERE IFNULL(match_id, '') = $match_id
              AND IFNULL(object_id, '') = $object_id
              AND source = $source
              AND snapshot_type = $snapshot_type
              AND content_hash = $content_hash
            ORDER BY captured_at DESC, id DESC
            LIMIT 1
            """;
        Add(command, "$source", source);
        Add(command, "$match_id", matchId ?? "");
        Add(command, "$object_id", objectId ?? "");
        Add(command, "$snapshot_type", snapshotType);
        Add(command, "$content_hash", contentHash);
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadDataSnapshot(reader) : null;
    }

    private static void SaveDataSnapshot(SqliteConnection connection, SqliteTransaction? transaction, DataSnapshotRecord item)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO data_snapshots (id, source, snapshot_type, object_id, match_id, content_json, content_hash, captured_at)
            VALUES ($id, $source, $snapshot_type, $object_id, $match_id, $content_json, $content_hash, $captured_at)
            ON CONFLICT(id) DO UPDATE SET
                source = excluded.source,
                snapshot_type = excluded.snapshot_type,
                object_id = excluded.object_id,
                match_id = excluded.match_id,
                content_json = excluded.content_json,
                content_hash = excluded.content_hash,
                captured_at = excluded.captured_at
            """;
        Add(command, "$id", item.Id);
        Add(command, "$source", item.Source);
        Add(command, "$snapshot_type", item.SnapshotType);
        Add(command, "$object_id", item.ObjectId);
        Add(command, "$match_id", item.MatchId);
        Add(command, "$content_json", item.ContentJson);
        Add(command, "$content_hash", item.ContentHash);
        Add(command, "$captured_at", item.CapturedAt);
        command.ExecuteNonQuery();
    }

    private static WorldCupSystemEventLog PrepareSystemEventLog(WorldCupSystemEventLog item)
    {
        if (string.IsNullOrWhiteSpace(item.Id)) item.Id = $"event_{Guid.NewGuid():N}";
        if (string.IsNullOrWhiteSpace(item.CreatedAt)) item.CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        if (string.IsNullOrWhiteSpace(item.PayloadJson)) item.PayloadJson = "{}";
        if (string.IsNullOrWhiteSpace(item.ContentHash)) item.ContentHash = Sha256(item.PayloadJson);
        return item;
    }

    private static void SaveSystemEventLog(SqliteConnection connection, SqliteTransaction? transaction, WorldCupSystemEventLog item)
    {
        item = PrepareSystemEventLog(item);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO system_event_logs (id, event_type, category, severity, source, employee_id, object_id, match_id,
                workflow_run_id, llm_call_id, snapshot_id, artifact_id, title, message, payload_json, content_hash, created_at)
            VALUES ($id, $event_type, $category, $severity, $source, $employee_id, $object_id, $match_id,
                $workflow_run_id, $llm_call_id, $snapshot_id, $artifact_id, $title, $message, $payload_json, $content_hash, $created_at)
            ON CONFLICT(id) DO UPDATE SET
                event_type = excluded.event_type,
                category = excluded.category,
                severity = excluded.severity,
                source = excluded.source,
                employee_id = excluded.employee_id,
                object_id = excluded.object_id,
                match_id = excluded.match_id,
                workflow_run_id = excluded.workflow_run_id,
                llm_call_id = excluded.llm_call_id,
                snapshot_id = excluded.snapshot_id,
                artifact_id = excluded.artifact_id,
                title = excluded.title,
                message = excluded.message,
                payload_json = excluded.payload_json,
                content_hash = excluded.content_hash,
                created_at = excluded.created_at
            """;
        Add(command, "$id", item.Id);
        Add(command, "$event_type", item.EventType);
        Add(command, "$category", item.Category);
        Add(command, "$severity", item.Severity);
        Add(command, "$source", item.Source);
        Add(command, "$employee_id", item.EmployeeId);
        Add(command, "$object_id", item.ObjectId);
        Add(command, "$match_id", item.MatchId);
        Add(command, "$workflow_run_id", item.WorkflowRunId);
        Add(command, "$llm_call_id", item.LlmCallId);
        Add(command, "$snapshot_id", item.SnapshotId);
        Add(command, "$artifact_id", item.ArtifactId);
        Add(command, "$title", item.Title);
        Add(command, "$message", item.Message);
        Add(command, "$payload_json", item.PayloadJson);
        Add(command, "$content_hash", item.ContentHash);
        Add(command, "$created_at", item.CreatedAt);
        command.ExecuteNonQuery();
    }

    private static int SignalSeverityScore(string severity)
    {
        return severity.ToLowerInvariant() switch
        {
            "critical" => 4,
            "high" => 3,
            "medium" => 2,
            _ => 1
        };
    }

    private static string BuildTeamIntelligenceReport(WorldCupWatchObject team, WorldCupEmployee? employee, IReadOnlyList<IntelligenceSignalRecord> signals)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {team.DisplayName} Intelligence Brief");
        sb.AppendLine();
        sb.AppendLine($"- Team: {team.DisplayName} (`{team.Id}`)");
        sb.AppendLine($"- Researcher: {employee?.Name ?? "Unassigned"}");
        sb.AppendLine($"- Generated at: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"- Mode: structured no-LLM trigger");
        sb.AppendLine();
        sb.AppendLine("## 核心判断");
        sb.AppendLine($"{team.DisplayName} 当前有 {signals.Count} 条情报信号需要研究员复核；若包含伤停或阵容信号，应优先核验来源与影响范围。");
        sb.AppendLine();
        sb.AppendLine("## 关键证据");
        foreach (var signal in signals)
        {
            sb.AppendLine($"- `{signal.SignalType}` | {signal.Severity} | confidence {signal.Confidence:0.00} | `{signal.Id}`");
            sb.AppendLine($"  {signal.Summary}");
            sb.AppendLine($"  evidence snapshot: `{signal.SourceSnapshotId}`");
        }
        sb.AppendLine();
        sb.AppendLine("## 不确定性与风险");
        sb.AppendLine("- 当前简报只基于已入库快照和情报信号，不声称已经完成实时网页核验。");
        sb.AppendLine("- 伤停、首发和名单类信息具有时效性，赛前需要二次确认。");
        sb.AppendLine();
        sb.AppendLine("## 建议动作");
        sb.AppendLine("- 若 severity 为 high 或同类信号连续出现，触发 DeepSeek 语义复核并记录 LLM 调用成本。");
        sb.AppendLine("- 将复核结果写入球队员工日志，供 CEO 汇总时引用。");
        sb.AppendLine();
        sb.AppendLine("## 非投注说明");
        sb.AppendLine("本报告仅用于赛事情报研究和内部决策辅助，不构成投注建议，也不保证任何比赛结果。");
        return sb.ToString();
    }

    private static string TruncateForLog(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength) return value;
        return value[..maxLength] + "...";
    }

    private static IntelligenceSignalRecord? FindIntelligenceSignalByHash(SqliteConnection connection, SqliteTransaction? transaction, string contentHash)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT id, source_snapshot_id, signal_type, severity, confidence, object_id, match_id,
                   title, summary, evidence_json, status, content_hash, created_at, updated_at
            FROM intelligence_signals
            WHERE content_hash = $content_hash
            LIMIT 1
            """;
        Add(command, "$content_hash", contentHash);
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadIntelligenceSignal(reader) : null;
    }

    private static void SaveIntelligenceSignal(SqliteConnection connection, SqliteTransaction transaction, IntelligenceSignalRecord item)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO intelligence_signals (id, source_snapshot_id, signal_type, severity, confidence, object_id, match_id,
                title, summary, evidence_json, status, content_hash, created_at, updated_at)
            VALUES ($id, $source_snapshot_id, $signal_type, $severity, $confidence, $object_id, $match_id,
                $title, $summary, $evidence_json, $status, $content_hash, $created_at, $updated_at)
            ON CONFLICT(id) DO UPDATE SET
                signal_type = excluded.signal_type,
                severity = excluded.severity,
                confidence = excluded.confidence,
                object_id = excluded.object_id,
                match_id = excluded.match_id,
                title = excluded.title,
                summary = excluded.summary,
                evidence_json = excluded.evidence_json,
                status = excluded.status,
                content_hash = excluded.content_hash,
                updated_at = excluded.updated_at
            """;
        Add(command, "$id", item.Id);
        Add(command, "$source_snapshot_id", item.SourceSnapshotId);
        Add(command, "$signal_type", item.SignalType);
        Add(command, "$severity", item.Severity);
        Add(command, "$confidence", item.Confidence);
        Add(command, "$object_id", item.ObjectId);
        Add(command, "$match_id", item.MatchId);
        Add(command, "$title", item.Title);
        Add(command, "$summary", item.Summary);
        Add(command, "$evidence_json", item.EvidenceJson);
        Add(command, "$status", item.Status);
        Add(command, "$content_hash", item.ContentHash);
        Add(command, "$created_at", item.CreatedAt);
        Add(command, "$updated_at", item.UpdatedAt);
        command.ExecuteNonQuery();
    }

    private static void UpdateIntelligenceSignalStatus(SqliteConnection connection, SqliteTransaction transaction, string signalId, string status)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE intelligence_signals
            SET status = $status,
                updated_at = $updated_at
            WHERE id = $id
            """;
        Add(command, "$status", status);
        Add(command, "$updated_at", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        Add(command, "$id", signalId);
        command.ExecuteNonQuery();
    }

    private static void UpsertWorkflowRun(SqliteConnection connection, SqliteTransaction transaction, WorkflowRunRecord item)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO workflow_runs (id, workflow_type, status, object_id, match_id, started_by, started_at, completed_at, error_message, metadata_json)
            VALUES ($id, $workflow_type, $status, $object_id, $match_id, $started_by, $started_at, $completed_at, $error_message, $metadata_json)
            ON CONFLICT(id) DO UPDATE SET
                workflow_type = excluded.workflow_type,
                status = excluded.status,
                object_id = excluded.object_id,
                match_id = excluded.match_id,
                started_by = excluded.started_by,
                started_at = excluded.started_at,
                completed_at = excluded.completed_at,
                error_message = excluded.error_message,
                metadata_json = excluded.metadata_json
            """;
        Add(command, "$id", item.Id);
        Add(command, "$workflow_type", item.WorkflowType);
        Add(command, "$status", item.Status);
        Add(command, "$object_id", item.ObjectId);
        Add(command, "$match_id", item.MatchId);
        Add(command, "$started_by", item.StartedBy);
        Add(command, "$started_at", item.StartedAt);
        Add(command, "$completed_at", item.CompletedAt);
        Add(command, "$error_message", item.ErrorMessage);
        Add(command, "$metadata_json", item.MetadataJson);
        command.ExecuteNonQuery();
    }

    private static void UpsertWorkflowStep(SqliteConnection connection, SqliteTransaction transaction, WorkflowStepRecord item)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO workflow_steps (id, workflow_run_id, step_type, status, assignee_employee_id, started_at, completed_at, input_json, output_json, artifact_id, error_message)
            VALUES ($id, $workflow_run_id, $step_type, $status, $assignee_employee_id, $started_at, $completed_at, $input_json, $output_json, $artifact_id, $error_message)
            ON CONFLICT(id) DO UPDATE SET
                workflow_run_id = excluded.workflow_run_id,
                step_type = excluded.step_type,
                status = excluded.status,
                assignee_employee_id = excluded.assignee_employee_id,
                started_at = excluded.started_at,
                completed_at = excluded.completed_at,
                input_json = excluded.input_json,
                output_json = excluded.output_json,
                artifact_id = excluded.artifact_id,
                error_message = excluded.error_message
            """;
        Add(command, "$id", item.Id);
        Add(command, "$workflow_run_id", item.WorkflowRunId);
        Add(command, "$step_type", item.StepType);
        Add(command, "$status", item.Status);
        Add(command, "$assignee_employee_id", item.AssigneeEmployeeId);
        Add(command, "$started_at", item.StartedAt);
        Add(command, "$completed_at", item.CompletedAt);
        Add(command, "$input_json", item.InputJson);
        Add(command, "$output_json", item.OutputJson);
        Add(command, "$artifact_id", item.ArtifactId);
        Add(command, "$error_message", item.ErrorMessage);
        command.ExecuteNonQuery();
    }

    private static ArtifactRecord SaveArtifact(SqliteConnection connection, SqliteTransaction transaction, ArtifactRecord item, string content)
    {
        var fullPath = Path.Combine(System.AppContext.BaseDirectory, item.FilePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath) ?? System.AppContext.BaseDirectory);
        File.WriteAllText(fullPath, content, Encoding.UTF8);
        item.ContentHash = Sha256(content);

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO artifacts (id, type, title, owner_employee_id, object_id, workflow_run_id, file_path, summary,
                mime_type, content_hash, version, metadata_json, parent_artifact_id, created_at)
            VALUES ($id, $type, $title, $owner_employee_id, $object_id, $workflow_run_id, $file_path, $summary,
                $mime_type, $content_hash, $version, $metadata_json, $parent_artifact_id, $created_at)
            ON CONFLICT(id) DO UPDATE SET
                type = excluded.type,
                title = excluded.title,
                owner_employee_id = excluded.owner_employee_id,
                object_id = excluded.object_id,
                workflow_run_id = excluded.workflow_run_id,
                file_path = excluded.file_path,
                summary = excluded.summary,
                mime_type = excluded.mime_type,
                content_hash = excluded.content_hash,
                version = excluded.version,
                metadata_json = excluded.metadata_json,
                parent_artifact_id = excluded.parent_artifact_id,
                created_at = excluded.created_at
            """;
        Add(command, "$id", item.Id);
        Add(command, "$type", item.Type);
        Add(command, "$title", item.Title);
        Add(command, "$owner_employee_id", item.OwnerEmployeeId);
        Add(command, "$object_id", item.ObjectId);
        Add(command, "$workflow_run_id", item.WorkflowRunId);
        Add(command, "$file_path", item.FilePath);
        Add(command, "$summary", item.Summary);
        Add(command, "$mime_type", item.MimeType);
        Add(command, "$content_hash", item.ContentHash);
        Add(command, "$version", item.Version);
        Add(command, "$metadata_json", item.MetadataJson);
        Add(command, "$parent_artifact_id", item.ParentArtifactId);
        Add(command, "$created_at", item.CreatedAt);
        command.ExecuteNonQuery();
        return item;
    }

    private static WorkflowStepRecord MakeCompletedStep(string workflowId, string stepType, string? employeeId, string artifactId, string outputJson)
    {
        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        return new WorkflowStepRecord
        {
            Id = $"step_{workflowId}_{stepType}",
            WorkflowRunId = workflowId,
            StepType = stepType,
            Status = "completed",
            AssigneeEmployeeId = employeeId,
            StartedAt = now,
            CompletedAt = now,
            OutputJson = outputJson,
            ArtifactId = artifactId
        };
    }

    private static string? FindPrimaryEmployeeId(SqliteConnection connection, string objectId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT employee_id
            FROM employee_assignments
            WHERE object_id = $object_id AND assignment_role = 'primary_researcher' AND status = 'active'
            LIMIT 1
            """;
        Add(command, "$object_id", objectId);
        return command.ExecuteScalar() as string;
    }

    private static string BuildMockReport(WorldCupMatch match, WorldCupWatchObject home, WorldCupWatchObject away, BaselinePredictionRecord baseline)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {home.DisplayName} vs {away.DisplayName} \u8d5b\u524d\u9884\u6d4b\u62a5\u544a");
        sb.AppendLine();
        sb.AppendLine($"\u6bd4\u8d5b\uff1a{match.Id}  ");
        sb.AppendLine($"\u9636\u6bb5\uff1a{match.Stage}  ");
        sb.AppendLine($"\u5f00\u7403\u65f6\u95f4\uff1a{match.KickoffTime}  ");
        sb.AppendLine($"\u573a\u5730\uff1a{match.Venue}");
        sb.AppendLine();
        sb.AppendLine("## \u57fa\u7ebf\u80dc\u7387");
        sb.AppendLine();
        sb.AppendLine($"- {home.DisplayName}\u80dc\uff1a{baseline.HomeWinProbability:P1}");
        sb.AppendLine($"- \u5e73\u5c40\uff1a{baseline.DrawProbability:P1}");
        sb.AppendLine($"- {away.DisplayName}\u80dc\uff1a{baseline.AwayWinProbability:P1}");
        sb.AppendLine();
        sb.AppendLine("## \u5458\u5de5\u534f\u4f5c\u6458\u8981");
        sb.AppendLine();
        sb.AppendLine("- \u4e3b\u961f\u7814\u7a76\u5458\uff1a\u5df2\u5b8c\u6210 mock \u7403\u961f\u72b6\u6001\u62a5\u544a\u3002");
        sb.AppendLine("- \u5ba2\u961f\u7814\u7a76\u5458\uff1a\u5df2\u5b8c\u6210 mock \u7403\u961f\u72b6\u6001\u62a5\u544a\u3002");
        sb.AppendLine($"- \u6570\u636e\u5206\u6790\u5e08\uff1a\u5df2\u5f15\u7528 `{baseline.StrategyVersion}`\u3002");
        sb.AppendLine("- \u98ce\u9669\u5b98\uff1a\u5df2\u5b8c\u6210 mock \u98ce\u9669\u5ba1\u67e5\u3002");
        sb.AppendLine("- CEO\uff1a\u5df2\u5b8c\u6210 mock \u6c47\u603b\u3002");
        sb.AppendLine();
        sb.AppendLine("## \u8bf4\u660e");
        sb.AppendLine();
        sb.AppendLine("\u8fd9\u662f\u5de5\u4f5c\u6d41 harness \u751f\u6210\u7684\u53ef\u8ffd\u8e2a\u62a5\u544a\uff0c\u7528\u4e8e\u9a8c\u8bc1\u6d41\u7a0b\u3001\u4ea7\u7269\u3001\u6b65\u9aa4\u548c\u57fa\u7ebf\u9884\u6d4b\u5f15\u7528\u3002");
        return sb.ToString();
    }

    private static string BuildReport(
        WorldCupMatch match,
        WorldCupWatchObject home,
        WorldCupWatchObject away,
        BaselinePredictionRecord baseline,
        IReadOnlyList<StepOutputDraft> outputs,
        IReadOnlyList<DataSnapshotRecord> evidenceSnapshots)
    {
        var predicted = ResolvePredictedOutcome(baseline);
        var sb = new StringBuilder();
        sb.AppendLine($"# {home.DisplayName} vs {away.DisplayName} \u8d5b\u524d\u9884\u6d4b\u62a5\u544a");
        sb.AppendLine();
        sb.AppendLine("## 执行摘要");
        sb.AppendLine();
        sb.AppendLine($"- 预测倾向：{OutcomeLabel(predicted)}。");
        sb.AppendLine($"- 概率分布：{home.DisplayName}胜 {baseline.HomeWinProbability:P1}，平局 {baseline.DrawProbability:P1}，{away.DisplayName}胜 {baseline.AwayWinProbability:P1}。");
        sb.AppendLine("- 使用方式：这是赛前辅助分析，不构成投注建议；需要结合临场阵容、伤病和赛程密度复核。");
        sb.AppendLine();
        sb.AppendLine("## 比赛信息");
        sb.AppendLine();
        sb.AppendLine($"- 比赛：{match.Id}");
        sb.AppendLine($"- 阶段：{match.Stage}");
        sb.AppendLine($"- 开球时间：{match.KickoffTime}");
        sb.AppendLine($"- 场地：{match.Venue}");
        sb.AppendLine();
        sb.AppendLine("## 客观概率");
        sb.AppendLine();
        sb.AppendLine($"- {home.DisplayName}胜：{baseline.HomeWinProbability:P1}");
        sb.AppendLine($"- 平局：{baseline.DrawProbability:P1}");
        sb.AppendLine($"- {away.DisplayName}胜：{baseline.AwayWinProbability:P1}");
        sb.AppendLine($"- 策略版本：{baseline.StrategyVersion}");
        sb.AppendLine($"- 策略说明：{baseline.Explanation}");
        sb.AppendLine();
        sb.AppendLine("## 关键证据");
        sb.AppendLine();
        sb.AppendLine("- 基线策略提供了可复盘的三项概率，作为 CEO 决策的客观底盘。");
        sb.AppendLine("- 主客队研究员分别给出球队视角，数据分析师负责校准概率，风险官负责挑出模型盲区。");
        sb.AppendLine("- 长期记忆和结构化快照会进入员工上下文，但过期短期记忆不会继续污染报告。");
        sb.AppendLine();
        sb.AppendLine("## 证据链引用");
        sb.AppendLine();
        if (evidenceSnapshots.Count == 0)
        {
            sb.AppendLine("- 本次报告没有召回结构化情报快照；需要先导入球队或比赛级情报。");
        }
        else
        {
            foreach (var snapshot in evidenceSnapshots)
            {
                var target = string.IsNullOrWhiteSpace(snapshot.ObjectId) ? "match" : snapshot.ObjectId;
                sb.AppendLine($"- `{snapshot.Id}` | source `{snapshot.Source}` | type `{snapshot.SnapshotType}` | target `{target}` | hash `{snapshot.ContentHash}` | captured `{snapshot.CapturedAt}`");
            }
        }
        sb.AppendLine();
        sb.AppendLine("## 主要风险");
        sb.AppendLine();
        sb.AppendLine("- 当前演示数据不是实时新闻抓取结果，临场阵容、伤病和轮换需要在赛前再次确认。");
        sb.AppendLine("- 概率是辅助判断，不是确定性结论；强队也可能受红牌、点球、天气和赛程影响。");
        sb.AppendLine();
        sb.AppendLine("## 员工协作输出");
        foreach (var output in outputs)
        {
            sb.AppendLine();
            sb.AppendLine($"### {StepTitle(output.StepType)} ({output.Source})");
            sb.AppendLine();
            sb.AppendLine(output.Content);
        }
        sb.AppendLine();
        sb.AppendLine("## CEO 结论");
        sb.AppendLine();
        sb.AppendLine(ExtractCeoConclusion(outputs, predicted));
        sb.AppendLine();
        sb.AppendLine("## 使用声明");
        sb.AppendLine();
        sb.AppendLine("本报告只用于赛前研究和工作流测试，不提供投注、赌博或收益承诺。");
        return sb.ToString();
    }

    private static string BuildMatchReviewReport(
        WorldCupMatch match,
        WorldCupWatchObject home,
        WorldCupWatchObject away,
        BaselinePredictionRecord prediction,
        string actual,
        string predicted,
        bool hit,
        double brier)
    {
        return $"""
            # {home.DisplayName} vs {away.DisplayName} Post-Match Review

            Match: {match.Id}
            Final score: {home.DisplayName} {match.HomeScore} - {match.AwayScore} {away.DisplayName}

            ## Forecast

            - Home win: {prediction.HomeWinProbability:P1}
            - Draw: {prediction.DrawProbability:P1}
            - Away win: {prediction.AwayWinProbability:P1}
            - Predicted outcome: {predicted}
            - Actual outcome: {actual}
            - Hit: {hit}
            - Brier Score: {brier:0.000}

            ## Review Notes

            This review records the objective result and the calibration error for the baseline strategy.
            A lower Brier Score is better. The note is stored as long-term strategy memory for future reports.
            """;
    }

    private static string StepTitle(string stepType)
    {
        return stepType switch
        {
            "team_report_home" => "主队研究员",
            "team_report_away" => "客队研究员",
            "data_analysis" => "数据分析师",
            "risk_review" => "风险官",
            "ceo_summary" => "CEO 汇总",
            _ => stepType
        };
    }

    private static string OutcomeLabel(string outcome)
    {
        return outcome switch
        {
            "home_win" => "主队胜",
            "draw" => "平局",
            "away_win" => "客队胜",
            _ => outcome
        };
    }

    private static string ExtractCeoConclusion(IReadOnlyList<StepOutputDraft> outputs, string predicted)
    {
        var ceo = outputs.FirstOrDefault(output => output.StepType == "ceo_summary");
        if (ceo == null || string.IsNullOrWhiteSpace(ceo.Content))
        {
            return $"CEO 暂无单独汇总，暂以基线最高概率结论作为临时判断：{OutcomeLabel(predicted)}。";
        }

        var normalized = ceo.Content
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();
        return normalized.Length <= 260 ? normalized : normalized[..260] + "...";
    }

    private static string ResolveOutcome(int homeScore, int awayScore)
    {
        if (homeScore > awayScore) return "home_win";
        if (homeScore < awayScore) return "away_win";
        return "draw";
    }

    private static void ValidateMatchResultRequest(MatchResultRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.MatchId))
        {
            throw new ArgumentException("match_id is required.");
        }
        if (request.HomeScore < 0 || request.HomeScore > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(request.HomeScore), "home_score must be between 0 and 100.");
        }
        if (request.AwayScore < 0 || request.AwayScore > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(request.AwayScore), "away_score must be between 0 and 100.");
        }
    }

    private static string? CalculateWorkflowMemoryExpiry(string stepType)
    {
        var now = DateTime.Now;
        return stepType switch
        {
            "team_report_home" or "team_report_away" or "data_analysis" => now.AddHours(2).ToString("yyyy-MM-dd HH:mm:ss"),
            "ceo_summary" => now.AddDays(1).ToString("yyyy-MM-dd HH:mm:ss"),
            "risk_review" => null,
            _ => now.AddDays(1).ToString("yyyy-MM-dd HH:mm:ss")
        };
    }

    private static bool IsKnockoutStage(string stage)
    {
        var normalized = stage.Trim().ToLowerInvariant().Replace("-", "_", StringComparison.Ordinal).Replace(" ", "_", StringComparison.Ordinal);
        return normalized is "round_of_32"
            or "round_of_16"
            or "quarter_final"
            or "quarterfinal"
            or "semi_final"
            or "semifinal"
            or "third_place"
            or "final"
            or "knockout";
    }

    private static string ResolvePredictedOutcome(BaselinePredictionRecord prediction)
    {
        if (prediction.HomeWinProbability >= prediction.DrawProbability && prediction.HomeWinProbability >= prediction.AwayWinProbability)
        {
            return "home_win";
        }
        if (prediction.AwayWinProbability >= prediction.HomeWinProbability && prediction.AwayWinProbability >= prediction.DrawProbability)
        {
            return "away_win";
        }
        return "draw";
    }

    private static bool PredictionHasFactorPayload(string raw)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (!doc.RootElement.TryGetProperty("schema", out var schema)
                || !doc.RootElement.TryGetProperty("factors", out var factors)
                || factors.ValueKind != JsonValueKind.Array
                || !doc.RootElement.TryGetProperty("aggregate_score", out _)
                || !doc.RootElement.TryGetProperty("evidence_snapshot_ids", out var snapshotIds)
                || snapshotIds.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            var schemaName = schema.GetString();
            if (schemaName == "strategy_factors_v1")
            {
                return factors.GetArrayLength() >= 6;
            }
            if (schemaName != "strategy_factors_v2" || factors.GetArrayLength() < 8)
            {
                return false;
            }

            var factorIds = factors.EnumerateArray()
                .Select(item => item.TryGetProperty("id", out var id) ? id.GetString() : null)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            return doc.RootElement.TryGetProperty("data_quality", out _)
                && factorIds.Contains("elo_edge")
                && factorIds.Contains("recent_form_edge")
                && factorIds.Contains("fifa_points_edge");
        }
        catch
        {
            return false;
        }
    }

    private static bool PredictionHasSnapshotAwarePayload(string raw)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (!doc.RootElement.TryGetProperty("factors", out var factors) || factors.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            var hasHomeSnapshotSignal = false;
            var hasAwaySnapshotSignal = false;
            foreach (var factor in factors.EnumerateArray())
            {
                if (!factor.TryGetProperty("id", out var id)) continue;
                hasHomeSnapshotSignal |= id.GetString() == "home_snapshot_signal";
                hasAwaySnapshotSignal |= id.GetString() == "away_snapshot_signal";
            }

            return hasHomeSnapshotSignal
                && hasAwaySnapshotSignal
                && doc.RootElement.TryGetProperty("evidence_snapshot_ids", out var snapshotIds)
                && snapshotIds.ValueKind == JsonValueKind.Array;
        }
        catch
        {
            return false;
        }
    }

    private static double CalculateBrierScore(BaselinePredictionRecord prediction, string actual)
    {
        var homeActual = actual == "home_win" ? 1.0 : 0.0;
        var drawActual = actual == "draw" ? 1.0 : 0.0;
        var awayActual = actual == "away_win" ? 1.0 : 0.0;
        return Math.Pow(prediction.HomeWinProbability - homeActual, 2)
            + Math.Pow(prediction.DrawProbability - drawActual, 2)
            + Math.Pow(prediction.AwayWinProbability - awayActual, 2);
    }

    private static string Sha256(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string EscapeJson(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
    }

    private static string ReadJsonText(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out var value))
        {
            return "";
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? "",
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "TRUE",
            JsonValueKind.False => "FALSE",
            _ => ""
        };
    }

    private static int? TryReadScore(JsonElement item, string propertyName)
    {
        var raw = ReadJsonText(item, propertyName);
        return int.TryParse(raw, out var value) ? value : null;
    }

    private static string NormalizeWorldCup26Stage(string value)
    {
        if (value.Contains("group", StringComparison.OrdinalIgnoreCase)) return "group";
        if (value.Contains("round", StringComparison.OrdinalIgnoreCase)) return "round_of_16";
        if (value.Contains("quarter", StringComparison.OrdinalIgnoreCase)) return "quarter_final";
        if (value.Contains("semi", StringComparison.OrdinalIgnoreCase)) return "semi_final";
        if (value.Contains("final", StringComparison.OrdinalIgnoreCase)) return "final";
        return string.IsNullOrWhiteSpace(value) ? "group" : value.ToLowerInvariant();
    }

    private static string NormalizeFixtureDownloadStage(string roundNumber)
    {
        return roundNumber switch
        {
            "1" or "2" or "3" => "group",
            "4" => "round_of_32",
            "5" => "round_of_16",
            "6" => "quarter_final",
            "7" => "semi_final",
            "8" => "final",
            _ => "scheduled"
        };
    }

    private static string ResolveFixtureTeamObjectId(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Dictionary<string, string> teamIdByName,
        string teamName,
        string groupName,
        string now,
        WorldCupPublicDataBootstrapResult result)
    {
        if (string.IsNullOrWhiteSpace(teamName) || IsFixtureSlotName(teamName))
        {
            return "slot_tba";
        }

        if (teamIdByName.TryGetValue(teamName, out var objectId))
        {
            return objectId;
        }

        var code = ResolveFixtureDownloadFifaCode(teamName);
        if (string.IsNullOrWhiteSpace(code))
        {
            result.Notes.Add($"FixtureDownload team name could not be mapped: {teamName}");
            return "slot_tba";
        }

        objectId = $"team_{code.ToLowerInvariant()}";
        teamIdByName[teamName] = objectId;
        var normalizedName = NormalizeFixtureDownloadTeamName(teamName);
        UpsertWatchObject(connection, transaction, new WorldCupWatchObject
        {
            Id = objectId,
            Type = "football_team",
            Symbol = code,
            Name = normalizedName,
            DisplayName = normalizedName,
            Status = "active",
            MetadataJson = new JsonObject
            {
                ["source"] = "fixturedownload",
                ["fifa_code"] = code,
                ["group"] = groupName,
                ["fifa_rank"] = TryInferExistingRank(objectId)
            }.ToJsonString(),
            CreatedAt = now,
            UpdatedAt = now
        });
        result.TeamsUpserted++;

        var employeeId = $"emp_{code.ToLowerInvariant()}";
        UpsertEmployee(connection, transaction, new WorldCupEmployee
        {
            Id = employeeId,
            Name = $"{normalizedName} Team Researcher",
            Role = "team_researcher",
            Specialty = $"{normalizedName} team form, squad, tactics, news and risk monitoring",
            Status = "active",
            PromptProfile = $"You are the dedicated AI researcher for {normalizedName}. Distinguish verified facts, source signals, model inference and uncertainty.",
            ContactsJson = """["emp_data","emp_risk","emp_ceo"]""",
            CreatedAt = now,
            UpdatedAt = now
        });
        result.EmployeesUpserted++;

        UpsertAssignment(connection, transaction, new EmployeeAssignment
        {
            Id = $"assign_{code.ToLowerInvariant()}",
            EmployeeId = employeeId,
            ObjectId = objectId,
            AssignmentRole = "primary_researcher",
            Status = "active",
            StartedAt = now,
            MetadataJson = $$"""{"source":"fixturedownload_bootstrap","team_name":"{{EscapeJson(normalizedName)}}"}"""
        });
        result.AssignmentsUpserted++;
        return objectId;
    }

    private static bool IsFixtureSlotName(string value)
    {
        var text = value.Trim();
        return Regex.IsMatch(text, @"^\d+[A-L]$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            || Regex.IsMatch(text, @"^\d+[A-L]+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            || text.Equals("To be announced", StringComparison.OrdinalIgnoreCase)
            || text.Equals("TBA", StringComparison.OrdinalIgnoreCase)
            || text.Contains("winner", StringComparison.OrdinalIgnoreCase)
            || text.Contains("runner", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeFixtureDownloadTeamName(string value)
    {
        return value.Trim() switch
        {
            "Cabo Verde" => "Cape Verde",
            "Côte d'Ivoire" => "Cote d'Ivoire",
            "Curaçao" => "Curacao",
            "Czechia" => "Czech Republic",
            "IR Iran" => "Iran",
            "Korea Republic" => "South Korea",
            "Türkiye" => "Turkey",
            "USA" => "United States",
            _ => value.Trim()
        };
    }

    private static string ResolveFixtureDownloadFifaCode(string value)
    {
        var normalized = NormalizeFixtureDownloadTeamName(value);
        return normalized switch
        {
            "Algeria" => "ALG",
            "Angola" => "ANG",
            "Argentina" => "ARG",
            "Australia" => "AUS",
            "Austria" => "AUT",
            "Belgium" => "BEL",
            "Bosnia and Herzegovina" => "BIH",
            "Brazil" => "BRA",
            "Cameroon" => "CMR",
            "Canada" => "CAN",
            "Cape Verde" => "CPV",
            "Colombia" => "COL",
            "Congo DR" => "COD",
            "Costa Rica" => "CRC",
            "Cote d'Ivoire" => "CIV",
            "Croatia" => "CRO",
            "Curacao" => "CUW",
            "Czech Republic" => "CZE",
            "Denmark" => "DEN",
            "Ecuador" => "ECU",
            "Egypt" => "EGY",
            "England" => "ENG",
            "France" => "FRA",
            "Germany" => "GER",
            "Ghana" => "GHA",
            "Haiti" => "HAI",
            "Iran" => "IRN",
            "Iraq" => "IRQ",
            "Italy" => "ITA",
            "Japan" => "JPN",
            "Jordan" => "JOR",
            "Mexico" => "MEX",
            "Morocco" => "MAR",
            "Netherlands" => "NED",
            "New Zealand" => "NZL",
            "Norway" => "NOR",
            "Panama" => "PAN",
            "Paraguay" => "PAR",
            "Portugal" => "POR",
            "Qatar" => "QAT",
            "Saudi Arabia" => "KSA",
            "Scotland" => "SCO",
            "Senegal" => "SEN",
            "South Africa" => "RSA",
            "South Korea" => "KOR",
            "Spain" => "ESP",
            "Sweden" => "SWE",
            "Switzerland" => "SUI",
            "Tunisia" => "TUN",
            "Turkey" => "TUR",
            "United States" => "USA",
            "Uruguay" => "URU",
            "Uzbekistan" => "UZB",
            _ => ""
        };
    }

    private static string BuildProviderPayload(string provider, string endpoint, JsonElement payload)
    {
        return new JsonObject
        {
            ["provider"] = provider,
            ["endpoint"] = endpoint,
            ["payload"] = JsonNode.Parse(payload.GetRawText())
        }.ToJsonString();
    }

    private static int TryInferExistingRank(string objectId)
    {
        return objectId switch
        {
            "team_arg" => 1,
            "team_fra" => 2,
            "team_bra" => 3,
            "team_eng" => 4,
            "team_esp" => 5,
            "team_ger" => 6,
            "team_usa" => 16,
            "team_jpn" => 18,
            _ => 50
        };
    }

    private void SaveMemory(MemoryRecord item)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO memories (id, scope, owner_id, object_id, memory_type, content, summary, tags_json,
                importance, confidence, source_type, source_id, content_hash, valid_from, expires_at, contradicted_by_memory_id,
                review_status, created_at, updated_at, last_used_at)
            VALUES ($id, $scope, $owner_id, $object_id, $memory_type, $content, $summary, $tags_json,
                $importance, $confidence, $source_type, $source_id, $content_hash, $valid_from, $expires_at, $contradicted_by_memory_id,
                $review_status, $created_at, $updated_at, $last_used_at)
            ON CONFLICT(id) DO UPDATE SET
                scope = excluded.scope,
                owner_id = excluded.owner_id,
                object_id = excluded.object_id,
                memory_type = excluded.memory_type,
                content = excluded.content,
                summary = excluded.summary,
                tags_json = excluded.tags_json,
                importance = excluded.importance,
                confidence = excluded.confidence,
                source_type = excluded.source_type,
                source_id = excluded.source_id,
                content_hash = excluded.content_hash,
                valid_from = excluded.valid_from,
                expires_at = excluded.expires_at,
                contradicted_by_memory_id = excluded.contradicted_by_memory_id,
                review_status = excluded.review_status,
                updated_at = excluded.updated_at,
                last_used_at = excluded.last_used_at
            """;
        Add(command, "$id", item.Id);
        Add(command, "$scope", item.Scope);
        Add(command, "$owner_id", item.OwnerId);
        Add(command, "$object_id", item.ObjectId);
        Add(command, "$memory_type", item.MemoryType);
        Add(command, "$content", item.Content);
        Add(command, "$summary", item.Summary);
        Add(command, "$tags_json", item.TagsJson);
        Add(command, "$importance", item.Importance);
        Add(command, "$confidence", item.Confidence);
        Add(command, "$source_type", item.SourceType);
        Add(command, "$source_id", item.SourceId);
        Add(command, "$content_hash", Sha256(item.Content));
        Add(command, "$valid_from", item.ValidFrom);
        Add(command, "$expires_at", item.ExpiresAt);
        Add(command, "$contradicted_by_memory_id", item.ContradictedByMemoryId);
        Add(command, "$review_status", item.ReviewStatus);
        Add(command, "$created_at", item.CreatedAt);
        Add(command, "$updated_at", item.UpdatedAt);
        Add(command, "$last_used_at", item.LastUsedAt);
        command.ExecuteNonQuery();
    }

    private MemoryRecord? FindMemoryByContentHash(string sourceType, string? sourceId, string? objectId, string? ownerId, string contentHash)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, scope, owner_id, object_id, memory_type, content, summary, tags_json, importance,
                   confidence, source_type, source_id, valid_from, expires_at, contradicted_by_memory_id,
                   review_status, created_at, updated_at, last_used_at
            FROM memories
            WHERE source_type = $source_type
              AND (($source_id IS NULL AND source_id IS NULL) OR source_id = $source_id)
              AND (($object_id IS NULL AND object_id IS NULL) OR object_id = $object_id)
              AND (($owner_id IS NULL AND owner_id IS NULL) OR owner_id = $owner_id)
              AND content_hash = $content_hash
            LIMIT 1
            """;
        Add(command, "$source_type", sourceType);
        Add(command, "$source_id", sourceId);
        Add(command, "$object_id", objectId);
        Add(command, "$owner_id", ownerId);
        Add(command, "$content_hash", contentHash);
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadMemory(reader) : null;
    }

    private static MemoryRecord ReadMemory(SqliteDataReader reader)
    {
        return new MemoryRecord
        {
            Id = reader.GetString(0),
            Scope = reader.GetString(1),
            OwnerId = reader.IsDBNull(2) ? null : reader.GetString(2),
            ObjectId = reader.IsDBNull(3) ? null : reader.GetString(3),
            MemoryType = reader.GetString(4),
            Content = reader.GetString(5),
            Summary = reader.GetString(6),
            TagsJson = reader.GetString(7),
            Importance = reader.GetDouble(8),
            Confidence = reader.GetDouble(9),
            SourceType = reader.GetString(10),
            SourceId = reader.IsDBNull(11) ? null : reader.GetString(11),
            ValidFrom = reader.GetString(12),
            ExpiresAt = reader.IsDBNull(13) ? null : reader.GetString(13),
            ContradictedByMemoryId = reader.IsDBNull(14) ? null : reader.GetString(14),
            ReviewStatus = reader.GetString(15),
            CreatedAt = reader.GetString(16),
            UpdatedAt = reader.GetString(17),
            LastUsedAt = reader.IsDBNull(18) ? null : reader.GetString(18)
        };
    }

    private static DataSnapshotRecord ReadDataSnapshot(SqliteDataReader reader)
    {
        return new DataSnapshotRecord
        {
            Id = reader.GetString(0),
            Source = reader.GetString(1),
            SnapshotType = reader.GetString(2),
            ObjectId = reader.IsDBNull(3) ? null : reader.GetString(3),
            MatchId = reader.IsDBNull(4) ? null : reader.GetString(4),
            ContentJson = reader.GetString(5),
            ContentHash = reader.GetString(6),
            CapturedAt = reader.GetString(7)
        };
    }

    private static IntelligenceSignalRecord ReadIntelligenceSignal(SqliteDataReader reader)
    {
        return new IntelligenceSignalRecord
        {
            Id = reader.GetString(0),
            SourceSnapshotId = reader.GetString(1),
            SignalType = reader.GetString(2),
            Severity = reader.GetString(3),
            Confidence = reader.GetDouble(4),
            ObjectId = reader.IsDBNull(5) ? null : reader.GetString(5),
            MatchId = reader.IsDBNull(6) ? null : reader.GetString(6),
            Title = reader.GetString(7),
            Summary = reader.GetString(8),
            EvidenceJson = reader.GetString(9),
            Status = reader.GetString(10),
            ContentHash = reader.GetString(11),
            CreatedAt = reader.GetString(12),
            UpdatedAt = reader.GetString(13)
        };
    }

    private static WorldCupSystemEventLog ReadSystemEventLog(SqliteDataReader reader)
    {
        return new WorldCupSystemEventLog
        {
            Id = reader.GetString(0),
            EventType = reader.GetString(1),
            Category = reader.GetString(2),
            Severity = reader.GetString(3),
            Source = reader.GetString(4),
            EmployeeId = reader.IsDBNull(5) ? null : reader.GetString(5),
            ObjectId = reader.IsDBNull(6) ? null : reader.GetString(6),
            MatchId = reader.IsDBNull(7) ? null : reader.GetString(7),
            WorkflowRunId = reader.IsDBNull(8) ? null : reader.GetString(8),
            LlmCallId = reader.IsDBNull(9) ? null : reader.GetString(9),
            SnapshotId = reader.IsDBNull(10) ? null : reader.GetString(10),
            ArtifactId = reader.IsDBNull(11) ? null : reader.GetString(11),
            Title = reader.GetString(12),
            Message = reader.GetString(13),
            PayloadJson = reader.GetString(14),
            ContentHash = reader.GetString(15),
            CreatedAt = reader.GetString(16)
        };
    }

    private static void MarkMemoryUsed(SqliteConnection connection, string memoryId, string? employeeId, string? matchId, string usedAt)
    {
        using var update = connection.CreateCommand();
        update.CommandText = "UPDATE memories SET last_used_at = $used_at WHERE id = $id";
        Add(update, "$used_at", usedAt);
        Add(update, "$id", memoryId);
        update.ExecuteNonQuery();

        using var insert = connection.CreateCommand();
        insert.CommandText = """
            INSERT INTO memory_access_logs (id, memory_id, employee_id, match_id, used_at)
            VALUES ($id, $memory_id, $employee_id, $match_id, $used_at)
            """;
        Add(insert, "$id", $"memory_access_{Guid.NewGuid():N}");
        Add(insert, "$memory_id", memoryId);
        Add(insert, "$employee_id", employeeId);
        Add(insert, "$match_id", matchId);
        Add(insert, "$used_at", usedAt);
        insert.ExecuteNonQuery();
    }

    private static string Summarize(string content)
    {
        var normalized = content
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();
        return normalized.Length <= 180 ? normalized : normalized[..180] + "...";
    }

    private static List<MemoryRecord> DeduplicateMemories(IEnumerable<MemoryRecord> memories)
    {
        return memories
            .GroupBy(memory => $"{memory.SourceType}|{memory.SourceId}|{memory.ObjectId}|{memory.OwnerId}|{Sha256(memory.Content)}")
            .Select(group => group
                .OrderByDescending(memory => memory.Importance)
                .ThenByDescending(memory => memory.UpdatedAt)
                .First())
            .ToList();
    }

    private static WorkflowRunRecord ReadWorkflowRun(SqliteDataReader reader)
    {
        return new WorkflowRunRecord
        {
            Id = reader.GetString(0),
            WorkflowType = reader.GetString(1),
            Status = reader.GetString(2),
            ObjectId = reader.IsDBNull(3) ? null : reader.GetString(3),
            MatchId = reader.IsDBNull(4) ? null : reader.GetString(4),
            StartedBy = reader.GetString(5),
            StartedAt = reader.GetString(6),
            CompletedAt = reader.IsDBNull(7) ? null : reader.GetString(7),
            ErrorMessage = reader.IsDBNull(8) ? null : reader.GetString(8),
            MetadataJson = reader.GetString(9)
        };
    }

    private static WorkflowStepRecord ReadWorkflowStep(SqliteDataReader reader)
    {
        return new WorkflowStepRecord
        {
            Id = reader.GetString(0),
            WorkflowRunId = reader.GetString(1),
            StepType = reader.GetString(2),
            Status = reader.GetString(3),
            AssigneeEmployeeId = reader.IsDBNull(4) ? null : reader.GetString(4),
            StartedAt = reader.IsDBNull(5) ? null : reader.GetString(5),
            CompletedAt = reader.IsDBNull(6) ? null : reader.GetString(6),
            InputJson = reader.GetString(7),
            OutputJson = reader.GetString(8),
            ArtifactId = reader.IsDBNull(9) ? null : reader.GetString(9),
            ErrorMessage = reader.IsDBNull(10) ? null : reader.GetString(10)
        };
    }

    private static ArtifactRecord ReadArtifact(SqliteDataReader reader)
    {
        return new ArtifactRecord
        {
            Id = reader.GetString(0),
            Type = reader.GetString(1),
            Title = reader.GetString(2),
            OwnerEmployeeId = reader.IsDBNull(3) ? null : reader.GetString(3),
            ObjectId = reader.IsDBNull(4) ? null : reader.GetString(4),
            WorkflowRunId = reader.IsDBNull(5) ? null : reader.GetString(5),
            FilePath = reader.GetString(6),
            Summary = reader.GetString(7),
            MimeType = reader.GetString(8),
            ContentHash = reader.GetString(9),
            Version = reader.GetInt32(10),
            MetadataJson = reader.GetString(11),
            ParentArtifactId = reader.IsDBNull(12) ? null : reader.GetString(12),
            CreatedAt = reader.GetString(13)
        };
    }

    private static WorldCupMatch? GetMatch(SqliteConnection connection, string matchId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, stage, group_name, home_object_id, away_object_id, kickoff_time, venue, status, home_score, away_score
            FROM matches
            WHERE id = $id
            """;
        Add(command, "$id", matchId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadMatch(reader) : null;
    }

    private static WorldCupWatchObject? GetWatchObject(SqliteConnection connection, string objectId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, type, symbol, name, display_name, status, metadata_json, created_at, updated_at
            FROM watch_objects
            WHERE id = $id
            """;
        Add(command, "$id", objectId);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        return new WorldCupWatchObject
        {
            Id = reader.GetString(0),
            Type = reader.GetString(1),
            Symbol = reader.GetString(2),
            Name = reader.GetString(3),
            DisplayName = reader.GetString(4),
            Status = reader.GetString(5),
            MetadataJson = reader.GetString(6),
            CreatedAt = reader.GetString(7),
            UpdatedAt = reader.GetString(8)
        };
    }

    private static WorldCupEmployee? GetEmployee(SqliteConnection connection, string employeeId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, name, role, specialty, status, prompt_profile, model_index, contacts_json, created_at, updated_at
            FROM employees
            WHERE id = $id
            """;
        Add(command, "$id", employeeId);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        return new WorldCupEmployee
        {
            Id = reader.GetString(0),
            Name = reader.GetString(1),
            Role = reader.GetString(2),
            Specialty = reader.GetString(3),
            Status = reader.GetString(4),
            PromptProfile = reader.GetString(5),
            ModelIndex = reader.GetInt32(6),
            ContactsJson = reader.GetString(7),
            CreatedAt = reader.GetString(8),
            UpdatedAt = reader.GetString(9)
        };
    }

    private static WorldCupMatch ReadMatch(SqliteDataReader reader)
    {
        return new WorldCupMatch
        {
            Id = reader.GetString(0),
            Stage = reader.GetString(1),
            GroupName = reader.GetString(2),
            HomeObjectId = reader.GetString(3),
            AwayObjectId = reader.GetString(4),
            KickoffTime = reader.GetString(5),
            Venue = reader.GetString(6),
            Status = reader.GetString(7),
            HomeScore = reader.IsDBNull(8) ? null : reader.GetInt32(8),
            AwayScore = reader.IsDBNull(9) ? null : reader.GetInt32(9)
        };
    }

    private static void Add(SqliteCommand command, string name, object? value)
    {
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);
    }

}

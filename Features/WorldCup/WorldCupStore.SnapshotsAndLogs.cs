using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace PiPiClaw.Team;

public sealed partial class WorldCupStore
{
    public List<DataSnapshotRecord> GetDataSnapshots(string? matchId = null, string? objectId = null, string? snapshotType = null)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        var filters = new List<string> { "1 = 1" };
        if (!string.IsNullOrWhiteSpace(matchId))
        {
            filters.Add("match_id = $match_id");
            Add(command, "$match_id", matchId);
        }
        if (!string.IsNullOrWhiteSpace(objectId))
        {
            filters.Add("object_id = $object_id");
            Add(command, "$object_id", objectId);
        }
        if (!string.IsNullOrWhiteSpace(snapshotType))
        {
            filters.Add("snapshot_type = $snapshot_type");
            Add(command, "$snapshot_type", snapshotType);
        }

        command.CommandText = $"""
            SELECT id, source, snapshot_type, object_id, match_id, content_json, content_hash, captured_at
            FROM data_snapshots
            WHERE {string.Join(" AND ", filters)}
            ORDER BY captured_at DESC, id
            """;
        using var reader = command.ExecuteReader();
        var rows = new List<DataSnapshotRecord>();
        while (reader.Read())
        {
            rows.Add(ReadDataSnapshot(reader));
        }
        return rows;
    }

    public List<WorldCupSystemEventLog> GetSystemEventLogs(
        string? category = null,
        string? eventType = null,
        string? matchId = null,
        string? objectId = null,
        string? employeeId = null,
        int limit = 200)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        var filters = new List<string> { "1 = 1" };
        if (!string.IsNullOrWhiteSpace(category))
        {
            filters.Add("category = $category");
            Add(command, "$category", category);
        }
        if (!string.IsNullOrWhiteSpace(eventType))
        {
            filters.Add("event_type = $event_type");
            Add(command, "$event_type", eventType);
        }
        if (!string.IsNullOrWhiteSpace(matchId))
        {
            filters.Add("match_id = $match_id");
            Add(command, "$match_id", matchId);
        }
        if (!string.IsNullOrWhiteSpace(objectId))
        {
            filters.Add("object_id = $object_id");
            Add(command, "$object_id", objectId);
        }
        if (!string.IsNullOrWhiteSpace(employeeId))
        {
            filters.Add("employee_id = $employee_id");
            Add(command, "$employee_id", employeeId);
        }

        command.CommandText = $"""
            SELECT id, event_type, category, severity, source, employee_id, object_id, match_id,
                   workflow_run_id, llm_call_id, snapshot_id, artifact_id, title, message,
                   payload_json, content_hash, created_at
            FROM system_event_logs
            WHERE {string.Join(" AND ", filters)}
            ORDER BY created_at DESC, id DESC
            LIMIT $limit
            """;
        Add(command, "$limit", Math.Clamp(limit, 1, 1000));
        using var reader = command.ExecuteReader();
        var rows = new List<WorldCupSystemEventLog>();
        while (reader.Read())
        {
            rows.Add(ReadSystemEventLog(reader));
        }
        return rows;
    }

    public void AddSystemEventLog(WorldCupSystemEventLog item)
    {
        using var connection = OpenConnection();
        SaveSystemEventLog(connection, null, PrepareSystemEventLog(item));
    }

    public WorldCupSystemEventLogHarnessResult RunSystemEventLogHarness()
    {
        SeedDemoWorldCupCompany();
        var marker = $"event_harness_{Guid.NewGuid():N}";
        AddSystemEventLog(new WorldCupSystemEventLog
        {
            EventType = "harness_event",
            Category = "harness",
            Source = "system_event_log_harness",
            EmployeeId = "emp_data",
            ObjectId = "team_arg",
            MatchId = "match_arg_jpn",
            Title = "System event harness marker",
            Message = marker,
            PayloadJson = $$"""{"marker":"{{marker}}","purpose":"system_event_log_harness"}"""
        });

        var recalled = GetSystemEventLogs(eventType: "harness_event", limit: 20)
            .Any(item => item.Message.Contains(marker, StringComparison.Ordinal));
        var categoryFiltered = GetSystemEventLogs(category: "harness", limit: 20)
            .Any(item => item.Message.Contains(marker, StringComparison.Ordinal));
        var entityFiltered = GetSystemEventLogs(matchId: "match_arg_jpn", objectId: "team_arg", employeeId: "emp_data", limit: 20)
            .Any(item => item.Message.Contains(marker, StringComparison.Ordinal));

        RunMockMatchPredictionWorkflow("match_arg_jpn");
        var workflowEvents = GetSystemEventLogs(category: "workflow", matchId: "match_arg_jpn", limit: 20)
            .Any(item => item.EventType == "workflow_completed");
        var employeeEvents = GetSystemEventLogs(category: "employee", matchId: "match_arg_jpn", limit: 20)
            .Count(item => item.EventType == "workflow_step_completed") >= 5;

        var result = new WorldCupSystemEventLogHarnessResult
        {
            EventWritten = true,
            EventRecalled = recalled,
            CategoryFilterWorks = categoryFiltered,
            EntityFilterWorks = entityFiltered,
            WorkflowEventsWritten = workflowEvents,
            EmployeeEventsWritten = employeeEvents
        };

        if (!result.EventRecalled) result.Notes.Add("Harness event was not recalled by event_type.");
        if (!result.CategoryFilterWorks) result.Notes.Add("Harness event was not recalled by category.");
        if (!result.EntityFilterWorks) result.Notes.Add("Harness event was not recalled by match/object/employee filters.");
        if (!result.WorkflowEventsWritten) result.Notes.Add("Workflow completion event was not written.");
        if (!result.EmployeeEventsWritten) result.Notes.Add("Workflow employee step events were not written.");
        result.Passed = result.EventWritten
            && result.EventRecalled
            && result.CategoryFilterWorks
            && result.EntityFilterWorks
            && result.WorkflowEventsWritten
            && result.EmployeeEventsWritten;
        return result;
    }
}

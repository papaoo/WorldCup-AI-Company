using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace PiPiClaw.Team;

public sealed partial class WorldCupStore
{
    public List<MemoryRecord> GetMemories(string? objectId = null, string? ownerId = null)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        var filters = new List<string> { "review_status != 'rejected'", "(expires_at IS NULL OR expires_at = '' OR expires_at > $now)" };
        Add(command, "$now", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        if (!string.IsNullOrWhiteSpace(objectId))
        {
            filters.Add("object_id = $object_id");
            Add(command, "$object_id", objectId);
        }
        if (!string.IsNullOrWhiteSpace(ownerId))
        {
            filters.Add("owner_id = $owner_id");
            Add(command, "$owner_id", ownerId);
        }

        command.CommandText = $"""
            SELECT id, scope, owner_id, object_id, memory_type, content, summary, tags_json, importance,
                   confidence, source_type, source_id, valid_from, expires_at, contradicted_by_memory_id,
                   review_status, created_at, updated_at, last_used_at
            FROM memories
            WHERE {string.Join(" AND ", filters)}
            ORDER BY importance DESC, created_at DESC
            """;
        using var reader = command.ExecuteReader();
        var rows = new List<MemoryRecord>();
        while (reader.Read())
        {
            rows.Add(ReadMemory(reader));
        }
        return DeduplicateMemories(rows);
    }

    public MemoryRecord AddMemory(MemoryCreateRequest request)
    {
        var contentHash = Sha256(request.Content);
        var existing = FindMemoryByContentHash(request.SourceType, request.SourceId, request.ObjectId, request.OwnerId, contentHash);
        if (existing != null) return existing;

        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        var memory = new MemoryRecord
        {
            Id = $"mem_{Guid.NewGuid():N}",
            Scope = request.Scope,
            OwnerId = request.OwnerId,
            ObjectId = request.ObjectId,
            MemoryType = request.MemoryType,
            Content = request.Content,
            Summary = string.IsNullOrWhiteSpace(request.Summary) ? Summarize(request.Content) : request.Summary,
            TagsJson = request.TagsJson,
            Importance = Math.Clamp(request.Importance, 0, 1),
            Confidence = Math.Clamp(request.Confidence, 0, 1),
            SourceType = request.SourceType,
            SourceId = request.SourceId,
            ExpiresAt = request.ExpiresAt,
            ReviewStatus = "approved",
            ValidFrom = now,
            CreatedAt = now,
            UpdatedAt = now
        };
        SaveMemory(memory);
        return memory;
    }

    public string BuildMemoryContext(string? employeeId, string? objectId, string? matchId)
    {
        var memories = RecallRelevantMemories(employeeId, objectId, 8);
        if (memories.Count == 0) return "\u6682\u65e0\u76f8\u5173\u957f\u671f\u8bb0\u5fc6\u3002";

        using var connection = OpenConnection();
        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        foreach (var memory in memories)
        {
            MarkMemoryUsed(connection, memory.Id, employeeId, matchId, now);
        }

        var sb = new StringBuilder();
        foreach (var memory in memories)
        {
            sb.AppendLine($"- [{memory.MemoryType}, importance {memory.Importance:0.0}, confidence {memory.Confidence:0.0}] {memory.Summary}");
        }
        return sb.ToString().Trim();
    }

    public List<MemoryRecord> RecallRelevantMemories(string? employeeId, string? objectId, int limit = 8)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, scope, owner_id, object_id, memory_type, content, summary, tags_json, importance,
                   confidence, source_type, source_id, valid_from, expires_at, contradicted_by_memory_id,
                   review_status, created_at, updated_at, last_used_at
            FROM memories
            WHERE review_status = 'approved'
              AND contradicted_by_memory_id IS NULL
              AND (expires_at IS NULL OR expires_at = '' OR expires_at > $now)
              AND (
                    ($employee_id IS NOT NULL AND owner_id = $employee_id)
                    OR ($object_id IS NOT NULL AND object_id = $object_id)
                    OR scope = 'user'
                    OR scope = 'strategy'
                  )
            ORDER BY importance DESC, confidence DESC, created_at DESC
            LIMIT $limit
            """;
        Add(command, "$now", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        Add(command, "$employee_id", employeeId);
        Add(command, "$object_id", objectId);
        Add(command, "$limit", limit);
        using var reader = command.ExecuteReader();
        var rows = new List<MemoryRecord>();
        while (reader.Read())
        {
            rows.Add(ReadMemory(reader));
        }
        return DeduplicateMemories(rows);
    }

    public void AddWorkflowMemories(MatchWorkflowResult workflow, IReadOnlyList<StepOutputDraft> outputs)
    {
        foreach (var output in outputs)
        {
            if (string.IsNullOrWhiteSpace(output.Content)) continue;
            AddMemory(new MemoryCreateRequest
            {
                Scope = "employee",
                OwnerId = output.EmployeeId,
                MemoryType = "episode",
                Content = output.Content,
                Summary = $"{output.StepType} \u4ea7\u51fa\uff1a{Summarize(output.Content)}",
                SourceType = "workflow_step",
                SourceId = workflow.WorkflowRun.Id,
                ExpiresAt = CalculateWorkflowMemoryExpiry(output.StepType),
                Importance = output.StepType == "ceo_summary" ? 0.75 : 0.55,
                Confidence = output.Source == "llm" ? 0.65 : 0.45
            });
        }

        if (!string.IsNullOrWhiteSpace(workflow.WorkflowRun.MatchId))
        {
            AddMemory(new MemoryCreateRequest
            {
                Scope = "workflow",
                MemoryType = "episode",
                Content = workflow.Artifact.Summary,
                Summary = $"\u6bd4\u8d5b {workflow.WorkflowRun.MatchId} \u5df2\u5b8c\u6210\u8d5b\u524d\u9884\u6d4b\u5de5\u4f5c\u6d41\uff1a{workflow.Artifact.Summary}",
                SourceType = "artifact",
                SourceId = workflow.Artifact.Id,
                ExpiresAt = DateTime.Now.AddDays(1).ToString("yyyy-MM-dd HH:mm:ss"),
                Importance = 0.65,
                Confidence = 0.7
            });
        }
    }

    public MemoryHarnessResult RunMemoryHarness()
    {
        var result = new MemoryHarnessResult();
        var active = AddMemory(new MemoryCreateRequest
        {
            Scope = "object",
            ObjectId = "team_arg",
            MemoryType = "episode",
            Content = "\u963f\u6839\u5ef7\u961f\u5728\u5f3a\u5f31\u5206\u660e\u573a\u6b21\u4e2d\u4ecd\u9700\u5173\u6ce8\u6162\u70ed\u98ce\u9669\uff0cCEO \u504f\u597d\u5355\u72ec\u63d0\u793a\u51b7\u95e8\u98ce\u9669\u3002",
            Summary = "\u963f\u6839\u5ef7\u5f3a\u961f\u573a\u6b21\u9700\u8981\u63d0\u793a\u6162\u70ed\u548c\u51b7\u95e8\u98ce\u9669\u3002",
            SourceType = "memory_harness",
            SourceId = "memory_harness_active",
            Importance = 0.9,
            Confidence = 0.8
        });
        result.MemoryCreated = !string.IsNullOrWhiteSpace(active.Id);

        AddMemory(new MemoryCreateRequest
        {
            Scope = "object",
            ObjectId = "team_arg",
            MemoryType = "episode",
            Content = "\u8fd9\u662f\u4e00\u6761\u5df2\u8fc7\u671f\u7684\u6d4b\u8bd5\u8bb0\u5fc6\uff0c\u4e0d\u5e94\u8be5\u88ab\u53ec\u56de\u3002",
            Summary = "\u8fc7\u671f\u6d4b\u8bd5\u8bb0\u5fc6",
            SourceType = "memory_harness",
            SourceId = "memory_harness_expired",
            ExpiresAt = "2000-01-01 00:00:00",
            Importance = 1,
            Confidence = 1
        });

        var recalled = RecallRelevantMemories("emp_arg", "team_arg", 12);
        result.MemoryRecalled = recalled.Any(m => m.Id == active.Id);
        result.ExpiredMemoryFiltered = recalled.All(m => m.Summary != "\u8fc7\u671f\u6d4b\u8bd5\u8bb0\u5fc6");
        var context = BuildMemoryContext("emp_arg", "team_arg", "match_arg_jpn");
        result.ContextContainsMemory = context.Contains("\u6162\u70ed", StringComparison.Ordinal);

        if (!result.MemoryCreated) result.Notes.Add("Memory was not created.");
        if (!result.MemoryRecalled) result.Notes.Add("Active memory was not recalled.");
        if (!result.ExpiredMemoryFiltered) result.Notes.Add("Expired memory was recalled.");
        if (!result.ContextContainsMemory) result.Notes.Add("Context did not include expected memory.");
        result.Passed = result.MemoryCreated && result.MemoryRecalled && result.ExpiredMemoryFiltered && result.ContextContainsMemory;
        return result;
    }
}

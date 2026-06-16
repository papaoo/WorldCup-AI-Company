namespace PiPiClaw.Team;

public sealed partial class WorldCupStore
{
    private const string SchemaSql = """
        PRAGMA foreign_keys = ON;
        PRAGMA journal_mode = WAL;

        CREATE TABLE IF NOT EXISTS watch_objects (
            id TEXT PRIMARY KEY,
            type TEXT NOT NULL,
            symbol TEXT NOT NULL,
            name TEXT NOT NULL,
            display_name TEXT NOT NULL,
            status TEXT NOT NULL,
            metadata_json TEXT NOT NULL DEFAULT '{}',
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS employees (
            id TEXT PRIMARY KEY,
            name TEXT NOT NULL,
            role TEXT NOT NULL,
            specialty TEXT NOT NULL,
            status TEXT NOT NULL,
            prompt_profile TEXT NOT NULL,
            model_index INTEGER NOT NULL DEFAULT 0,
            contacts_json TEXT NOT NULL DEFAULT '[]',
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS employee_assignments (
            id TEXT PRIMARY KEY,
            employee_id TEXT NOT NULL,
            object_id TEXT NOT NULL,
            assignment_role TEXT NOT NULL,
            status TEXT NOT NULL,
            started_at TEXT NOT NULL,
            ended_at TEXT,
            metadata_json TEXT NOT NULL DEFAULT '{}',
            FOREIGN KEY(employee_id) REFERENCES employees(id),
            FOREIGN KEY(object_id) REFERENCES watch_objects(id)
        );

        CREATE TABLE IF NOT EXISTS strategies (
            id TEXT PRIMARY KEY,
            name TEXT NOT NULL,
            domain TEXT NOT NULL,
            version TEXT NOT NULL,
            description TEXT NOT NULL,
            enabled INTEGER NOT NULL DEFAULT 1,
            config_json TEXT NOT NULL DEFAULT '{}'
        );

        CREATE TABLE IF NOT EXISTS strategy_runs (
            id TEXT PRIMARY KEY,
            strategy_id TEXT NOT NULL,
            object_id TEXT,
            match_id TEXT,
            input_snapshot_id TEXT,
            output_json TEXT NOT NULL DEFAULT '{}',
            metrics_json TEXT NOT NULL DEFAULT '{}',
            created_at TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS workflow_runs (
            id TEXT PRIMARY KEY,
            workflow_type TEXT NOT NULL,
            status TEXT NOT NULL,
            object_id TEXT,
            match_id TEXT,
            started_by TEXT NOT NULL,
            started_at TEXT NOT NULL,
            completed_at TEXT,
            error_message TEXT,
            metadata_json TEXT NOT NULL DEFAULT '{}'
        );

        CREATE TABLE IF NOT EXISTS workflow_steps (
            id TEXT PRIMARY KEY,
            workflow_run_id TEXT NOT NULL,
            step_type TEXT NOT NULL,
            status TEXT NOT NULL,
            assignee_employee_id TEXT,
            started_at TEXT,
            completed_at TEXT,
            input_json TEXT NOT NULL DEFAULT '{}',
            output_json TEXT NOT NULL DEFAULT '{}',
            artifact_id TEXT,
            error_message TEXT,
            FOREIGN KEY(workflow_run_id) REFERENCES workflow_runs(id)
        );

        CREATE TABLE IF NOT EXISTS agent_tasks (
            id TEXT PRIMARY KEY,
            workflow_run_id TEXT,
            workflow_step_id TEXT,
            employee_id TEXT NOT NULL,
            title TEXT NOT NULL,
            task_prompt TEXT NOT NULL,
            status TEXT NOT NULL,
            result_artifact_id TEXT,
            created_at TEXT NOT NULL,
            started_at TEXT,
            completed_at TEXT
        );

        CREATE TABLE IF NOT EXISTS llm_calls (
            id TEXT PRIMARY KEY,
            agent_task_id TEXT,
            employee_id TEXT,
            model_name TEXT NOT NULL,
            provider TEXT NOT NULL,
            prompt_version TEXT NOT NULL,
            prompt_tokens INTEGER NOT NULL DEFAULT 0,
            completion_tokens INTEGER NOT NULL DEFAULT 0,
            cost_estimate REAL NOT NULL DEFAULT 0,
            request_hash TEXT NOT NULL DEFAULT '',
            response_hash TEXT NOT NULL DEFAULT '',
            status TEXT NOT NULL,
            error_message TEXT,
            created_at TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS data_snapshots (
            id TEXT PRIMARY KEY,
            source TEXT NOT NULL,
            snapshot_type TEXT NOT NULL,
            object_id TEXT,
            match_id TEXT,
            content_json TEXT NOT NULL,
            content_hash TEXT NOT NULL,
            captured_at TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS intelligence_signals (
            id TEXT PRIMARY KEY,
            source_snapshot_id TEXT NOT NULL,
            signal_type TEXT NOT NULL,
            severity TEXT NOT NULL,
            confidence REAL NOT NULL,
            object_id TEXT,
            match_id TEXT,
            title TEXT NOT NULL,
            summary TEXT NOT NULL,
            evidence_json TEXT NOT NULL DEFAULT '{}',
            status TEXT NOT NULL,
            content_hash TEXT NOT NULL,
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS artifacts (
            id TEXT PRIMARY KEY,
            type TEXT NOT NULL,
            title TEXT NOT NULL,
            owner_employee_id TEXT,
            object_id TEXT,
            workflow_run_id TEXT,
            file_path TEXT NOT NULL,
            summary TEXT NOT NULL DEFAULT '',
            mime_type TEXT NOT NULL DEFAULT 'text/plain',
            content_hash TEXT NOT NULL DEFAULT '',
            version INTEGER NOT NULL DEFAULT 1,
            metadata_json TEXT NOT NULL DEFAULT '{}',
            parent_artifact_id TEXT,
            created_at TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS memories (
            id TEXT PRIMARY KEY,
            scope TEXT NOT NULL,
            owner_id TEXT,
            object_id TEXT,
            memory_type TEXT NOT NULL,
            content TEXT NOT NULL,
            summary TEXT NOT NULL,
            tags_json TEXT NOT NULL DEFAULT '[]',
            importance REAL NOT NULL DEFAULT 0.5,
            confidence REAL NOT NULL DEFAULT 0.5,
            source_type TEXT NOT NULL,
            source_id TEXT,
            content_hash TEXT NOT NULL DEFAULT '',
            valid_from TEXT NOT NULL,
            expires_at TEXT,
            contradicted_by_memory_id TEXT,
            review_status TEXT NOT NULL DEFAULT 'approved',
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL,
            last_used_at TEXT
        );

        CREATE TABLE IF NOT EXISTS system_event_logs (
            id TEXT PRIMARY KEY,
            event_type TEXT NOT NULL,
            category TEXT NOT NULL,
            severity TEXT NOT NULL,
            source TEXT NOT NULL,
            employee_id TEXT,
            object_id TEXT,
            match_id TEXT,
            workflow_run_id TEXT,
            llm_call_id TEXT,
            snapshot_id TEXT,
            artifact_id TEXT,
            title TEXT NOT NULL,
            message TEXT NOT NULL,
            payload_json TEXT NOT NULL DEFAULT '{}',
            content_hash TEXT NOT NULL DEFAULT '',
            created_at TEXT NOT NULL
        );

        CREATE INDEX IF NOT EXISTS idx_memories_object ON memories(object_id, review_status, importance);
        CREATE INDEX IF NOT EXISTS idx_memories_owner ON memories(owner_id, review_status, importance);
        CREATE INDEX IF NOT EXISTS idx_memories_content_hash ON memories(source_type, source_id, object_id, owner_id, content_hash);
        CREATE INDEX IF NOT EXISTS idx_data_snapshots_lookup ON data_snapshots(match_id, object_id, snapshot_type, content_hash, captured_at);
        CREATE INDEX IF NOT EXISTS idx_intelligence_signals_lookup ON intelligence_signals(status, object_id, match_id, signal_type, created_at);
        CREATE INDEX IF NOT EXISTS idx_intelligence_signals_hash ON intelligence_signals(content_hash);
        CREATE INDEX IF NOT EXISTS idx_system_event_logs_timeline ON system_event_logs(created_at, category, event_type);
        CREATE INDEX IF NOT EXISTS idx_system_event_logs_entities ON system_event_logs(match_id, object_id, employee_id);

        CREATE TABLE IF NOT EXISTS memory_access_logs (
            id TEXT PRIMARY KEY,
            memory_id TEXT NOT NULL,
            employee_id TEXT,
            match_id TEXT,
            used_at TEXT NOT NULL,
            FOREIGN KEY(memory_id) REFERENCES memories(id)
        );

        CREATE TABLE IF NOT EXISTS matches (
            id TEXT PRIMARY KEY,
            stage TEXT NOT NULL,
            group_name TEXT NOT NULL,
            home_object_id TEXT NOT NULL,
            away_object_id TEXT NOT NULL,
            kickoff_time TEXT NOT NULL,
            venue TEXT NOT NULL,
            status TEXT NOT NULL,
            home_score INTEGER,
            away_score INTEGER,
            FOREIGN KEY(home_object_id) REFERENCES watch_objects(id),
            FOREIGN KEY(away_object_id) REFERENCES watch_objects(id)
        );

        CREATE INDEX IF NOT EXISTS idx_matches_home ON matches(home_object_id);
        CREATE INDEX IF NOT EXISTS idx_matches_away ON matches(away_object_id);

        CREATE TABLE IF NOT EXISTS baseline_predictions (
            id TEXT PRIMARY KEY,
            match_id TEXT NOT NULL,
            strategy_version TEXT NOT NULL,
            home_win_probability REAL NOT NULL,
            draw_probability REAL NOT NULL,
            away_win_probability REAL NOT NULL,
            method TEXT NOT NULL,
            input_snapshot_ids_json TEXT NOT NULL DEFAULT '[]',
            explanation TEXT NOT NULL,
            created_at TEXT NOT NULL
        );

        CREATE INDEX IF NOT EXISTS idx_baseline_predictions_match ON baseline_predictions(match_id);
        """;
}

-- Disposable recording projection only. The source journal is authoritative.
-- Never attach this migration to the Usage database or its migration chain.
PRAGMA foreign_keys=ON;
CREATE TABLE IF NOT EXISTS recording_projection_state (
 singleton INTEGER PRIMARY KEY CHECK(singleton=1), schema_version INTEGER NOT NULL,
 projection_version INTEGER NOT NULL, journal_id TEXT, cursor INTEGER NOT NULL DEFAULT 0,
 generation INTEGER NOT NULL DEFAULT 1, rebuilding INTEGER NOT NULL DEFAULT 0
);
INSERT OR IGNORE INTO recording_projection_state(singleton,schema_version,projection_version) VALUES(1,1,1);
CREATE TABLE IF NOT EXISTS recording_occurrences (
 source_epoch TEXT NOT NULL, sequence INTEGER NOT NULL, event_id TEXT NOT NULL,
 cursor INTEGER NOT NULL UNIQUE, journal_id TEXT NOT NULL, journal_offset INTEGER NOT NULL,
 content_hash TEXT NOT NULL, capture_id TEXT NOT NULL, request_id TEXT NOT NULL,
 attempt_id TEXT, observed_ms INTEGER NOT NULL, boundary TEXT NOT NULL, protocol TEXT NOT NULL,
 kind TEXT NOT NULL, complete INTEGER NOT NULL, gap_reason TEXT,
 PRIMARY KEY(source_epoch,sequence)
);
CREATE TABLE IF NOT EXISTS recording_records (
 id TEXT PRIMARY KEY, capture_id TEXT NOT NULL, request_id TEXT NOT NULL, attempt_id TEXT,
 conversation_id TEXT NOT NULL, parent_conversation_id TEXT, worker_id TEXT, parent_worker_id TEXT,
 parent_request_id TEXT, observed_ms INTEGER NOT NULL, model TEXT, requested_model TEXT,
 route_id TEXT, provider TEXT, account_id TEXT, response_id TEXT, previous_response_id TEXT,
 complete INTEGER NOT NULL, gap_reason TEXT, projection_version INTEGER NOT NULL,
 record_json TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS recording_records_conversation ON recording_records(conversation_id,observed_ms,id);
CREATE INDEX IF NOT EXISTS recording_records_capture ON recording_records(capture_id);
CREATE INDEX IF NOT EXISTS recording_records_request ON recording_records(request_id,attempt_id);
CREATE INDEX IF NOT EXISTS recording_records_worker ON recording_records(worker_id,observed_ms,id);
CREATE INDEX IF NOT EXISTS recording_records_model ON recording_records(model,observed_ms,id);
CREATE INDEX IF NOT EXISTS recording_records_route ON recording_records(route_id,observed_ms,id);
CREATE TABLE IF NOT EXISTS recording_evidence (
 record_id TEXT NOT NULL REFERENCES recording_records(id) ON DELETE CASCADE,
 source_epoch TEXT NOT NULL, sequence INTEGER NOT NULL, event_id TEXT NOT NULL,
 PRIMARY KEY(record_id,source_epoch,sequence,event_id)
);
CREATE TABLE IF NOT EXISTS recording_entities (
 scope TEXT NOT NULL, kind TEXT NOT NULL, entity_id TEXT NOT NULL,
 native_id TEXT, provenance TEXT NOT NULL, state TEXT NOT NULL,
 PRIMARY KEY(scope,kind,entity_id)
);
CREATE TABLE IF NOT EXISTS recording_edges (
 scope TEXT NOT NULL, from_id TEXT NOT NULL, to_id TEXT NOT NULL, kind TEXT NOT NULL,
 source_epoch TEXT NOT NULL, sequence INTEGER NOT NULL, evidence TEXT NOT NULL,
 state TEXT NOT NULL, PRIMARY KEY(scope,from_id,to_id,kind,source_epoch,sequence)
);
CREATE TABLE IF NOT EXISTS recording_runs (
 id TEXT PRIMARY KEY, capture_id TEXT, conversation_id TEXT,
 variant TEXT, configured_revision TEXT, prompt_hash TEXT, skill_hash TEXT,
 observed_instruction_hash TEXT, evidence TEXT NOT NULL, source_cursor INTEGER NOT NULL,
 manifest_json TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS recording_runs_conversation ON recording_runs(conversation_id,id);
PRAGMA user_version=1;

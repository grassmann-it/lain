CREATE TABLE sources (
    source_id TEXT PRIMARY KEY,
    cursor INTEGER NOT NULL CHECK(cursor >= 0),
    high_watermark INTEGER NOT NULL CHECK(high_watermark >= cursor),
    observed_ms INTEGER NOT NULL,
    faulted INTEGER NOT NULL DEFAULT 0 CHECK(faulted IN (0,1))
);
CREATE TABLE journal_events (
    source_id TEXT NOT NULL REFERENCES sources(source_id),
    sequence INTEGER NOT NULL CHECK(sequence > 0),
    event_id TEXT NOT NULL,
    digest TEXT NOT NULL,
    PRIMARY KEY(source_id, sequence),
    UNIQUE(source_id, event_id)
);
CREATE TABLE attempts (
    source_id TEXT NOT NULL REFERENCES sources(source_id),
    attempt_id TEXT NOT NULL,
    request_id TEXT NOT NULL,
    account_id TEXT NOT NULL,
    credential_id TEXT NOT NULL,
    route_id TEXT,
    provider TEXT NOT NULL,
    requested_model TEXT NOT NULL,
    model TEXT NOT NULL,
    started_ms INTEGER NOT NULL,
    finished_ms INTEGER,
    status_code INTEGER,
    state TEXT NOT NULL,
    usage_reported INTEGER NOT NULL,
    input_tokens INTEGER,
    output_tokens INTEGER,
    cached_tokens INTEGER,
    cache_creation_tokens INTEGER,
    reasoning_tokens INTEGER,
    total_tokens INTEGER,
    duration_ms REAL,
    first_token_ms REAL,
    PRIMARY KEY(source_id, attempt_id)
);
CREATE INDEX attempts_time ON attempts(started_ms, attempt_id);
CREATE INDEX attempts_account_time ON attempts(account_id, started_ms);
CREATE INDEX attempts_model_time ON attempts(model, started_ms);
CREATE INDEX attempts_status_time ON attempts(status_code, started_ms);
PRAGMA user_version = 1;

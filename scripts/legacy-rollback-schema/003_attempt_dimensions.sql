-- Historical rows remain unknown. No identity or measurement is inferred from
-- account labels, model names, legacy timings, or timestamp proximity.
ALTER TABLE attempts ADD COLUMN dimensions_json TEXT
    CHECK(dimensions_json IS NULL OR json_valid(dimensions_json));
ALTER TABLE attempts ADD COLUMN measurements_json TEXT
    CHECK(measurements_json IS NULL OR json_valid(measurements_json));
ALTER TABLE attempts ADD COLUMN first_output_latency_ms REAL
    CHECK(first_output_latency_ms IS NULL OR first_output_latency_ms >= 0);
ALTER TABLE attempts ADD COLUMN total_duration_ms REAL
    CHECK(total_duration_ms IS NULL OR total_duration_ms >= 0);
ALTER TABLE attempts ADD COLUMN completion_recorded INTEGER NOT NULL DEFAULT 0
    CHECK(completion_recorded IN (0,1));
ALTER TABLE attempts ADD COLUMN completion_state TEXT
    CHECK(completion_state IS NULL OR completion_state IN ('success','failed','canceled'));
CREATE INDEX attempts_route_time ON attempts(route_id,started_ms);
CREATE INDEX attempts_family_time ON attempts(json_extract(dimensions_json,'$.modelFamily'),started_ms);
CREATE INDEX attempts_conversation_time ON attempts(json_extract(dimensions_json,'$.conversationId'),started_ms);
CREATE INDEX attempts_worker_time ON attempts(json_extract(dimensions_json,'$.workerId'),started_ms);
PRAGMA user_version = 3;

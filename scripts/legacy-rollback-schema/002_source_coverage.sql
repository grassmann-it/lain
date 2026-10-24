CREATE TABLE collector_state (
    singleton INTEGER PRIMARY KEY CHECK(singleton = 1),
    active_source_id TEXT REFERENCES sources(source_id)
);
INSERT INTO collector_state(singleton,active_source_id)
VALUES(1,(SELECT source_id FROM sources ORDER BY observed_ms DESC,source_id LIMIT 1));
CREATE TABLE coverage_gaps (
    id INTEGER PRIMARY KEY,
    from_ms INTEGER NOT NULL,
    to_ms INTEGER NOT NULL CHECK(to_ms > from_ms),
    previous_source_id TEXT REFERENCES sources(source_id),
    next_source_id TEXT REFERENCES sources(source_id),
    reason TEXT NOT NULL
);
CREATE INDEX coverage_gaps_time ON coverage_gaps(from_ms,to_ms);
-- Version 1 did not retain source transitions. Multiple historical sources cannot
-- establish coverage before their last observation, so never certify that history.
INSERT INTO coverage_gaps(from_ms,to_ms,reason)
SELECT -62135596800000, MAX(observed_ms)+1, 'legacy_source_history'
FROM sources HAVING COUNT(*) > 1;
PRAGMA user_version = 2;

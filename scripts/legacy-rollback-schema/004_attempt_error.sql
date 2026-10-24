ALTER TABLE attempts ADD COLUMN error_category TEXT
    CHECK(error_category IS NULL OR error_category IN (
        'invalid_request', 'authentication', 'permission', 'rate_limited',
        'upstream_error', 'canceled', 'unknown', 'unsupported_tool', 'tool_limit'
    ));
PRAGMA user_version = 4;

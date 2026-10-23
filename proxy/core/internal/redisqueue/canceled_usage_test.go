package redisqueue

import (
	"context"
	coreusage "github.com/router-for-me/CLIProxyAPI/v7/sdk/cliproxy/usage"
	"testing"
)

func TestUsageQueueCanceledHasNoNormalizedZeroTokens(t *testing.T) {
	withEnabledQueue(t, func() {
		reported := false
		(&usageQueuePlugin{}).HandleUsage(context.Background(), coreusage.Record{
			RequestID: "aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee", ParentRequestID: "11111111-2222-4333-8444-555555555555",
			Provider: "codex", Failed: true, Outcome: "canceled", UsageReported: &reported,
			Detail: coreusage.Detail{TotalTokens: 999}, // explicit absence overrides stray detail
		})
		payload := popSinglePayload(t)
		requireStringField(t, payload, "outcome", "canceled")
		requireStringField(t, payload, "execution_id", "aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee")
		requireStringField(t, payload, "parent_request_id", "11111111-2222-4333-8444-555555555555")
		if string(payload["usage_reported"]) != "false" || string(payload["failed"]) != "true" {
			t.Fatal("cancel classified as success or usage presence lost")
		}
		for _, key := range []string{"tokens", "token_breakdown", "fail"} {
			requireMissingField(t, payload, key)
		}
	})
}

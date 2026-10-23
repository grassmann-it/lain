package redisqueue

import (
	"context"
	"testing"

	coreusage "github.com/router-for-me/CLIProxyAPI/v7/sdk/cliproxy/usage"
)

func TestUsageQueueParentIsExplicitAndAttemptsStayDistinct(t *testing.T) {
	withEnabledQueue(t, func() {
		parent := "11111111-2222-4333-8444-555555555555"
		for _, attempt := range []string{"aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee", "12345678-1234-4234-8234-123456789abc"} {
			(&usageQueuePlugin{}).HandleUsage(context.Background(), coreusage.Record{
				RequestID: attempt, ParentRequestID: parent, TraceID: "00000000", Provider: "test", Model: "model",
			})
			payload := popSinglePayload(t)
			requireStringField(t, payload, "execution_id", attempt)
			requireStringField(t, payload, "parent_request_id", parent)
			requireStringField(t, payload, "trace_id", "00000000")
		}
		(&usageQueuePlugin{}).HandleUsage(context.Background(), coreusage.Record{RequestID: parent, TraceID: "00000000"})
		requireMissingField(t, popSinglePayload(t), "parent_request_id")
	})
}

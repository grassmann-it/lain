package helps

import (
	"context"
	"testing"
	"time"

	"github.com/router-for-me/CLIProxyAPI/v7/sdk/cliproxy/usage"
)

type cancelUsageCapture struct {
	alias   string
	records chan usage.Record
}

func (c *cancelUsageCapture) HandleUsage(_ context.Context, r usage.Record) {
	if r.Alias == c.alias {
		c.records <- r
	}
}
func (c *cancelUsageCapture) next(t *testing.T) usage.Record {
	t.Helper()
	select {
	case r := <-c.records:
		return r
	case <-time.After(5 * time.Second):
		t.Fatal("missing record")
		return usage.Record{}
	}
}

type cancelUsageNoop struct{}

func (cancelUsageNoop) HandleUsage(context.Context, usage.Record) {}

func TestUsageReporterCanceledSharesExecutionAndOnce(t *testing.T) {
	for _, terminalFirst := range []bool{false, true} {
		name := "canceled_first"
		if terminalFirst {
			name = "completed_first"
		}
		t.Run(name, func(t *testing.T) {
			capture := &cancelUsageCapture{alias: t.Name(), records: make(chan usage.Record, 8)}
			usage.RegisterNamedPlugin(t.Name(), capture)
			t.Cleanup(func() { usage.RegisterNamedPlugin(t.Name(), cancelUsageNoop{}) })
			parent := "11111111-2222-4333-8444-555555555555"
			ctx, cancel := context.WithCancel(usage.WithRequestedModelAlias(usage.WithParentRequestID(context.Background(), parent), t.Name()))
			defer cancel()
			reporter := NewUsageReporter(ctx, "codex", "model", nil)
			reporter.PublishCanceled(ctx) // live context must not consume once
			deadlineCtx, deadlineCancel := context.WithDeadline(ctx, time.Now().Add(-time.Second))
			defer deadlineCancel()
			reporter.PublishCanceled(deadlineCtx) // no expansion to timeouts
			detail := usage.Detail{InputTokens: 5, OutputTokens: 7, TotalTokens: 12}
			if terminalFirst {
				reporter.Publish(ctx, detail)
			}
			cancel()
			reporter.PublishCanceled(ctx)
			reporter.PublishCanceled(ctx)
			reporter.Publish(ctx, detail)
			record := capture.next(t)
			if record.RequestID != reporter.RequestID() || record.ParentRequestID != parent {
				t.Fatal("cancellation lost original execution or parent")
			}
			if terminalFirst {
				if record.Failed || record.Outcome == "canceled" || record.Detail.TotalTokens != 12 {
					t.Fatal("late cancellation replaced completed usage")
				}
			} else {
				if !record.Failed || record.Outcome != "canceled" || record.UsageReported == nil || *record.UsageReported || record.Detail != (usage.Detail{}) || record.Fail != (usage.Failure{}) {
					t.Fatal("canceled metadata normalized or misclassified")
				}
			}
			const sentinel = "aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee"
			usage.PublishRecord(ctx, usage.Record{RequestID: sentinel, Alias: t.Name()})
			if capture.next(t).RequestID != sentinel {
				t.Fatal("reporter once emitted duplicate completion")
			}
		})
	}
}

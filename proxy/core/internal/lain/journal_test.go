package lain

import (
	"context"
	"encoding/json"
	"errors"
	"github.com/gin-gonic/gin"
	"github.com/google/uuid"
	"github.com/router-for-me/CLIProxyAPI/v7/internal/config"
	"github.com/router-for-me/CLIProxyAPI/v7/internal/runtime/executor/helps"
	auth "github.com/router-for-me/CLIProxyAPI/v7/sdk/cliproxy/auth"
	executor "github.com/router-for-me/CLIProxyAPI/v7/sdk/cliproxy/executor"
	"github.com/router-for-me/CLIProxyAPI/v7/sdk/cliproxy/usage"
	"github.com/tidwall/gjson"
	"os"
	"path/filepath"
	"reflect"
	"sync"
	"testing"
)

func testJournal(t *testing.T) *Journal {
	t.Helper()
	j, e := OpenJournal(filepath.Join(t.TempDir(), "events.jsonl"))
	if e != nil {
		t.Fatal(e)
	}
	t.Cleanup(func() { _ = j.Close() })
	return j
}
func TestJournalReplayRestartAndExclusiveOwnership(t *testing.T) {
	path := filepath.Join(t.TempDir(), "events.jsonl")
	j, err := OpenJournal(path)
	if err != nil {
		t.Fatal(err)
	}
	if _, err = OpenJournal(path); err == nil {
		t.Fatal("second writer accepted")
	}
	if err = j.Append(Event{Kind: "attempt_started", RequestID: "r", AttemptID: "a"}); err != nil {
		t.Fatal(err)
	}
	one, err := j.Read(0, 1000)
	if err != nil {
		t.Fatal(err)
	}
	_ = j.Close()
	j, err = OpenJournal(path)
	if err != nil {
		t.Fatal(err)
	}
	defer func() { _ = j.Close() }()
	replay, _ := j.Read(0, 1000)
	if !reflect.DeepEqual(one, replay) {
		t.Fatalf("replay changed: %#v %#v", one, replay)
	}
	if err = j.Append(Event{Kind: "usage", RequestID: "r", AttemptID: "a"}); err != nil {
		t.Fatal(err)
	}
	tail, _ := j.Read(1, 1)
	if tail.NextCursor != 2 || tail.HighWatermark != 2 || len(tail.Events) != 1 {
		t.Fatal(tail)
	}
}
func TestJournalSyncIsBeforeVisibilityAndFaultIsSticky(t *testing.T) {
	j := testJournal(t)
	syncCalls := 0
	j.syncFile = func() error {
		syncCalls++
		if len(j.offsets) != 0 {
			t.Fatal("event visible before fsync")
		}
		return errors.New("injected disk-full fsync")
	}
	if j.Append(Event{Kind: "attempt_started"}) == nil {
		t.Fatal("fsync failure ignored")
	}
	if j.Append(Event{Kind: "usage"}) == nil {
		t.Fatal("continued after disk fault")
	}
	if syncCalls != 1 {
		t.Fatal(syncCalls)
	}
	b, _ := j.Read(0, 1)
	if b.HighWatermark != 0 {
		t.Fatal("failed fsync published")
	}
}
func TestJournalTornAppendAndWrongSequenceFailClosed(t *testing.T) {
	for _, data := range []string{"{\"sequence\":1", "{}\n"} {
		t.Run(data, func(t *testing.T) {
			path := filepath.Join(t.TempDir(), "events.jsonl")
			j, e := OpenJournal(path)
			if e != nil {
				t.Fatal(e)
			}
			_ = j.Close()
			if e = os.WriteFile(path, []byte(data), 0600); e != nil {
				t.Fatal(e)
			}
			if _, e = OpenJournal(path); e == nil {
				t.Fatal("corruption silently accepted")
			}
		})
	}
}
func TestJournalConcurrentContiguousAndBounded(t *testing.T) {
	j := testJournal(t)
	var wg sync.WaitGroup
	for n := 0; n < 40; n++ {
		wg.Add(1)
		go func() {
			defer wg.Done()
			if e := j.Append(Event{Kind: "usage"}); e != nil {
				t.Error(e)
			}
		}()
	}
	wg.Wait()
	b, e := j.Read(0, 13)
	if e != nil || len(b.Events) != 13 || b.NextCursor != 13 || b.HighWatermark != 40 {
		t.Fatal(b, e)
	}
	for n, e := range b.Events {
		if e.Sequence != int64(n+1) {
			t.Fatal(e)
		}
	}
}
func dispatchFixture(t *testing.T) (*Integration, context.Context, *auth.Auth) {
	t.Helper()
	j := testJournal(t)
	b := Bindings{Accounts: map[string]string{"stable-account": "credential-v2"}, Routes: map[string]Route{"alias": {ID: "route", Model: "model", Selection: "strict", Accounts: []string{"stable-account"}}}}
	i, e := New(j, b)
	if e != nil {
		t.Fatal(e)
	}
	c := &gin.Context{}
	c.Set("lain.selection", selection{Route: b.Routes["alias"], Alias: "alias", Allowed: map[string]bool{"credential-v2": true}})
	ctx := context.WithValue(context.Background(), "gin", c)
	ctx = usage.WithParentRequestID(ctx, uuid.NewString())
	ctx = usage.WithExecutionRequestID(ctx, uuid.NewString())
	return i, ctx, &auth.Auth{ID: "credential-v2", Provider: "fixture"}
}
func TestDispatchRequiresDurableIdentityAndTerminalUnknown(t *testing.T) {
	i, ctx, a := dispatchFixture(t)
	if err := i.Before(ctx, &auth.Auth{ID: "wrong"}, executor.Request{Model: "model"}, executor.Options{}); err == nil {
		t.Fatal("wrong account dispatched")
	}
	if err := i.Before(ctx, a, executor.Request{Model: "model"}, executor.Options{}); err != nil {
		t.Fatal(err)
	}
	start, _ := i.Journal.Read(0, 1000)
	if len(start.Events) != 1 || start.Events[0].AccountID != "stable-account" {
		t.Fatal(start)
	}
	if err := i.After(ctx, context.Canceled); err != nil {
		t.Fatal(err)
	}
	all, _ := i.Journal.Read(0, 1000)
	last := all.Events[1]
	if last.InputTokens != nil || last.TotalTokens != nil || last.UsageReported || *last.Outcome != "canceled" {
		t.Fatal(last)
	}
	wire, _ := json.Marshal(last)
	var check map[string]any
	_ = json.Unmarshal(wire, &check)
	if check["inputTokens"] != nil {
		t.Fatal(string(wire))
	}
}
func TestUsageSynchronouslyDurableAndNotDoubleCounted(t *testing.T) {
	i, ctx, a := dispatchFixture(t)
	if err := i.Before(ctx, a, executor.Request{Model: "model"}, executor.Options{}); err != nil {
		t.Fatal(err)
	}
	r := usage.Record{RequestID: usage.ExecutionRequestIDFromContext(ctx), ParentRequestID: usage.ParentRequestIDFromContext(ctx), AuthID: a.ID, UsageReported: ptr(true), Detail: usage.Detail{InputTokens: 3, OutputTokens: 2, TotalTokens: 5, Reported: &usage.ReportedTokens{InputTokens: ptr(int64(3)), OutputTokens: ptr(int64(2)), TotalTokens: ptr(int64(5))}}}
	if err := i.Publish(ctx, r); err != nil {
		t.Fatal(err)
	}
	b, _ := i.Journal.Read(0, 1000)
	if len(b.Events) != 2 || *b.Events[1].TotalTokens != 5 {
		t.Fatal(b)
	}
	if err := i.Publish(ctx, r); err != nil {
		t.Fatal(err)
	}
	if err := i.After(ctx, nil); err != nil {
		t.Fatal(err)
	}
	b, _ = i.Journal.Read(0, 1000)
	if len(b.Events) != 2 {
		t.Fatal("duplicate terminal event")
	}
}
func TestUsageIdentityMismatchClosesDispatch(t *testing.T) {
	i, ctx, a := dispatchFixture(t)
	if err := i.Before(ctx, a, executor.Request{Model: "model"}, executor.Options{}); err != nil {
		t.Fatal(err)
	}
	if err := i.Publish(ctx, usage.Record{AuthID: "different-account"}); err == nil {
		t.Fatal("mismatched identity accepted")
	}
	if err := i.Before(ctx, a, executor.Request{Model: "model"}, executor.Options{}); err == nil {
		t.Fatal("dispatch after fault")
	}
}

func TestRouteCredentialOverridesPreserveLogicalAccount(t *testing.T) {
	j := testJournal(t)
	b := Bindings{Accounts: map[string]string{"a": "chat-credential"}, Routes: map[string]Route{"alias": {ID: "route", Model: "model", Selection: "strict", Accounts: []string{"a"}, Credentials: map[string]string{"a": "responses-credential"}}}}
	i, err := New(j, b)
	if err != nil {
		t.Fatal(err)
	}
	c := &gin.Context{}
	c.Set("lain.selection", selection{Route: b.Routes["alias"], Alias: "alias", Allowed: map[string]bool{"responses-credential": true}})
	ctx := usage.WithExecutionRequestID(usage.WithParentRequestID(context.WithValue(context.Background(), "gin", c), uuid.NewString()), uuid.NewString())
	if err = i.Before(ctx, &auth.Auth{ID: "responses-credential", Provider: "codex"}, executor.Request{Model: "model"}, executor.Options{}); err != nil {
		t.Fatal(err)
	}
	page, _ := j.Read(0, 10)
	if page.Events[0].AccountID != "a" || page.Events[0].CredentialID != "responses-credential" {
		t.Fatal(page)
	}
	b.Accounts["b"] = "responses-credential"
	if _, err = New(j, b); err == nil {
		t.Fatal("same credential assigned to two logical accounts")
	}
}

func TestDispatchPreservesPublicAliasForPayloadRules(t *testing.T) {
	i, ctx, a := dispatchFixture(t)
	opts := executor.Options{Metadata: map[string]any{executor.RequestedModelMetadataKey: "core-model"}}
	if err := i.Before(ctx, a, executor.Request{Model: "upstream-model"}, opts); err != nil {
		t.Fatal(err)
	}
	if opts.Metadata[executor.RequestedModelMetadataKey] != "alias" {
		t.Fatal("public payload selector alias lost")
	}
}

func TestPublicAliasPayloadFilterAfterRouteRewrite(t *testing.T) {
	i, ctx, a := dispatchFixture(t)
	c := ctx.Value("gin").(*gin.Context)
	v, _ := c.Get("lain.selection")
	selected := v.(selection)
	selected.Alias = "cmd-fixture"
	c.Set("lain.selection", selected)
	opts := executor.Options{Metadata: map[string]any{executor.RequestedModelMetadataKey: "gpt-upstream"}}
	if err := i.Before(ctx, a, executor.Request{Model: "gpt-upstream"}, opts); err != nil {
		t.Fatal(err)
	}
	cfg := &config.Config{Payload: config.PayloadConfig{Filter: []config.PayloadFilterRule{{Models: []config.PayloadModelRule{{Name: "cmd-*", Protocol: "codex"}}, Params: []string{"include"}}}}}
	body := []byte(`{"model":"gpt-upstream","include":["reasoning.encrypted_content"],"input":"hello"}`)
	filtered := helps.ApplyPayloadConfigWithRoot(cfg, "gpt-upstream", "codex", "", body, body, helps.PayloadRequestedModel(opts, "gpt-upstream"), "")
	if gjson.GetBytes(filtered, "include").Exists() || gjson.GetBytes(filtered, "input").String() != "hello" {
		t.Fatal(string(filtered))
	}
}

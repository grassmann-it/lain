package lain

import (
	"context"
	"encoding/json"
	"errors"
	"io"
	"net/http"
	"os"
	"strings"
	"testing"

	"github.com/gin-gonic/gin"
	auth "github.com/router-for-me/CLIProxyAPI/v8/sdk/cliproxy/auth"
	executor "github.com/router-for-me/CLIProxyAPI/v8/sdk/cliproxy/executor"
	"github.com/router-for-me/CLIProxyAPI/v8/sdk/cliproxy/usage"
	"github.com/router-for-me/CLIProxyAPI/v8/sdk/httptransport"
	"github.com/router-for-me/CLIProxyAPI/v8/sdk/translator"
)

func compatibilityFixture(t *testing.T, provider, model, protocol string) (*Integration, context.Context, *auth.Auth) {
	i, ctx, a := dispatchFixture(t)
	c := ctx.Value("gin").(*gin.Context)
	route := i.Bindings.Routes["alias"]
	route.Provider, route.Model, route.UpstreamProtocol = provider, model, protocol
	i.Bindings.Routes["alias"] = route
	c.Set("lain.selection", selection{Route: route, Alias: "alias", Allowed: map[string]bool{a.ID: true}})
	a.Attributes = map[string]string{"upstream_protocol": map[string]string{"openai": "openai-chat", "responses": "openai-responses"}[protocol]}
	return i, ctx, a
}

func TestSharedCompatibilityDispatchMatrix(t *testing.T) {
	path := os.Getenv("LAIN_COMPATIBILITY_FIXTURE")
	if path == "" {
		path = "testdata/dispatch-matrix.json"
	}
	data, err := os.ReadFile(path)
	if err != nil {
		t.Fatal(err)
	}
	var matrix struct {
		ContractVersion int
		Cases           []struct {
			Name, Provider, Model, IngressProtocol, UpstreamProtocol string
			Request                                                  json.RawMessage
			ExpectedCode, ExpectedCategory                           *string
		}
	}
	if json.Unmarshal(data, &matrix) != nil || matrix.ContractVersion != 1 || len(matrix.Cases) == 0 {
		t.Fatal("unknown or empty shared contract")
	}
	for _, row := range matrix.Cases {
		t.Run(row.Name, func(t *testing.T) {
			_, ctx, a := compatibilityFixture(t, row.Provider, row.Model, row.UpstreamProtocol)
			format := translator.FormatOpenAI
			if row.IngressProtocol == "responses" {
				format = translator.FormatOpenAIResponse
			}
			policy, err := compatibilityForDispatch(ctx, a, executor.Request{Model: row.Model}, executor.Options{SourceFormat: format}, "")
			if err != nil {
				t.Fatal(err)
			}
			for _, final := range []bool{false, true} {
				var payload map[string]json.RawMessage
				_ = json.Unmarshal(row.Request, &payload)
				payload["model"], _ = json.Marshal(row.Model)
				body, _ := json.Marshal(payload)
				err = policy.validate(body, final)
				var rejected *compatibilityError
				if row.ExpectedCode == nil {
					if err != nil {
						t.Fatal(err)
					}
					continue
				}
				if !errors.As(err, &rejected) || rejected.code != *row.ExpectedCode || row.ExpectedCategory == nil || rejected.category != *row.ExpectedCategory {
					t.Fatalf("final=%v got %v, want %s", final, err, *row.ExpectedCode)
				}
			}
		})
	}
}

func TestCompatibilityOriginalFailureHasDurableIdentity(t *testing.T) {
	i, ctx, a := compatibilityFixture(t, "xiaomi-mimo", "mimo-v2.6-pro", "openai")
	c := ctx.Value("gin").(*gin.Context)
	// Simulate a handler dropping a tool before executor options are produced.
	c.Set("lain.original_request", []byte(`{"model":"alias","tools":[{"type":"web_search"}]}`))
	raw := &fixtureExecutor{}
	bound := i.ExecutorDecorator(nil)(raw)
	_, err := bound.Execute(ctx, a, executor.Request{Model: "mimo-v2.6-pro", Payload: []byte(`{"tools":[]}`)}, executor.Options{SourceFormat: translator.FormatOpenAIResponse})
	if compatibilityErrorCategory(err) != "unsupported_tool" || raw.executions != 0 {
		t.Fatalf("provider reached or policy missed: %v %d", err, raw.executions)
	}
	page, _ := i.Journal.Read(0, 100)
	if len(page.Events) != 2 {
		t.Fatal(page)
	}
	first, last := page.Events[0], page.Events[1]
	if first.Kind != "attempt_started" || last.Kind != "usage" || last.AccountID != "stable-account" || last.RequestID != first.RequestID || last.AttemptID != first.AttemptID || first.AttemptID == "" || last.ErrorCategory == nil || *last.ErrorCategory != "unsupported_tool" || last.Outcome == nil || *last.Outcome != "failed" || last.StatusCode == nil || *last.StatusCode != 400 {
		t.Fatal(page)
	}
	if last.InputTokens != nil || last.TotalTokens != nil || last.UsageReported || first.ErrorCategory != nil {
		t.Fatal("invented measurement or premature failure")
	}
}

type compatibilityWireExecutor struct {
	fixtureExecutor
	body string
	sent int
}

func (e *compatibilityWireExecutor) Execute(ctx context.Context, a *auth.Auth, _ executor.Request, _ executor.Options) (executor.Response, error) {
	e.executions++
	client := httptransport.Decorate(ctx, &http.Client{Transport: fixtureTransport(func(r *http.Request) (*http.Response, error) {
		e.sent++
		return &http.Response{StatusCode: 200, Body: io.NopCloser(strings.NewReader(`{}`)), Header: http.Header{}}, nil
	})})
	r, _ := http.NewRequestWithContext(ctx, "POST", "https://fixture.invalid/v1/chat/completions", strings.NewReader(e.body))
	response, err := client.Do(r)
	if response != nil {
		_ = response.Body.Close()
	}
	if err != nil {
		usage.PublishRecord(ctx, usage.Record{RequestID: usage.ExecutionRequestIDFromContext(ctx), AuthID: a.ID, Failed: true, Fail: usage.Failure{StatusCode: 400}})
	}
	return executor.Response{}, err
}

func TestCompatibilityFinalTransportRejectsOverrideAndRecordsCategory(t *testing.T) {
	i, ctx, a := compatibilityFixture(t, "commandcode", "stealth/glyph-cluster:free", "openai")
	raw := &compatibilityWireExecutor{body: `{"model":"stealth/glyph-cluster:free","tools":[{"type":"custom","name":"apply_patch"}]}`}
	bound := i.ExecutorDecorator(nil)(raw)
	_, err := bound.Execute(ctx, a, executor.Request{Model: "stealth/glyph-cluster:free", Payload: []byte(`{"tools":[]}`)}, executor.Options{SourceFormat: translator.FormatOpenAIResponse})
	if compatibilityErrorCategory(err) != "unsupported_tool" || raw.executions != 1 || raw.sent != 0 {
		t.Fatalf("wire guard missed %v %d/%d", err, raw.executions, raw.sent)
	}
	page, _ := i.Journal.Read(0, 100)
	if len(page.Events) != 3 || page.Events[1].ErrorCategory == nil || *page.Events[1].ErrorCategory != "unsupported_tool" || page.Events[2].ErrorCategory != nil || page.Events[2].Kind != "attempt_finished" {
		t.Fatal(page)
	}
}

func TestBeamCountsFinalTopLevelToolsWithoutMutation(t *testing.T) {
	_, ctx, a := compatibilityFixture(t, "commandcode", "reflection/beam:free", "openai")
	p, err := compatibilityForDispatch(ctx, a, executor.Request{Model: "reflection/beam:free"}, executor.Options{SourceFormat: translator.FormatOpenAIResponse}, "")
	if err != nil {
		t.Fatal(err)
	}
	for _, n := range []int{128, 129} {
		items := make([]map[string]string, n)
		for k := range items {
			items[k] = map[string]string{"type": "function", "name": "fixture"}
		}
		body, _ := json.Marshal(map[string]any{"model": "reflection/beam:free", "tools": items})
		original := string(body)
		if err := p.validate(body, false); err != nil {
			t.Fatal("original count incorrectly treated as wire count", err)
		}
		err = p.validate(body, true)
		if n == 129 && compatibilityErrorCategory(err) != "tool_limit" || n == 128 && err != nil || string(body) != original {
			t.Fatalf("n=%d err=%v or body mutated", n, err)
		}
	}
	nested := `{"model":"reflection/beam:free","tools":[{"type":"namespace","tools":[` + strings.TrimSuffix(strings.Repeat(`{"type":"function","name":"f"},`, 129), ",") + `]}]}`
	if err := p.validate([]byte(nested), true); err != nil {
		t.Fatal("nested tools added to top-level count", err)
	}
}

func TestCompatibilityScopeAndDuplicateControlsFailClosed(t *testing.T) {
	_, ctx, a := compatibilityFixture(t, "commandcode", "stealth/glyph-cluster:free", "openai")
	req := executor.Request{Model: "stealth/glyph-cluster:free"}
	opts := executor.Options{SourceFormat: translator.FormatOpenAIResponse}
	p, err := compatibilityForDispatch(ctx, a, req, opts, "")
	if err != nil {
		t.Fatal(err)
	}
	for _, body := range []string{`{"tools":[],"tools":[{"type":"custom"}]}`, `{"tools":[{"type":"function","type":"custom"}]}`, `{"tool_choice":{"type":"custom"}}`} {
		if err := p.validate([]byte(body), false); err == nil {
			t.Fatal("ambiguous/unsupported controls accepted", body)
		}
	}
	if err := p.validate([]byte(`{"tools":[{"type":"function","function":{"name":"custom","parameters":{"type":"namespace"}}}]}`), false); err != nil {
		t.Fatal("function data treated as native tools", err)
	}
	req.Model = "alias"
	if _, err := compatibilityForDispatch(ctx, a, req, opts, ""); err == nil {
		t.Fatal("alias model selected upstream rule")
	}
	req.Model = "stealth/glyph-cluster:free"
	a.Attributes["upstream_protocol"] = "openai-responses"
	if _, err := compatibilityForDispatch(ctx, a, req, opts, ""); err == nil {
		t.Fatal("mismatched transport accepted")
	}
	if err := p.validate([]byte(`{"model":"other","tools":[]}`), true); err == nil {
		t.Fatal("final model changed")
	}
	wrongProtocol, _ := http.NewRequest("POST", "https://fixture.invalid/v1/responses", strings.NewReader(`{"model":"stealth/glyph-cluster:free","tools":[]}`))
	if err := p.validateFinal(wrongProtocol); err == nil {
		t.Fatal("final HTTP protocol changed")
	}
	wire, _ := json.Marshal(Event{Kind: "usage"})
	if strings.Contains(string(wire), "errorCategory") {
		t.Fatal("null category must be omitted")
	}
	if compatibilityErrorCategory(errors.New("429 unsupported_tool")) != "" {
		t.Fatal("free text classified as policy rejection")
	}
}

type namedFixtureExecutor struct {
	fixtureExecutor
	name string
}

func (e *namedFixtureExecutor) Identifier() string { return e.name }

type formattedFixtureExecutor struct {
	namedFixtureExecutor
	format translator.Format
}

func (e *formattedFixtureExecutor) RequestToFormat(executor.Request, executor.Options) translator.Format {
	return e.format
}

func TestCompatibilityNativeExecutorsDoNotInheritChatConstraints(t *testing.T) {
	for _, row := range []struct {
		provider, model, protocol, name string
		format                          translator.Format
	}{
		{"xiaomi-mimo", "mimo-v2.6-pro", "responses", "codex", ""},
		{"commandcode", "stealth/glyph-cluster:free", "anthropic", "claude", ""},
		{"commandcode", "reflection/beam:free", "gemini", "gemini", translator.FormatGemini},
	} {
		t.Run(row.protocol, func(t *testing.T) {
			i, ctx, a := compatibilityFixture(t, row.provider, row.model, row.protocol)
			a.Attributes = map[string]string{}
			base := &namedFixtureExecutor{name: row.name}
			var raw auth.ProviderExecutor = base
			if row.format != "" {
				raw = &formattedFixtureExecutor{namedFixtureExecutor: *base, format: row.format}
			}
			bound := i.ExecutorDecorator(nil)(raw)
			_, err := bound.Execute(ctx, a, executor.Request{Model: row.model, Payload: []byte(`{"tools":[{"type":"web_search"},{"type":"custom"},{"type":"namespace","tools":[]}]}`)}, executor.Options{SourceFormat: translator.FormatOpenAIResponse})
			if err != nil {
				t.Fatal("native transport inherited Chat constraint", err)
			}
			page, _ := i.Journal.Read(0, 100)
			if len(page.Events) != 3 || page.Events[1].ErrorCategory != nil || *page.Events[1].Outcome != "success" {
				t.Fatal(page)
			}
		})
	}
}

func TestCompatibilityLegacyChatDefaultRequiresMatchingExecutorKey(t *testing.T) {
	i, ctx, a := compatibilityFixture(t, "xiaomi-mimo", "mimo-v2.6-pro", "openai")
	a.Attributes = map[string]string{"compat_name": "fixture", "provider_key": "openai-compatible-fixture"}
	raw := &namedFixtureExecutor{name: "openai-compatible-fixture"}
	_, err := i.ExecutorDecorator(nil)(raw).Execute(ctx, a, executor.Request{Model: "mimo-v2.6-pro", Payload: []byte(`{"tools":[{"type":"web_search"}]}`)}, executor.Options{SourceFormat: translator.FormatOpenAIResponse})
	if compatibilityErrorCategory(err) != "unsupported_tool" || raw.executions != 0 {
		t.Fatal("qualified legacy Chat default lost", err)
	}
	a.Attributes["provider_key"] = "different-executor"
	_, err = i.ExecutorDecorator(nil)(raw).Execute(ctx, a, executor.Request{Model: "mimo-v2.6-pro", Payload: []byte(`{"tools":[]}`)}, executor.Options{SourceFormat: translator.FormatOpenAIResponse})
	if err == nil || compatibilityErrorCategory(err) != "" {
		t.Fatal("unproven executor key guessed a transport", err)
	}
	_, err = compatibilityForDispatch(ctx, a, executor.Request{Model: "mimo-v2.6-pro"}, executor.Options{}, translator.FormatClaude)
	if err == nil {
		t.Fatal("native executor mismatch accepted")
	}
}

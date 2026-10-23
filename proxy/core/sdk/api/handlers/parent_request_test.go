package handlers

import (
	"bytes"
	"context"
	"net/http"
	"testing"
	"time"

	coreauth "github.com/router-for-me/CLIProxyAPI/v7/sdk/cliproxy/auth"
	coreexecutor "github.com/router-for-me/CLIProxyAPI/v7/sdk/cliproxy/executor"
	coreusage "github.com/router-for-me/CLIProxyAPI/v7/sdk/cliproxy/usage"
	sdkconfig "github.com/router-for-me/CLIProxyAPI/v7/sdk/config"
	"github.com/router-for-me/CLIProxyAPI/v7/sdk/pluginapi"
)

func TestLifecycleParentContextIsNestedAndIndependent(t *testing.T) {
	h := NewBaseAPIHandlers(nil, nil)
	outer := h.newRequestLifecycleTracker(context.Background(), "openai", "model", "model", false, nil, "")
	inner := h.newRequestLifecycleTracker(outer.ctx, "openai", "model", "model", false, nil, "")
	if coreusage.ParentRequestIDFromContext(outer.ctx) != outer.requestID() || coreusage.ParentRequestIDFromContext(inner.ctx) != inner.requestID() || outer.requestID() == inner.requestID() {
		t.Fatal("nested lifecycle did not establish an independent parent context")
	}
}

func TestLifecycleParentReachesAllAuthManagerPathsWithoutPayloadChanges(t *testing.T) {
	for _, mode := range []string{"execute", "count", "stream"} {
		t.Run(mode, func(t *testing.T) {
			model := "parent-request-test-" + mode
			payload := []byte(`{"model":"` + model + `","messages":[{"role":"user","content":"synthetic"}],"metadata":{"parent_request_id":"untrusted"}}`)
			var parent string
			called := false
			check := func(ctx context.Context, req coreexecutor.Request, opts coreexecutor.Options) {
				called = true
				if parent == "" || coreusage.ParentRequestIDFromContext(ctx) != parent {
					t.Fatal("execution did not receive the exact lifecycle UUID")
				}
				if !bytes.Equal(req.Payload, payload) || !bytes.Equal(opts.OriginalRequest, payload) || opts.Headers.Get("X-Synthetic") != "unchanged" {
					t.Fatal("parent propagation modified payload or headers")
				}
			}
			e := &interceptorCaptureExecutor{}
			execute := func(ctx context.Context, _ *coreauth.Auth, req coreexecutor.Request, opts coreexecutor.Options) (coreexecutor.Response, error) {
				check(ctx, req, opts)
				return coreexecutor.Response{Payload: []byte(`{"choices":[{"message":{"content":"synthetic"}}]}`)}, nil
			}
			e.execute, e.executeCount = execute, execute
			e.stream = func(ctx context.Context, _ *coreauth.Auth, req coreexecutor.Request, opts coreexecutor.Options) (*coreexecutor.StreamResult, error) {
				check(ctx, req, opts)
				chunks := make(chan coreexecutor.StreamChunk, 1)
				chunks <- coreexecutor.StreamChunk{Payload: []byte("data: {\"choices\":[{\"delta\":{\"content\":\"synthetic\"}}]}\n\n")}
				close(chunks)
				return &coreexecutor.StreamResult{Chunks: chunks}, nil
			}
			h := newInterceptorHandler(t, model, e, &sdkconfig.SDKConfig{})
			completed := make(chan pluginapi.RequestCompletion, 1)
			h.SetPluginHost(&handlerInterceptorTestHost{
				interceptRequestBeforeAuth: func(ctx context.Context, req pluginapi.RequestInterceptRequest) pluginapi.RequestInterceptResponse {
					parent = req.RequestID
					if coreusage.ParentRequestIDFromContext(ctx) != parent {
						t.Fatal("before-auth context missing parent")
					}
					return pluginapi.RequestInterceptResponse{}
				},
				completeRequest: func(ctx context.Context, c pluginapi.RequestCompletion) {
					if coreusage.ParentRequestIDFromContext(ctx) != c.RequestID {
						t.Error("completion context lost parent")
					}
					completed <- c
				},
			})
			ctx := contextWithHeaders(http.Header{"X-Synthetic": {"unchanged"}})
			switch mode {
			case "execute":
				_, _, errMsg := h.ExecuteWithAuthManager(ctx, "openai", model, payload, "")
				if errMsg != nil {
					t.Fatal("execute failed")
				}
			case "count":
				_, _, errMsg := h.ExecuteCountWithAuthManager(ctx, "openai", model, payload, "")
				if errMsg != nil {
					t.Fatal("count failed")
				}
			case "stream":
				data, _, errs := h.ExecuteStreamWithAuthManager(ctx, "openai", model, payload, "")
				for range data {
				}
				for errMsg := range errs {
					if errMsg != nil {
						t.Fatal("stream failed")
					}
				}
			}
			select {
			case c := <-completed:
				if !called || c.RequestID != parent || c.Outcome != pluginapi.RequestCompletionSucceeded {
					t.Fatal("lifecycle identity/outcome changed")
				}
			case <-time.After(time.Second):
				t.Fatal("completion did not arrive")
			}
		})
	}
}

func TestWebSocketAdapterPreservesObserverAndParentIDs(t *testing.T) {
	h := NewBaseAPIHandlers(nil, nil)
	called := false
	h.SetPluginHost(&handlerInterceptorTestHost{observeWebSocketResponseEvent: func(_ context.Context, ev pluginapi.WebSocketResponseEvent) {
		called = true
		if ev.RequestID != "observer-id" || ev.ParentRequestID != "11111111-2222-4333-8444-555555555555" {
			t.Fatal("WS adapter replaced an explicit ID or lost its parent")
		}
	}})
	h.webSocketResponseObserver("fallback-lifecycle", "")(context.Background(), coreexecutor.WebSocketResponseEvent{
		RequestID: "observer-id", ParentRequestID: "11111111-2222-4333-8444-555555555555",
	})
	if !called {
		t.Fatal("WS observer was not called")
	}
}

package handlers

import (
	"context"
	"testing"
	"time"

	coreauth "github.com/router-for-me/CLIProxyAPI/v7/sdk/cliproxy/auth"
	coreexecutor "github.com/router-for-me/CLIProxyAPI/v7/sdk/cliproxy/executor"
	"github.com/router-for-me/CLIProxyAPI/v7/sdk/pluginapi"
)

func TestWSExecutionObservationIsAfterTransformAndPreservesOwnership(t *testing.T) {
	model := "ws-execution-observer"
	started := time.Date(2026, 10, 17, 0, 0, 0, 0, time.UTC)
	ids := []string{"11111111-2222-4333-8444-555555555555", "aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee"}
	executor := &interceptorCaptureExecutor{stream: func(ctx context.Context, auth *coreauth.Auth, req coreexecutor.Request, opts coreexecutor.Options) (*coreexecutor.StreamResult, error) {
		ch := make(chan coreexecutor.StreamChunk, 3)
		for _, id := range ids {
			ch <- coreexecutor.StreamChunk{ExecutionRequestID: id, ExecutionStartedAt: started, Payload: []byte("data: {\"type\":\"response.created\",\"response\":{\"id\":\"resp_before\"}}\n\n")}
		}
		ch <- coreexecutor.StreamChunk{ExecutionRequestID: ids[0], ExecutionStartedAt: started, Payload: []byte("data: {\"type\":\"response.completed\",\"response\":{\"id\":\"resp_drop\"}}\n\n")}
		close(ch)
		return &coreexecutor.StreamResult{Chunks: ch}, nil
	}}
	handler := newInterceptorHandler(t, model, executor, nil)
	observed := make(chan pluginapi.WebSocketResponseEvent, 4)
	var parent string
	handler.SetPluginHost(&handlerInterceptorTestHost{
		interceptStreamChunk: func(ctx context.Context, r pluginapi.StreamChunkInterceptRequest) pluginapi.StreamChunkInterceptResponse {
			if r.ChunkIndex < 0 {
				return pluginapi.StreamChunkInterceptResponse{}
			}
			parent = r.RequestID
			if r.ExecutionRequestID == "" || !r.ExecutionStartedAt.Equal(started) {
				t.Error("lost execution ownership before interception")
			}
			if r.ChunkIndex == 2 {
				return pluginapi.StreamChunkInterceptResponse{DropChunk: true}
			}
			return pluginapi.StreamChunkInterceptResponse{Body: []byte("data: {\"type\":\"response.created\",\"response\":{\"id\":\"resp_after\"}}\n\n")}
		},
		observeWebSocketResponseEvent: func(ctx context.Context, e pluginapi.WebSocketResponseEvent) { observed <- e },
	})
	data, _, errs := handler.ExecuteStreamWithAuthManager(context.Background(), "openai-response", model, []byte(`{"model":"ws-execution-observer","input":[]}`), "")
	for range data {
	}
	for err := range errs {
		if err != nil {
			t.Fatal(err)
		}
	}
	for _, id := range ids {
		select {
		case e := <-observed:
			if e.RequestID != id || e.ParentRequestID != parent || e.Direction != "downstream" || !e.ExecutionStartedAt.Equal(started) || string(e.Payload) != "data: {\"type\":\"response.created\",\"response\":{\"id\":\"resp_after\"}}\n\n" {
				t.Fatalf("wrong post-transform observation: %+v", e)
			}
		default:
			t.Fatal("missing exact execution observation")
		}
	}
	select {
	case <-observed:
		t.Fatal("dropped payload was observed")
	default:
	}
}

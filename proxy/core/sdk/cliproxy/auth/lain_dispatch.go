package auth

import (
	"bytes"
	"context"
	"github.com/google/uuid"
	executor "github.com/router-for-me/CLIProxyAPI/v7/sdk/cliproxy/executor"
	"github.com/router-for-me/CLIProxyAPI/v7/sdk/cliproxy/usage"
	"github.com/tidwall/gjson"
)

// Hooks are installed once, before the service starts. Original executor types stay intact.
var LainBeforeDispatch func(context.Context, *Auth, executor.Request, executor.Options) error
var LainAfterDispatch func(context.Context, error) error

// LainObserveOutput sees downstream-format executor output, never transport
// headers. The observer must keep content separate from durable usage.
var LainObserveOutput func(context.Context, []byte, bool)
var LainReadOnlyCredentials bool
var LainCredentialWritable func(*Auth) bool

func lainCredentialWritable(a *Auth) bool {
	return !LainReadOnlyCredentials || (LainCredentialWritable != nil && LainCredentialWritable(a))
}

func lainAuthAllowed(meta map[string]any, id string) bool {
	allowed, exists := meta["lain.allowed_auth_ids"]
	if !exists {
		return true
	}
	ids, ok := allowed.(map[string]bool)
	return ok && ids[id]
}

func lainBegin(ctx context.Context, auth *Auth, req executor.Request, opts executor.Options) (context.Context, error) {
	if LainBeforeDispatch == nil {
		return ctx, nil
	}
	ctx = usage.WithExecutionRequestID(ctx, uuid.NewString())
	return ctx, LainBeforeDispatch(ctx, auth, req, opts)
}
func lainEnd(ctx context.Context, err error) error {
	if LainAfterDispatch != nil {
		if durableErr := LainAfterDispatch(ctx, err); durableErr != nil {
			return durableErr
		}
	}
	return err
}
func lainExecute(e ProviderExecutor, ctx context.Context, auth *Auth, req executor.Request, opts executor.Options) (executor.Response, error) {
	ctx, err := lainBegin(ctx, auth, req, opts)
	if err != nil {
		return executor.Response{}, err
	}
	response, err := e.Execute(ctx, auth, req, opts)
	if LainObserveOutput != nil && err == nil {
		LainObserveOutput(ctx, response.Payload, false)
	}
	return response, lainEnd(ctx, err)
}
func lainCountTokens(e ProviderExecutor, ctx context.Context, auth *Auth, req executor.Request, opts executor.Options) (executor.Response, error) {
	ctx, err := lainBegin(ctx, auth, req, opts)
	if err != nil {
		return executor.Response{}, err
	}
	response, err := e.CountTokens(ctx, auth, req, opts)
	return response, lainEnd(ctx, err)
}
func lainExecuteStream(e ProviderExecutor, ctx context.Context, auth *Auth, req executor.Request, opts executor.Options) (*executor.StreamResult, error) {
	ctx, err := lainBegin(ctx, auth, req, opts)
	if err != nil {
		return nil, err
	}
	result, err := e.ExecuteStream(ctx, auth, req, opts)
	if err != nil {
		return result, lainEnd(ctx, err)
	}
	if LainAfterDispatch == nil {
		return result, nil
	}
	chunks := make(chan executor.StreamChunk)
	go func() {
		defer close(chunks)
		var terminal error
		canceled := false
		var completion []executor.StreamChunk
		for chunk := range result.Chunks {
			if chunk.Err != nil {
				terminal = chunk.Err
			}
			if canceled {
				continue
			}
			if LainObserveOutput != nil && chunk.Err == nil {
				LainObserveOutput(ctx, chunk.Payload, true)
			}
			if len(completion) > 0 || lainTerminalChunk(chunk.Payload) {
				completion = append(completion, chunk)
				continue
			}
			select {
			case chunks <- chunk:
			case <-ctx.Done():
				terminal = ctx.Err()
				canceled = true
			}
		}
		if errEnd := lainEnd(ctx, terminal); errEnd != nil {
			if !canceled {
				select {
				case chunks <- executor.StreamChunk{Err: errEnd}:
				case <-ctx.Done():
				}
			}
			return
		}
		for _, chunk := range completion {
			select {
			case chunks <- chunk:
			case <-ctx.Done():
				return
			}
		}

	}()
	return &executor.StreamResult{Headers: result.Headers, Chunks: chunks}, nil
}

// Completion markers are withheld until terminal usage has been synchronously persisted.
func lainTerminalChunk(payload []byte) bool {
	for _, line := range bytes.Split(payload, []byte("\n")) {
		line = bytes.TrimSpace(line)
		line = bytes.TrimSpace(bytes.TrimPrefix(line, []byte("data:")))
		if bytes.Equal(line, []byte("[DONE]")) {
			return true
		}
		root := gjson.ParseBytes(line)
		switch root.Get("type").String() {
		case "response.completed", "response.done", "message_stop":
			return true
		}
		done := false
		root.Get("choices").ForEach(func(_, choice gjson.Result) bool {
			reason := choice.Get("finish_reason")
			if reason.Exists() && reason.Type != gjson.Null {
				done = true
			}
			return !done
		})
		if done {
			return true
		}
	}
	return false
}

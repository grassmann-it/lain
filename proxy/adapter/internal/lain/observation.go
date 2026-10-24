package lain

import (
	"bytes"
	"context"
	"time"

	"github.com/router-for-me/CLIProxyAPI/v8/sdk/cliproxy/usage"
	"github.com/tidwall/gjson"
)

type outputObservation struct{ kind, text, toolName, toolCallID string }

// Observe is called at the executor boundary, before client delivery. This is
// first generated output observed by Core, not network TTFT or first SSE event.
func (i *Integration) Observe(ctx context.Context, payload []byte, stream bool) {
	if len(payload) == 0 || len(payload) > 1024*1024 {
		return
	}
	now := time.Now() // retain the monotonic clock component
	i.mu.Lock()
	defer i.mu.Unlock()
	i.pruneCapture(now)
	a := i.active[usage.ExecutionRequestIDFromContext(ctx)]
	if a == nil {
		return
	}
	visit := func(raw []byte) {
		if !gjson.ValidBytes(raw) {
			return
		}
		root := gjson.ParseBytes(raw)
		responseID, key := root.Get("response.id"), "response.id"
		if responseID.Type != gjson.String {
			responseID, key = root.Get("message.id"), "message.id"
		}
		if responseID.Type != gjson.String {
			switch root.Get("object").String() {
			case "response", "chat.completion", "chat.completion.chunk":
				responseID, key = root.Get("id"), "id"
			}
			if root.Get("type").String() == "message" {
				responseID, key = root.Get("id"), "id"
			}
		}
		if responseID.Type == gjson.String && safeDimension(responseID.String()) != "" {
			if a.event.Dimensions == nil {
				a.event.Dimensions = &Dimensions{}
			}
			if a.event.Dimensions.set("responseId", &a.event.Dimensions.ResponseID, responseID.String(), "provider_response", key) {
				e := liveEvent(a.event, "metadata")
				e.ObservedAt = now.UTC()
				i.Inspector.append(e)
			}
		}
		for _, part := range classifyOutput(root, stream) {
			if a.firstOutput.IsZero() {
				a.firstOutput = now
				first := liveEvent(a.event, "metadata")
				first.ObservedAt = now.UTC()
				first.Measurements = a.measurements(time.Time{})
				i.Inspector.append(first)
			}
			if a.content == nil {
				continue
			}
			if part.kind == "tool_delta" {
				e := liveEvent(a.event, part.kind)
				e.ObservedAt = now.UTC()
				e.ToolName, e.ToolCallID = safeDimension(part.toolName), safeDimension(part.toolCallID)
				e.Text = "[tool arguments omitted]"
				i.Inspector.append(e)
				continue
			}
			buffer := a.content[part.kind]
			if buffer == nil {
				buffer = &captureBuffer{}
				a.content[part.kind] = buffer
			}
			for _, text := range buffer.consume(part.text, !stream) {
				e := liveEvent(a.event, part.kind)
				e.ObservedAt = now.UTC()
				e.Text = text
				e.Truncated = text == "[content omitted: line limit]\n"
				i.Inspector.append(e)
			}
		}
	}
	if !stream || gjson.ValidBytes(payload) {
		visit(payload)
		return
	}
	for _, line := range bytes.Split(payload, []byte("\n")) {
		line = bytes.TrimSpace(line)
		if bytes.HasPrefix(line, []byte("data:")) {
			visit(bytes.TrimSpace(bytes.TrimPrefix(line, []byte("data:"))))
		}
	}
}

func classifyOutput(root gjson.Result, stream bool) []outputObservation {
	var out []outputObservation
	text := func(kind string, value gjson.Result) {
		if value.Type == gjson.String && value.String() != "" {
			out = append(out, outputObservation{kind: kind, text: value.String()})
		}
	}
	tool := func(name, id, arguments gjson.Result) {
		if name.String() != "" || id.String() != "" || arguments.String() != "" {
			out = append(out, outputObservation{kind: "tool_delta", toolName: name.String(), toolCallID: id.String()})
		}
	}
	switch root.Get("type").String() {
	case "response.output_text.delta":
		text("text_delta", root.Get("delta"))
	case "response.reasoning_text.delta", "response.reasoning_summary_text.delta":
		text("reasoning_delta", root.Get("delta"))
	case "response.output_item.added":
		item := root.Get("item")
		if item.Get("type").String() == "function_call" {
			tool(item.Get("name"), item.Get("call_id"), item.Get("arguments"))
		}
	case "response.function_call_arguments.delta":
		tool(gjson.Result{}, root.Get("call_id"), root.Get("delta"))
	case "content_block_start":
		block := root.Get("content_block")
		switch block.Get("type").String() {
		case "text":
			text("text_delta", block.Get("text"))
		case "thinking":
			text("reasoning_delta", block.Get("thinking"))
		case "tool_use":
			tool(block.Get("name"), block.Get("id"), gjson.Result{})
		}
	case "content_block_delta":
		delta := root.Get("delta")
		switch delta.Get("type").String() {
		case "text_delta":
			text("text_delta", delta.Get("text"))
		case "thinking_delta":
			text("reasoning_delta", delta.Get("thinking"))
		case "input_json_delta":
			tool(gjson.Result{}, gjson.Result{}, delta.Get("partial_json"))
		}
	}
	root.Get("choices").ForEach(func(_, choice gjson.Result) bool {
		message := choice.Get("delta")
		if !stream {
			message = choice.Get("message")
		}
		text("text_delta", message.Get("content"))
		text("reasoning_delta", message.Get("reasoning_content"))
		text("reasoning_delta", message.Get("reasoning"))
		message.Get("tool_calls").ForEach(func(_, call gjson.Result) bool {
			tool(call.Get("function.name"), call.Get("id"), call.Get("function.arguments"))
			return true
		})
		return true
	})
	root.Get("candidates").ForEach(func(_, candidate gjson.Result) bool {
		candidate.Get("content.parts").ForEach(func(_, part gjson.Result) bool {
			kind := "text_delta"
			if part.Get("thought").Bool() {
				kind = "reasoning_delta"
			}
			text(kind, part.Get("text"))
			call := part.Get("functionCall")
			if call.Exists() {
				tool(call.Get("name"), call.Get("id"), call.Get("args"))
			}
			return true
		})
		return true
	})
	if !stream {
		blocks := func(content gjson.Result) {
			content.ForEach(func(_, block gjson.Result) bool {
				switch block.Get("type").String() {
				case "text", "output_text":
					text("text_delta", block.Get("text"))
				case "thinking":
					text("reasoning_delta", block.Get("thinking"))
				case "tool_use":
					tool(block.Get("name"), block.Get("id"), block.Get("input"))
				}
				return true
			})
		}
		blocks(root.Get("content"))
		root.Get("output").ForEach(func(_, item gjson.Result) bool {
			blocks(item.Get("content"))
			if item.Get("type").String() == "function_call" {
				tool(item.Get("name"), item.Get("call_id"), item.Get("arguments"))
			}
			if item.Get("type").String() == "reasoning" {
				item.Get("summary").ForEach(func(_, part gjson.Result) bool { text("reasoning_delta", part.Get("text")); return true })
			}
			return true
		})
	}
	return out
}

func (a *attempt) measurements(finished time.Time) *Measurements {
	m := &Measurements{Source: "core_monotonic_v1"}
	if !a.firstOutput.IsZero() {
		m.FirstOutputLatencyMs = ptr(float64(a.firstOutput.Sub(a.started)) / float64(time.Millisecond))
		m.FirstOutputAt = ptr(a.firstOutput.UTC())
	}
	if !finished.IsZero() {
		m.TotalDurationMs = ptr(float64(finished.Sub(a.started)) / float64(time.Millisecond))
		m.FinishedAt = ptr(finished.UTC())
	}
	return m
}

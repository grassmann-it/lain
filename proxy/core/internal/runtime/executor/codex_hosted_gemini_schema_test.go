package executor

import (
	"bytes"
	"context"
	"testing"

	cliproxyexecutor "github.com/router-for-me/CLIProxyAPI/v7/sdk/cliproxy/executor"
	sdktranslator "github.com/router-for-me/CLIProxyAPI/v7/sdk/translator"
	"github.com/tidwall/gjson"
)

// Exercise the actual HTTP request builder, not the bypassed Gemini translator.
func TestCodexCacheHelperHostedGeminiSchema(t *testing.T) {
	e := NewCodexExecutor(nil)
	payload := []byte(`{"tools":[{"type":"function","name":"image_gen__imagegen","parameters":{"type":"object","properties":{"referenced_image_paths":{"type":["array","null"],"items":{"type":"string","description":"absolute normalized path"}}},"required":[]}}],"input":[{"type":"function_call","arguments":"{\"referenced_image_paths\":null}"}]}`)
	for _, model := range []string{"google/gemini-4-argon", "gpt-6-sol", "glm-5.3"} {
		req := cliproxyexecutor.Request{Model: model, Payload: payload}
		_, body, _, err := e.cacheHelper(context.Background(), sdktranslator.FormatOpenAIResponse, "https://api.commandcode.ai/provider/v1/responses", nil, req, payload, payload)
		if err != nil {
			t.Fatal(err)
		}
		if !bytes.Equal([]byte(gjson.GetBytes(payload, "input").Raw), []byte(gjson.GetBytes(body, "input").Raw)) {
			t.Fatal("historical tool arguments changed")
		}
		union := gjson.GetBytes(body, "tools.0.parameters.properties.referenced_image_paths.anyOf")
		if (model == "google/gemini-4-argon") != union.IsArray() {
			t.Fatalf("wrong route normalized: %s", model)
		}
	}
}

func TestCodexCacheHelperHostedMuseSchema(t *testing.T) {
	e := NewCodexExecutor(nil)
	payload := []byte(`{"tools":[{"type":"function","parameters":{"$defs":{"Node":{"type":"object","properties":{"part":{"$ref":"#/$defs/Node"}}}},"properties":{"part":{"$ref":"#/$defs/Node"}}}}],"input":[{"type":"function_call","arguments":"{\"part\":null}"}]}`)
	for _, model := range []string{"meta/muse-spark-1.3", "gpt-6.1-sol", "google/gemini-4-argon"} {
		req := cliproxyexecutor.Request{Model: model, Payload: payload}
		_, body, _, err := e.cacheHelper(context.Background(), sdktranslator.FormatOpenAIResponse, "https://api.commandcode.ai/provider/v1/responses", nil, req, payload, payload)
		if err != nil {
			t.Fatal(err)
		}
		if gjson.GetBytes(body, "input").Raw != gjson.GetBytes(payload, "input").Raw {
			t.Fatal("arguments changed")
		}
		refs := bytes.Contains([]byte(gjson.GetBytes(body, "tools").Raw), []byte(`"$ref"`))
		if refs == (model == "meta/muse-spark-1.3") {
			t.Fatal("wrong route normalized")
		}
	}
}

func TestCodexCacheHelperDirectMiMoNativeSearch(t *testing.T) {
	e := NewCodexExecutor(nil)
	payload := []byte(`{"tools":[{"type":"web_search"},{"type":"function","name":"web_search","parameters":{"type":"object"}}],"input":[{"type":"function_call","arguments":"{\"type\":\"web_search\"}"}]}`)
	for _, model := range []string{"mimo-v2.6-pro", "gpt-6.1-sol", "glm-5.3"} {
		req := cliproxyexecutor.Request{Model: model, Payload: payload}
		_, body, _, err := e.cacheHelper(context.Background(), sdktranslator.FormatOpenAIResponse, "https://token-plan-ams.xiaomimimo.com/v1/responses", nil, req, payload, payload)
		if err != nil {
			t.Fatal(err)
		}
		if gjson.GetBytes(body, "input").Raw != gjson.GetBytes(payload, "input").Raw {
			t.Fatal("historical arguments changed")
		}
		tools := gjson.GetBytes(body, "tools").Array()
		count := len(tools)
		if model == "mimo-v2.6-pro" && count != 1 || model != "mimo-v2.6-pro" && count != 2 {
			t.Fatal("wrong native search scope")
		}
		if tools[count-1].Get("name").String() != "web_search" {
			t.Fatal("ordinary function removed")
		}
	}
}

package helps

import (
	"bytes"
	"testing"

	"github.com/tidwall/gjson"
)

const mimoNativeToolsFixture = `{"model":"mimo-v2.6-pro","tools":[{"type":"web_search"},{"type":"function","name":"web_search","parameters":{"type":"object","properties":{"type":{"const":"web_search"}}}},{"type":"namespace","name":"mcp_search","tools":[{"type":"function","name":"web_search_preview","parameters":{"type":"object"}}]},{"type":"web_search_preview"},{"type":"web_search_preview_2025_03_11"},{"type":"code_interpreter"}],"input":[{"type":"function_call","arguments":"{\"type\":\"web_search\"}"},{"type":"function_call_output","output":"{\"tools\":[{\"type\":\"web_search\"}]}"}],"metadata":{"type":"web_search"}}`

func TestDirectMiMoFiltersOnlyUnsupportedNativeSearch(t *testing.T) {
	in := []byte(mimoNativeToolsFixture)
	out := NormalizeMiMoResponsesNativeTools(in, "mimo-v2.6-pro", "https://token-plan-ams.xiaomimimo.com/v1/responses")
	tools := gjson.GetBytes(out, "tools").Array()
	if len(tools) != 3 || tools[0].Get("type").String() != "function" || tools[1].Get("type").String() != "namespace" || tools[2].Get("type").String() != "code_interpreter" {
		t.Fatal("only native web search declarations may be filtered")
	}
	original := gjson.GetBytes(in, "tools").Array()
	for i, sourceIndex := range []int{1, 2, 5} {
		if tools[i].Raw != original[sourceIndex].Raw {
			t.Fatal("retained declaration changed")
		}
	}
	for _, path := range []string{"input", "metadata", "model"} {
		if gjson.GetBytes(in, path).Raw != gjson.GetBytes(out, path).Raw {
			t.Fatalf("non-declaration data changed: %s", path)
		}
	}
	if !bytes.Equal(out, NormalizeMiMoResponsesNativeTools(out, "mimo-v2.6-pro", "https://token-plan-ams.xiaomimimo.com/v1/responses")) {
		t.Fatal("filter is not idempotent")
	}
}

func TestDirectMiMoOtherRoutesAndFunctionOnlyAreByteIdentical(t *testing.T) {
	in := []byte(mimoNativeToolsFixture)
	for _, route := range [][2]string{
		{"mimo-v2.6-pro", "https://api.commandcode.ai/v1/responses"},
		{"mimo-v2.6-pro", "https://token-plan-ams.xiaomimimo.com.invalid/v1/responses"},
		{"mimo-v2.6-flash", "https://token-plan-ams.xiaomimimo.com/v1/responses"},
		{"gpt-6.1-sol", "https://token-plan-ams.xiaomimimo.com/v1/responses"},
	} {
		if !bytes.Equal(in, NormalizeMiMoResponsesNativeTools(in, route[0], route[1])) {
			t.Fatal("unrelated route changed")
		}
	}
	for _, raw := range []string{`{"tools":[{"type":"function","name":"web_search"}]}`, `{"input":[]}`, `{"tools":[]}`} {
		if !bytes.Equal([]byte(raw), NormalizeMiMoResponsesNativeTools([]byte(raw), "mimo-v2.6-pro", "https://token-plan-ams.xiaomimimo.com/v1/responses")) {
			t.Fatal("request without unsupported native tools changed")
		}
	}
}

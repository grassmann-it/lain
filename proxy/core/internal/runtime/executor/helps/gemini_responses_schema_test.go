package helps

import (
	"bytes"
	"testing"

	"github.com/tidwall/gjson"
)

const hostedGeminiSchemaFixture = `{"tools":[{"type":"namespace","name":"image_gen","tools":[{"type":"function","name":"imagegen","strict":false,"parameters":{"type":"object","properties":{"referenced_image_paths":{"type":["array","null"],"items":{"type":"string","description":"absolute normalized path"}},"prompt":{"type":"string"},"other":{"anyOf":[{"type":"string"},{"type":"null"}],"description":"optional","default":null},"type":{"type":"string"},"literal":{"type":"object","default":{"type":["array","null"],"anyOf":[{}],"items":{"example":"data"}}}},"required":["prompt"],"additionalProperties":false}}]}],"input":[{"type":"function_call","name":"imagegen","arguments":"{\"referenced_image_paths\":null,\"type\":[\"array\",\"null\"]}"},{"type":"function_call_output","output":"{\"anyOf\":[1,2]}"}]}`

func TestHostedGeminiNullableArrayAndAnyOf(t *testing.T) {
	in := []byte(hostedGeminiSchemaFixture)
	out := NormalizeGeminiResponsesToolSchemas(in, "google/gemini-4-argon", "https://api.commandcode.ai/provider/v1/responses")
	root := gjson.ParseBytes(out)
	props := root.Get("tools.0.tools.0.parameters.properties")
	array := props.Get("referenced_image_paths")
	if len(array.Map()) != 1 || !array.Get("anyOf").IsArray() {
		t.Fatalf("union must have no sibling fields: %s", array.Raw)
	}
	if array.Get("anyOf.0.type").String() != "array" || array.Get("anyOf.0.items.type").String() != "string" || array.Get("anyOf.1.type").String() != "null" || array.Get("anyOf.1.items").Exists() {
		t.Fatalf("lost array or explicit null branch: %s", array.Raw)
	}
	if props.Get("other.anyOf.0.description").String() != "optional" || !props.Get("other.anyOf.1.default").Exists() {
		t.Fatal("anyOf annotations were lost")
	}
	if root.Get("tools.0.tools.0.parameters.required").Raw != `["prompt"]` || root.Get("tools.0.tools.0.strict").Bool() {
		t.Fatal("optionality/strict changed")
	}
	for _, path := range []string{"input", "tools.0.tools.0.parameters.properties.literal.default", "tools.0.tools.0.parameters.properties.type"} {
		if !gjson.GetBytes(out, path).Exists() || !bytes.Equal([]byte(gjson.GetBytes(out, path).Raw), []byte(gjson.GetBytes(in, path).Raw)) {
			// Schema serialization can reorder default map keys: compare values.
			if path == "input" || gjson.GetBytes(out, path).Get("type").Raw != gjson.GetBytes(in, path).Get("type").Raw {
				t.Fatalf("data mutated at %s", path)
			}
		}
	}
	if next := NormalizeGeminiResponsesToolSchemas(out, "google/gemini-4-argon", "https://api.commandcode.ai/provider/v1/responses"); !bytes.Equal(out, next) {
		t.Fatal("normalization is not idempotent")
	}
}

func TestHostedGeminiOtherRoutesUnchanged(t *testing.T) {
	in := []byte(hostedGeminiSchemaFixture)
	for _, pair := range [][2]string{
		{"gpt-6-sol", "https://api.commandcode.ai/provider/v1/responses"},
		{"glm-5.3", "https://api.z.ai/api/v1/responses"},
		{"google/gemini-4-argon", "https://api.kilo.ai/api/gateway/responses"},
		{"google/gemini-4-argon", "https://api.commandcode.ai.evil.invalid/responses"},
	} {
		if out := NormalizeGeminiResponsesToolSchemas(in, pair[0], pair[1]); !bytes.Equal(in, out) {
			t.Errorf("unrelated route modified: %v", pair)
		}
	}
}

func TestHostedGeminiPreservesConjoinedUnion(t *testing.T) {
	in := []byte(`{"tools":[{"type":"function","name":"test","parameters":{"type":"object","properties":{"x":{"type":["string","null"],"anyOf":[{"enum":["ok"]},{"type":"null"}]}}}}]}`)
	out := NormalizeGeminiResponsesToolSchemas(in, "google/gemini-4-argon", "https://api.commandcode.ai/responses")
	x := gjson.GetBytes(out, "tools.0.parameters.properties.x")
	if x.Get("anyOf.0.allOf.0.anyOf.0.enum.0").String() != "ok" || x.Get("anyOf.1.type").String() != "null" {
		t.Fatalf("existing union conjunction was lost: %s", x.Raw)
	}
}

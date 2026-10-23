package helps

import (
	"bytes"
	"encoding/json"
	"os"
	"reflect"
	"testing"

	"github.com/tidwall/gjson"
)

func TestHostedMuseOriginalGmailSchema(t *testing.T) {
	schema, err := os.ReadFile("testdata/muse_gmail_recursive_schema.json")
	if err != nil {
		t.Fatal(err)
	}
	in := []byte(`{"tools":[{"type":"namespace","name":"gmail","tools":[{"type":"function","name":"draft","strict":false,"parameters":` + string(schema) + `}]},{"type":"web_search"}],"input":[{"type":"function_call","arguments":"{\"parts\":[{\"mime_type\":\"text/plain\"}]}"}]}`)
	out := NormalizeMuseResponsesToolSchemas(in, "meta/muse-spark-1.3", "https://api.commandcode.ai/provider/v1/responses")
	if bytes.Equal(in, out) || bytes.Contains([]byte(gjson.GetBytes(out, "tools").Raw), []byte(`"$ref"`)) {
		t.Fatal("recursive schema not expanded")
	}
	if !bytes.Equal([]byte(gjson.GetBytes(in, "input").Raw), []byte(gjson.GetBytes(out, "input").Raw)) {
		t.Fatal("historical arguments changed")
	}
	if len(gjson.GetBytes(out, "tools").Array()) != 2 || len(gjson.GetBytes(out, "tools.0.tools").Array()) != 1 || gjson.GetBytes(out, "tools.1.type").String() != "web_search" {
		t.Fatal("tools removed")
	}
	base := "tools.0.tools.0.parameters"
	for _, key := range []string{"required", "additionalProperties"} {
		var before, after any
		_ = json.Unmarshal([]byte(gjson.GetBytes(in, base+"."+key).Raw), &before)
		_ = json.Unmarshal([]byte(gjson.GetBytes(out, base+"."+key).Raw), &after)
		if !reflect.DeepEqual(before, after) {
			t.Fatal("outer constraints changed")
		}
	}
	if gjson.GetBytes(out, "tools.0.tools.0.strict").Bool() {
		t.Fatal("strict changed")
	}
	if next := NormalizeMuseResponsesToolSchemas(out, "meta/muse-spark-1.3", "https://api.commandcode.ai/provider/v1/responses"); !bytes.Equal(out, next) {
		t.Fatal("not idempotent")
	}
}

func TestHostedMuseScopeAndAcyclicSchema(t *testing.T) {
	in := []byte(`{"tools":[{"type":"function","parameters":{"$defs":{"Node":{"type":"object","properties":{"parts":{"type":"array","items":{"$ref":"#/$defs/Node"}}}}},"type":"object","properties":{"node":{"$ref":"#/$defs/Node"},"literal":{"default":{"$ref":"#/$defs/Node"}}}}}],"input":[]}`)
	for _, route := range [][2]string{{"google/gemini-4-argon", "https://api.commandcode.ai/v1"}, {"meta/muse-spark-1.3", "https://api.kilo.ai/v1"}, {"meta/muse-spark-1.3", "https://api.commandcode.ai.invalid/v1"}, {"gpt-6.1-sol", "https://api.commandcode.ai/v1"}} {
		if out := NormalizeMuseResponsesToolSchemas(in, route[0], route[1]); !bytes.Equal(out, in) {
			t.Fatal("unrelated route changed")
		}
	}
	out := NormalizeMuseResponsesToolSchemas(in, "meta/muse-spark-1.3", "https://api.commandcode.ai/v1")
	if gjson.GetBytes(out, "tools.0.parameters.properties.literal.default.$ref").String() != "#/$defs/Node" {
		t.Fatal("literal data interpreted as schema")
	}
	boundary := gjson.GetBytes(out, "tools.0.parameters.properties.node.properties.parts.items")
	if boundary.Get("type").String() != "object" || boundary.Get("properties").Exists() || boundary.Get("description").String() != "See: Node" {
		t.Fatal("cycle must use existing typed fallback")
	}
	acyclic := []byte(`{"tools":[{"type":"function","parameters":{"$defs":{"X":{"type":"string"}},"properties":{"x":{"$ref":"#/$defs/X"}}}}]}`)
	if !bytes.Equal(acyclic, NormalizeMuseResponsesToolSchemas(acyclic, "meta/muse-spark-1.3", "https://api.commandcode.ai/v1")) {
		t.Fatal("acyclic declaration changed")
	}
}

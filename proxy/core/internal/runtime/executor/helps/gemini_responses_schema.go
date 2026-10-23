package helps

import (
	"bytes"
	"encoding/json"
	"net/url"
	"reflect"
	"strings"

	"github.com/tidwall/gjson"
	"github.com/tidwall/sjson"
)

// NormalizeGeminiResponsesToolSchemas handles the hosted Responses-to-Gemini
// converter, which bypasses the native Gemini translator. In particular, that
// converter expands type arrays into any_of but leaves items beside the union.
// Move constraints into the branches before it sees the schema. Unlike the
// native lossy cleaner, this retains every non-null branch and explicit null.
// Only the configured Command Code Gemini model family is affected.
func NormalizeGeminiResponsesToolSchemas(body []byte, model, endpoint string) []byte {
	u, err := url.Parse(endpoint)
	if err != nil || !strings.EqualFold(u.Hostname(), "api.commandcode.ai") || !strings.HasPrefix(model, "google/gemini-") {
		return body
	}
	return normalizeHostedResponsesToolSchemas(body, normalizeGeminiResponseSchema)
}

func normalizeHostedResponsesToolSchemas(body []byte, normalize func(map[string]any) bool) []byte {
	tools := gjson.GetBytes(body, "tools")
	if !tools.IsArray() {
		return body
	}
	var declarations []map[string]any
	decoder := json.NewDecoder(bytes.NewBufferString(tools.Raw))
	decoder.UseNumber()
	if decoder.Decode(&declarations) != nil {
		return body
	}
	changed := false
	var visitTools func([]map[string]any)
	visitTools = func(list []map[string]any) {
		for _, tool := range list {
			if tool["type"] == "namespace" {
				if children, ok := tool["tools"].([]any); ok {
					for _, child := range children {
						if t, ok := child.(map[string]any); ok {
							visitTools([]map[string]any{t})
						}
					}
				}
				continue
			}
			if tool["type"] != "function" {
				continue
			}
			for _, key := range []string{"parameters", "input_schema"} {
				if schema, ok := tool[key].(map[string]any); ok && normalize(schema) {
					changed = true
				}
			}
		}
	}
	visitTools(declarations)
	if !changed {
		return body
	}
	raw, err := json.Marshal(declarations)
	if err != nil {
		return body
	}
	out, err := sjson.SetRawBytes(body, "tools", raw)
	if err != nil {
		return body
	}
	return out
}

// Walk schema locations only: enum/default/examples and property names are data.
func normalizeGeminiResponseSchema(schema map[string]any) bool {
	changed := false
	for _, key := range []string{"properties", "patternProperties", "$defs", "definitions", "dependentSchemas"} {
		if children, ok := schema[key].(map[string]any); ok {
			for _, child := range children {
				if s, ok := child.(map[string]any); ok && normalizeGeminiResponseSchema(s) {
					changed = true
				}
			}
		}
	}
	for _, key := range []string{"items", "additionalProperties", "contains", "not", "if", "then", "else", "propertyNames"} {
		if child, ok := schema[key].(map[string]any); ok && normalizeGeminiResponseSchema(child) {
			changed = true
		}
	}
	for _, key := range []string{"allOf", "anyOf", "oneOf", "prefixItems"} {
		if children, ok := schema[key].([]any); ok {
			for _, child := range children {
				if s, ok := child.(map[string]any); ok && normalizeGeminiResponseSchema(s) {
					changed = true
				}
			}
		}
	}
	if types, ok := schema["type"].([]any); ok && len(types) > 0 {
		branches := make([]any, 0, len(types))
		for _, t := range types {
			if _, ok := t.(string); !ok {
				return changed
			}
			branches = append(branches, map[string]any{"type": t})
		}
		delete(schema, "type")
		// Existing anyOf is a conjunction, so retain it rather than overwrite it.
		if existing, ok := schema["anyOf"]; ok {
			delete(schema, "anyOf")
			all, _ := schema["allOf"].([]any)
			schema["allOf"] = append(all, map[string]any{"anyOf": existing})
		}
		schema["anyOf"] = branches
		changed = true
	}
	if branches, ok := schema["anyOf"].([]any); ok && len(branches) > 0 && len(schema) > 1 {
		siblings := make(map[string]any, len(schema)-1)
		for key, value := range schema {
			if key != "anyOf" {
				siblings[key] = value
			}
		}
		merged := make([]any, 0, len(branches))
		for _, branch := range branches {
			child, ok := branch.(map[string]any)
			if !ok { // Preserve boolean/invalid schemas rather than approximate them.
				return changed
			}
			combined := make(map[string]any, len(siblings)+len(child))
			for k, v := range siblings {
				combined[k] = v
			}
			conflict := false
			for k, v := range child {
				if old, exists := combined[k]; exists && !reflect.DeepEqual(old, v) {
					conflict = true
				}
				combined[k] = v
			}
			if conflict {
				combined = map[string]any{"allOf": []any{siblings, child}}
			}
			// Type-specific keywords are ignored for other JSON instance types.
			// Drop them in the null branch: Gemini's protobuf validator does not
			// permit e.g. items unless type is array, even though JSON Schema does.
			if combined["type"] == "null" {
				for _, k := range []string{"items", "contains", "minItems", "maxItems", "uniqueItems", "properties", "required", "additionalProperties", "minProperties", "maxProperties", "pattern", "minLength", "maxLength", "format", "minimum", "maximum", "multipleOf", "exclusiveMinimum", "exclusiveMaximum"} {
					delete(combined, k)
				}
			}
			normalizeGeminiResponseSchema(combined)
			merged = append(merged, combined)
		}
		for k := range schema {
			delete(schema, k)
		}
		schema["anyOf"] = merged
		changed = true
	}
	return changed
}

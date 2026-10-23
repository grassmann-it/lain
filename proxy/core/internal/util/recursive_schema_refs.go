package util

import (
	"bytes"
	"encoding/json"
	"strings"
)

// InlineRecursiveSchemaRefs uses the existing typed cycle fallback for recursive
// local schema references. Unlike InlineLocalRefs, it visits schema keywords only:
// defaults, enums, examples and other literal data must never be interpreted as schemas.
// Acyclic schemas are returned unchanged. At a cycle boundary, deep validation is
// intentionally relaxed to the referenced type, nullable flag and descriptive hint.
func InlineRecursiveSchemaRefs(raw []byte) ([]byte, bool) {
	var root map[string]any
	d := json.NewDecoder(bytes.NewReader(raw))
	d.UseNumber()
	if d.Decode(&root) != nil {
		return raw, false
	}
	cycle, exhausted, budget := false, false, 8192
	active := make(map[string]bool)
	var resolve func(map[string]any) map[string]any
	resolve = func(node map[string]any) map[string]any {
		budget--
		if budget < 0 {
			exhausted = true
			return node
		}
		if ref, ok := node["$ref"].(string); ok && (ref == "#" || strings.HasPrefix(ref, "#/")) {
			var target any = root
			found := ref == "#"
			if !found {
				target, found = resolveJSONPointer(root, ref)
			}
			if targetMap, ok := target.(map[string]any); found && ok {
				if active[ref] {
					cycle = true
					return cyclicRefFallback(node, target, ref)
				}
				active[ref] = true
				expanded := resolve(targetMap)
				delete(active, ref)
				// Keep sibling constraints as a conjunction, rather than overriding
				// constraints from the referenced schema.
				if len(node) == 1 {
					return expanded
				}
				siblings := make(map[string]any, len(node)-1)
				for key, value := range node {
					if key != "$ref" {
						siblings[key] = value
					}
				}
				return map[string]any{"allOf": []any{expanded, resolve(siblings)}}
			}
		}
		out := make(map[string]any, len(node))
		for key, value := range node {
			out[key] = value
		}
		for _, key := range []string{"properties", "patternProperties", "$defs", "definitions", "dependentSchemas"} {
			if children, ok := node[key].(map[string]any); ok {
				mapped := make(map[string]any, len(children))
				for name, child := range children {
					mapped[name] = child
					if schema, ok := child.(map[string]any); ok {
						mapped[name] = resolve(schema)
					}
				}
				out[key] = mapped
			}
		}
		for _, key := range []string{"items", "additionalProperties", "contains", "not", "if", "then", "else", "propertyNames", "unevaluatedProperties", "unevaluatedItems"} {
			if schema, ok := node[key].(map[string]any); ok {
				out[key] = resolve(schema)
			}
		}
		for _, key := range []string{"allOf", "anyOf", "oneOf", "prefixItems"} {
			if children, ok := node[key].([]any); ok {
				mapped := append([]any(nil), children...)
				for i, child := range children {
					if schema, ok := child.(map[string]any); ok {
						mapped[i] = resolve(schema)
					}
				}
				out[key] = mapped
			}
		}
		return out
	}
	out := resolve(root)
	if !cycle || exhausted {
		return raw, false
	}
	encoded, err := json.Marshal(out)
	if err != nil {
		return raw, false
	}
	return encoded, true
}

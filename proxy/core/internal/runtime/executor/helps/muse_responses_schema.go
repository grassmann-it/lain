package helps

import (
	"bytes"
	"encoding/json"
	"net/url"
	"strings"

	"github.com/router-for-me/CLIProxyAPI/v7/internal/util"
)

// NormalizeMuseResponsesToolSchemas adapts recursive declarations for the hosted
// Muse converter. Tools and historical argument data are retained. Only schemas
// containing an actual local reference cycle are expanded; deep validation at the
// cycle boundary is relaxed using the existing util typed-hint fallback.
func NormalizeMuseResponsesToolSchemas(body []byte, model, endpoint string) []byte {
	u, err := url.Parse(endpoint)
	if err != nil || !strings.EqualFold(u.Hostname(), "api.commandcode.ai") || model != "meta/muse-spark-1.3" {
		return body
	}
	return normalizeHostedResponsesToolSchemas(body, func(schema map[string]any) bool {
		raw, err := json.Marshal(schema)
		if err != nil {
			return false
		}
		normalized, changed := util.InlineRecursiveSchemaRefs(raw)
		if !changed {
			return false
		}
		var replacement map[string]any
		decoder := json.NewDecoder(bytes.NewReader(normalized))
		decoder.UseNumber()
		if decoder.Decode(&replacement) != nil {
			return false
		}
		for key := range schema {
			delete(schema, key)
		}
		for key, value := range replacement {
			schema[key] = value
		}
		return true
	})
}

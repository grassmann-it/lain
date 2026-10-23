package helps

import (
	"bytes"
	"net/url"
	"strings"

	"github.com/tidwall/gjson"
	"github.com/tidwall/sjson"
)

// NormalizeMiMoResponsesNativeTools declares the direct token-plan gateway's
// proven native-tool limit. Native web search is unsupported even in ordinary
// JSON function mode. Remove only these native declarations; identically named
// functions, MCP search, schemas and historical argument data are retained.
// This is a capability restriction, not native web-search emulation.
func NormalizeMiMoResponsesNativeTools(body []byte, model, endpoint string) []byte {
	u, err := url.Parse(endpoint)
	if err != nil || !strings.EqualFold(u.Hostname(), "token-plan-ams.xiaomimimo.com") || model != "mimo-v2.6-pro" {
		return body
	}
	tools := gjson.GetBytes(body, "tools")
	if !tools.IsArray() {
		return body
	}
	kept := make([][]byte, 0)
	changed := false
	tools.ForEach(func(_, tool gjson.Result) bool {
		switch tool.Get("type").String() {
		case "web_search", "web_search_preview", "web_search_preview_2025_03_11":
			changed = true
		default:
			kept = append(kept, []byte(tool.Raw))
		}
		return true
	})
	if !changed {
		return body
	}
	raw := append([]byte{'['}, bytes.Join(kept, []byte{','})...)
	raw = append(raw, ']')
	out, err := sjson.SetRawBytes(body, "tools", raw)
	if err != nil {
		return body
	}
	return out
}

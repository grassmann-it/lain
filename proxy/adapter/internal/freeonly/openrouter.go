// Package freeonly contains the narrow OpenRouter zero-spend dispatch guard.
// It does not create keys, update account policy, or contact inference endpoints.
package freeonly

import (
	"bytes"
	"context"
	"encoding/json"
	"errors"
	"io"
	"net/http"
	"strings"
	"time"
)

const OpenRouterZeroCapV1 = "openrouter_zero_cap_v1"
const openRouterKeyURL = "https://openrouter.ai/api/v1/key"
const maxPayload = 32 << 20

var ErrRejected = errors.New("OpenRouter free-only request rejected")

type pinKey struct{}

func WithOpenRouterPin(ctx context.Context, model string) context.Context {
	return context.WithValue(ctx, pinKey{}, model)
}

func OpenRouterPin(ctx context.Context) (string, bool) {
	model, ok := ctx.Value(pinKey{}).(string)
	// Presence, not qualification: malformed intent must reach the rejecting
	// final guard instead of falling back to ordinary unguarded dispatch.
	return model, ok
}

// IsOpenRouterFreeModel deliberately excludes random routers and suffix guesses.
func IsOpenRouterFreeModel(model string) bool {
	return model == "meta-llama/llama-3.3-70b-instruct:free" || model == "qwen/qwen3-coder:free"
}

func reject(reason string) error { return errors.Join(ErrRejected, errors.New(reason)) }

// ValidateOpenRouterInput runs before alias translation. The final outgoing guard
// remains mandatory because translators and configured payload rules run later.
func ValidateOpenRouterInput(payload []byte, alias, pinnedModel string) error {
	if !IsOpenRouterFreeModel(pinnedModel) {
		return reject("unqualified model pin")
	}
	body, err := object(payload)
	if err != nil {
		return err
	}
	if body["model"] != alias {
		return reject("model route override")
	}
	return validateBody(body, true)
}

// PrepareOpenRouterRequest must run after all payload/header customization and
// immediately before client.Do. It verifies the SAME bearer key afresh, fails
// closed on errors, disables redirects for both proof and inference, and injects
// endpoint price caps. Call only for a selected, validated zero-cap route intent.
// Provider-side zero-cap behavior still requires independent live qualification;
// no stored flag or successful mock establishes live account access.
func PrepareOpenRouterRequest(ctx context.Context, client *http.Client, req *http.Request, pinnedModel string) error {
	if client == nil || req == nil || req.URL == nil || !IsOpenRouterFreeModel(pinnedModel) {
		return reject("missing qualified dispatch")
	}
	u := req.URL
	if req.Method != http.MethodPost || u.Scheme != "https" || u.Host != "openrouter.ai" || u.User != nil || u.Path != "/api/v1/chat/completions" || u.RawPath != "" || u.RawQuery != "" || u.Fragment != "" || (req.Host != "" && req.Host != u.Host) {
		return reject("unqualified inference endpoint")
	}
	for name := range req.Header {
		// net/http permits directly constructed noncanonical map keys. Do not let
		// two differently cased Authorization entries prove one key and send another.
		if name != http.CanonicalHeaderKey(name) || len(req.Header[name]) != 1 {
			return reject("ambiguous upstream header")
		}
		switch http.CanonicalHeaderKey(name) {
		case "Authorization", "Content-Type", "Accept", "User-Agent", "Http-Referer", "X-Title", "X-Openrouter-Title", "X-Request-Id":
		case "Cache-Control":
			if req.Header.Get(name) != "no-cache" {
				return reject("unqualified cache-control header")
			}
		default:
			return reject("unqualified upstream header")
		}
	}
	authorizations := req.Header.Values("Authorization")
	if len(authorizations) != 1 || !strings.HasPrefix(authorizations[0], "Bearer ") {
		return reject("missing inference bearer key")
	}
	apiKey := strings.TrimPrefix(authorizations[0], "Bearer ")
	if apiKey == "" || strings.ContainsAny(apiKey, " \t\r\n") {
		return reject("invalid inference bearer key")
	}
	if req.Body == nil {
		return reject("missing request body")
	}
	payload, err := io.ReadAll(io.LimitReader(req.Body, maxPayload+1))
	_ = req.Body.Close()
	if err != nil || len(payload) > maxPayload {
		return reject("request body unavailable or too large")
	}
	body, err := object(payload)
	if err != nil {
		return err
	}
	if body["model"] != pinnedModel {
		return reject("outgoing model differs from pin")
	}
	if err := validateBody(body, false); err != nil {
		return err
	}
	body["provider"] = map[string]any{"allow_fallbacks": false, "require_parameters": true, "max_price": map[string]any{"prompt": 0, "completion": 0, "request": 0, "image": 0}}
	body["plugins"] = []any{}
	encoded, err := json.Marshal(body)
	if err != nil {
		return reject("request body cannot be encoded")
	}
	// The executor supplies a fresh per-execution client. Mutating a shared client
	// concurrently is unsupported; this constraint is part of the hook contract.
	client.CheckRedirect = func(*http.Request, []*http.Request) error { return http.ErrUseLastResponse }
	client.Jar = nil
	if err := verifyZeroCap(ctx, client, apiKey); err != nil {
		return err
	}
	req.Body = io.NopCloser(bytes.NewReader(encoded))
	req.GetBody = func() (io.ReadCloser, error) { return io.NopCloser(bytes.NewReader(encoded)), nil }
	req.ContentLength = int64(len(encoded))
	req.Header.Set("Content-Type", "application/json")
	return nil
}

func verifyZeroCap(ctx context.Context, client *http.Client, apiKey string) error {
	ctx, cancel := context.WithTimeout(ctx, 5*time.Second)
	defer cancel()
	req, err := http.NewRequestWithContext(ctx, http.MethodGet, openRouterKeyURL, nil)
	if err != nil {
		return reject("key proof request unavailable")
	}
	req.Header.Set("Authorization", "Bearer "+apiKey)
	req.Header.Set("Accept", "application/json")
	req.Header.Set("Cache-Control", "no-cache")
	resp, err := client.Do(req)
	if err != nil {
		return reject("fresh key proof unavailable")
	}
	defer resp.Body.Close()
	if resp.StatusCode != http.StatusOK {
		return reject("fresh key proof refused")
	}
	// Never consume a cached proof, including responses served by intermediary caches.
	if age := resp.Header.Get("Age"); age != "" && age != "0" {
		return reject("cached key proof")
	}
	payload, err := io.ReadAll(io.LimitReader(resp.Body, (1<<20)+1))
	if err != nil || len(payload) > 1<<20 {
		return reject("key proof unavailable or too large")
	}
	root, err := object(payload)
	if err != nil {
		return reject("key proof invalid")
	}
	data, ok := root["data"].(map[string]any)
	if !ok || !zero(data["limit"]) || !zero(data["limit_remaining"]) || data["include_byok_in_limit"] != true || data["is_management_key"] != false {
		return reject("key does not prove a zero USD cap including BYOK")
	}
	return nil
}

func zero(value any) bool {
	number, ok := value.(json.Number)
	if !ok {
		return false
	}
	// Do not round a tiny positive cap such as 1e-1000 down to zero.
	mantissa := strings.SplitN(strings.ToLower(string(number)), "e", 2)[0]
	for _, digit := range mantissa {
		if digit != '0' && digit != '-' && digit != '.' {
			return false
		}
	}
	return true
}

func validateBody(body map[string]any, incoming bool) error {
	for name, value := range body {
		switch name {
		case "model", "messages", "stream", "stream_options", "max_tokens", "max_completion_tokens", "temperature", "top_p", "top_k", "min_p", "frequency_penalty", "presence_penalty", "repetition_penalty", "seed", "stop", "n", "logit_bias", "logprobs", "top_logprobs", "response_format", "parallel_tool_calls", "reasoning", "reasoning_effort", "verbosity", "user":
		case "tool_choice":
			if choice, ok := value.(string); ok {
				if choice != "none" && choice != "auto" && choice != "required" {
					return reject("unqualified tool choice")
				}
			} else {
				choice, ok := value.(map[string]any)
				if !ok || choice["type"] != "function" {
					return reject("gateway tool choice excluded")
				}
				function, ok := choice["function"].(map[string]any)
				if !ok {
					if !incoming {
						return reject("invalid tool choice")
					}
					function = choice
				}
				name, ok := function["name"].(string)
				if !ok || name == "" || strings.ContainsAny(name, ":/") {
					return reject("gateway tool choice excluded")
				}
			}
		case "tools":
			tools, ok := value.([]any)
			if !ok {
				return reject("invalid tools")
			}
			for _, raw := range tools {
				tool, ok := raw.(map[string]any)
				if !ok || tool["type"] != "function" {
					return reject("server tools are excluded")
				}
				function, nested := tool["function"].(map[string]any)
				if !nested {
					if !incoming {
						return reject("invalid function tool")
					}
					function = tool
				}
				name, ok := function["name"].(string)
				if !ok || name == "" || strings.Contains(name, ":") || strings.Contains(name, "/") {
					return reject("gateway tools are excluded")
				}
			}
		case "provider":
			if err := validateProvider(value); err != nil {
				return err
			}
		case "plugins":
			plugins, ok := value.([]any)
			if !ok || len(plugins) != 0 {
				return reject("plugins are excluded")
			}
		case "input", "instructions", "max_output_tokens", "store", "include", "previous_response_id", "metadata", "truncation", "text":
			if !incoming {
				return reject("unqualified outgoing field")
			}
		default:
			return reject("unqualified request extension")
		}
	}
	if messages, exists := body["messages"]; exists {
		list, ok := messages.([]any)
		if !ok {
			return reject("invalid messages")
		}
		for _, raw := range list {
			message, ok := raw.(map[string]any)
			if !ok {
				return reject("invalid message")
			}
			if content, ok := message["content"].([]any); ok {
				for _, raw := range content {
					part, ok := raw.(map[string]any)
					if !ok || part["type"] != "text" {
						return reject("only text input is qualified")
					}
				}
			} else if message["content"] != nil {
				if _, ok := message["content"].(string); !ok {
					return reject("only text input is qualified")
				}
			}
		}
	}
	return nil
}

func validateProvider(value any) error {
	provider, ok := value.(map[string]any)
	if !ok {
		return reject("invalid provider controls")
	}
	for name, value := range provider {
		switch name {
		case "allow_fallbacks":
			if value != false {
				return reject("provider fallback override")
			}
		case "require_parameters":
			if value != true {
				return reject("provider parameter override")
			}
		case "max_price":
			prices, ok := value.(map[string]any)
			if !ok {
				return reject("invalid price controls")
			}
			for kind, price := range prices {
				if (kind != "prompt" && kind != "completion" && kind != "request" && kind != "image") || !zero(price) {
					return reject("nonzero or unknown price override")
				}
			}
		default:
			return reject("unqualified provider override")
		}
	}
	return nil
}

// Decode unique keys recursively: different JSON parsers must not disagree on a
// duplicate model/provider/key-cap property. Caller and provider data both use it.
func object(payload []byte) (map[string]any, error) {
	if len(payload) == 0 || len(payload) > maxPayload {
		return nil, reject("invalid JSON body size")
	}
	decoder := json.NewDecoder(bytes.NewReader(payload))
	decoder.UseNumber()
	value, err := uniqueValue(decoder, 0)
	if err != nil {
		return nil, reject("invalid or duplicate JSON property")
	}
	if _, err := decoder.Token(); err != io.EOF {
		return nil, reject("multiple JSON values")
	}
	root, ok := value.(map[string]any)
	if !ok {
		return nil, reject("JSON object required")
	}
	return root, nil
}

func uniqueValue(decoder *json.Decoder, depth int) (any, error) {
	if depth > 64 {
		return nil, ErrRejected
	}
	token, err := decoder.Token()
	if err != nil {
		return nil, err
	}
	switch token {
	case json.Delim('{'):
		result := map[string]any{}
		for decoder.More() {
			token, err := decoder.Token()
			if err != nil {
				return nil, err
			}
			key, ok := token.(string)
			if !ok {
				return nil, ErrRejected
			}
			if _, exists := result[key]; exists {
				return nil, ErrRejected
			}
			result[key], err = uniqueValue(decoder, depth+1)
			if err != nil {
				return nil, err
			}
		}
		end, err := decoder.Token()
		if err != nil || end != json.Delim('}') {
			return nil, ErrRejected
		}
		return result, nil
	case json.Delim('['):
		result := []any{}
		for decoder.More() {
			value, err := uniqueValue(decoder, depth+1)
			if err != nil {
				return nil, err
			}
			result = append(result, value)
		}
		end, err := decoder.Token()
		if err != nil || end != json.Delim(']') {
			return nil, ErrRejected
		}
		return result, nil
	default:
		if _, delimiter := token.(json.Delim); delimiter {
			return nil, ErrRejected
		}
		return token, nil
	}
}

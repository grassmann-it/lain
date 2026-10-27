package lain

import (
	"context"
	"encoding/hex"
	"encoding/json"
	"errors"
	"io"
	"net/http"
	"strings"

	"github.com/google/uuid"
	"github.com/router-for-me/CLIProxyAPI/v8/sdk/cliproxy"
	auth "github.com/router-for-me/CLIProxyAPI/v8/sdk/cliproxy/auth"
	executor "github.com/router-for-me/CLIProxyAPI/v8/sdk/cliproxy/executor"
	"github.com/router-for-me/CLIProxyAPI/v8/sdk/translator"
	"lain.local/proxy/internal/freeonly"
)

type CollectionRouter struct {
	ID                 string            `json:"id"`
	ProfileID          string            `json:"profileId"`
	Selection          string            `json:"selection"`
	CostPolicy         string            `json:"costPolicy"`
	RouteIDs           []string          `json:"routeIds"`
	MemberFingerprints map[string]string `json:"memberFingerprints"`
}

func validRouterThread(value string) bool {
	parsed, err := uuid.Parse(value)
	return err == nil && parsed.String() == value
}
func validRouterFingerprint(value string) bool {
	raw, err := hex.DecodeString(value)
	return err == nil && len(raw) == 32 && strings.ToLower(value) == value
}

func validateRouters(b Bindings) error {
	ids := map[string]bool{}
	routes := map[string]Route{}
	for _, route := range b.Routes {
		if _, duplicate := routes[route.ID]; duplicate && len(b.Routers) > 0 {
			return errors.New("router route identities must be unique")
		}
		routes[route.ID] = route
	}
	for alias, router := range b.Routers {
		_, collision := b.Routes[alias]
		if alias == "" || collision || ids[router.ID] || safeDimension(router.ID) == "" || safeDimension(router.ProfileID) == "" ||
			router.Selection != "ordered_sticky_v1" || (router.CostPolicy != "configured" && router.CostPolicy != "free_only") || len(router.RouteIDs) == 0 ||
			len(router.MemberFingerprints) != len(router.RouteIDs) {
			return errors.New("invalid collection router binding")
		}
		ids[router.ID] = true
		seen := map[string]bool{}
		for _, id := range router.RouteIDs {
			route, found := routes[id]
			if !found || seen[id] || !validRouterFingerprint(router.MemberFingerprints[id]) || route.Selection != "strict" || len(route.Accounts) != 1 ||
				(route.Visible != nil && !*route.Visible) || (route.UpstreamProtocol != "openai" && route.UpstreamProtocol != "responses") ||
				(router.CostPolicy == "free_only" && route.CostPolicy != "free_only") || validateCostPolicy(route) != nil {
				return errors.New("unqualified collection router member")
			}
			seen[id] = true
		}
	}
	return nil
}

// This is deliberately a complete-history Responses contract. A router cannot
// infer provenance for an upstream stored response or adopt another model's history.
func routerRequest(body []byte, headers http.Header) (string, bool, error) {
	values := headers.Values("Thread-Id")
	if len(values) != 1 || !validRouterThread(values[0]) {
		return "", false, errors.New("router requires one canonical Thread-Id UUID")
	}
	fields, err := toolControlFields(body, "model", "input", "store", "previous_response_id", "conversation", "tools", "tool_choice")
	if err != nil {
		return "", false, err
	}
	for _, name := range []string{"previous_response_id", "conversation"} {
		if _, present := fields[name]; present {
			return "", false, errors.New("router requires full history without stored response references")
		}
	}
	if raw, present := fields["store"]; present && string(raw) != "false" {
		return "", false, errors.New("router requires store:false")
	}
	var input string
	if len(fields["input"]) > 0 && fields["input"][0] == '"' && json.Unmarshal(fields["input"], &input) == nil {
		return values[0], false, nil
	}
	var items []map[string]json.RawMessage
	if json.Unmarshal(fields["input"], &items) != nil || len(items) == 0 {
		return "", false, errors.New("router requires Responses input")
	}
	continuation := false
	for _, item := range items {
		var kind, role string
		_ = json.Unmarshal(item["type"], &kind)
		_ = json.Unmarshal(item["role"], &role)
		if kind == "item_reference" {
			return "", false, errors.New("router does not accept stored item references")
		}
		if kind != "" && kind != "message" || role != "user" && role != "system" && role != "developer" {
			continuation = true
		}
	}
	return values[0], continuation, nil
}

func (i *Integration) routerRoute(router CollectionRouter, alias string, body []byte, headers http.Header, manager *auth.Manager) (Route, error) {
	if i.routerPinError != nil || i.routerPins == nil {
		return Route{}, errors.New("persistent router state unavailable; explicit recovery required")
	}
	thread, continuation, err := routerRequest(body, headers)
	if err != nil {
		return Route{}, err
	}
	byID, eligible := i.routerCandidates(router, alias, body, headers, manager)
	pin, err := i.routerPins.resolve(router, thread, continuation, func() (routerPin, error) {
		quotaBlocked := false
		for _, id := range router.RouteIDs {
			route := byID[id]
			if eligible(route) == nil {
				if i.routerMemberBlocked(router, route, manager) {
					quotaBlocked = true
					continue
				}
				return routerPin{RouteID: id, AccountID: route.Accounts[0], Fingerprint: router.MemberFingerprints[id]}, nil
			}
		}
		if quotaBlocked {
			return routerPin{}, errRouterExhausted
		}
		return routerPin{}, errors.New("no qualified available router member")
	}, func(pin routerPin) error {
		fingerprint, member := router.MemberFingerprints[pin.RouteID]
		route, exists := byID[pin.RouteID]
		if !member || !exists || fingerprint != pin.Fingerprint || len(route.Accounts) != 1 || route.Accounts[0] != pin.AccountID {
			return errors.New("pinned router member changed or was removed; start a new thread")
		}
		return eligible(route)
	})
	if err != nil {
		return Route{}, err
	}
	return byID[pin.RouteID], nil
}

func (i *Integration) routerCandidates(router CollectionRouter, alias string, body []byte, headers http.Header, manager *auth.Manager) (map[string]Route, func(Route) error) {
	byID := map[string]Route{}
	for _, route := range i.Bindings.Routes {
		byID[route.ID] = route
	}
	loaded := map[string]*auth.Auth{}
	for _, credential := range manager.List() {
		loaded[credential.ID] = credential
	}
	eligible := func(route Route) error {
		if account := headers.Get("X-Lain-Account-ID"); account != "" && account != route.Accounts[0] {
			return errors.New("account pin differs from router member")
		}
		credential := loaded[i.Credential(route, route.Accounts[0])]
		if credential == nil || credential.Disabled {
			return errors.New("router member credential unavailable")
		}
		registered := false
		for _, model := range cliproxy.GlobalModelRegistry().GetModelsForClient(credential.ID) {
			if model.ID == route.Model {
				registered = true
			}
		}
		if !registered {
			return errors.New("router member model unavailable")
		}
		if route.CostPolicy == "free_only" {
			if !qualifiedOpenRouterAuth(route, credential, route.Model) {
				return errors.New("router free-only transport unavailable")
			}
			if err := freeonly.ValidateOpenRouterInput(body, alias, route.Model); err != nil {
				return err
			}
		}
		// Reuse the exact documented negative constraints. Unknown capabilities
		// stay unknown; no synthetic capability or upstream success is asserted.
		policy := compatibilityPolicyForRoute(route, true)
		if err := policy.validate(body, false); err != nil {
			return err
		}
		return validateCohereInput(credential, executor.Request{Model: route.Model, Payload: body}, executor.Options{OriginalRequest: body, SourceFormat: translator.FormatOpenAIResponse})
	}
	return byID, eligible
}

func (i *Integration) isRouterDispatch(ctx context.Context) bool {
	selected, ok := routeSelection(ctx)
	if !ok {
		return false
	}
	_, ok = i.Bindings.Routers[selected.Alias]
	return ok
}

func (i *Integration) validateRouterFinal(req *http.Request) error {
	if !i.isRouterDispatch(req.Context()) {
		return nil
	}
	selected, _ := routeSelection(req.Context())
	endpoint := "/responses"
	if selected.Route.UpstreamProtocol == "openai" {
		endpoint = "/chat/completions"
	}
	if req.Method != http.MethodPost || !strings.HasSuffix(req.URL.Path, endpoint) || req.GetBody == nil {
		return closed(errors.New("router final transport cannot be verified"))
	}
	body, err := req.GetBody()
	if err != nil {
		return closed(err)
	}
	defer body.Close()
	raw, err := io.ReadAll(io.LimitReader(body, 32*1024*1024+1))
	if err != nil || len(raw) > 32*1024*1024 {
		return closed(errors.New("router final body cannot be verified"))
	}
	fields, err := toolControlFields(raw, "model")
	var model string
	if err != nil || json.Unmarshal(fields["model"], &model) != nil || model != selected.Route.Model {
		return closed(errors.New("router final model differs from pinned route"))
	}
	return nil
}

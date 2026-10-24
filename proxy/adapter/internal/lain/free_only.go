package lain

import (
	"context"
	"errors"
	"strings"

	"lain.local/proxy/internal/freeonly"

	auth "github.com/router-for-me/CLIProxyAPI/v8/sdk/cliproxy/auth"
	executor "github.com/router-for-me/CLIProxyAPI/v8/sdk/cliproxy/executor"
)

// Qualification is intentionally narrow. This permits requesting the runtime
// guard; it does not attest that an account currently has a zero cap. The final
// outgoing HTTP guard must prove that afresh for the actual bearer key.
func validateCostPolicy(route Route) error {
	switch route.CostPolicy {
	case "", "configured":
		return nil
	case "free_only":
		if route.Provider == "openrouter" && route.PricingClass == "free" && route.FreeOnlyEnforcement == freeonly.OpenRouterZeroCapV1 && freeonly.IsOpenRouterFreeModel(route.Model) {
			return nil
		}
		return errors.New("free_only route has no qualified runtime enforcement")
	default:
		return errors.New("unknown route cost policy")
	}
}

// DispatchContext runs at the executor boundary because ordinary handlers do
// not copy arbitrary values from the HTTP request context. Only trusted route
// selection can create this marker; headers/body cannot request or erase it.
func (i *Integration) DispatchContext(ctx context.Context, a *auth.Auth, req executor.Request, _ executor.Options) (context.Context, error) {
	selected, ok := routeSelection(ctx)
	if !ok {
		return ctx, closed(errors.New("dispatch route missing"))
	}
	if err := validateCostPolicy(selected.Route); err != nil {
		return ctx, closed(err)
	}
	if selected.Route.CostPolicy != "free_only" {
		return ctx, nil
	}
	if !qualifiedOpenRouterAuth(selected.Route, a, req.Model) {
		return ctx, closed(errors.New("free_only credential transport does not match the qualified route"))
	}
	return freeonly.WithOpenRouterPin(ctx, selected.Route.Model), nil
}

func qualifiedOpenRouterAuth(route Route, a *auth.Auth, model string) bool {
	return a != nil && model == route.Model &&
		a.Provider == "openai-compatible-openrouter-openai-chat" &&
		a.Attributes["compat_name"] == "openrouter-openai-chat" &&
		a.Attributes["upstream_protocol"] == "openai-chat" &&
		strings.TrimSuffix(a.Attributes["base_url"], "/") == "https://openrouter.ai/api/v1"
}

func validateCostDispatch(ctx context.Context, route Route, a *auth.Auth, model string) error {
	if err := validateCostPolicy(route); err != nil {
		return err
	}
	if route.CostPolicy != "free_only" {
		return nil
	}
	pin, ok := freeonly.OpenRouterPin(ctx)
	if !ok || pin != route.Model || !qualifiedOpenRouterAuth(route, a, model) {
		return errors.New("free_only dispatch is missing its qualified execution guard")
	}
	return nil
}

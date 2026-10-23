package executor

import (
	"context"
	"net/http"

	"github.com/router-for-me/CLIProxyAPI/v7/internal/freeonly"
)

// Every compatibility HTTP path reaches this boundary after translation,
// payload overrides and auth/header application. Unknown or billable paths fail
// the explicit free-only guard; ordinary configured routes are unchanged.
func doOpenAICompatRequest(ctx context.Context, client *http.Client, request *http.Request) (*http.Response, error) {
	pinned, freeOnly := freeonly.OpenRouterPin(ctx)
	if !freeOnly {
		return client.Do(request)
	}
	bounded := *client
	bounded.CheckRedirect = func(*http.Request, []*http.Request) error { return http.ErrUseLastResponse }
	bounded.Jar = nil
	if err := freeonly.PrepareOpenRouterRequest(ctx, &bounded, request, pinned); err != nil {
		return nil, err
	}
	return bounded.Do(request)
}

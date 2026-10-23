package executor

import (
	"context"
	"net/http"

	"github.com/router-for-me/CLIProxyAPI/v7/internal/freeonly"
)

// Every compatibility HTTP path reaches this boundary after translation,
// payload overrides and auth/header application. Unknown or billable paths fail
// the explicit free-only guard; ordinary configured routes are unchanged.
func doOpenAICompatRequest(ctx context.Context, client *http.Client, request *http.Request, tracking ...func(*http.Client) *http.Client) (*http.Response, error) {
	track := func(client *http.Client) *http.Client {
		if len(tracking) > 0 {
			return tracking[0](client)
		}
		return client
	}
	pinned, freeOnly := freeonly.OpenRouterPin(ctx)
	if !freeOnly {
		return track(client).Do(request)
	}
	bounded := *client
	bounded.CheckRedirect = func(*http.Request, []*http.Request) error { return http.ErrUseLastResponse }
	bounded.Jar = nil
	if err := freeonly.PrepareOpenRouterRequest(ctx, &bounded, request, pinned); err != nil {
		return nil, err
	}
	// The balance proof is not an inference response and must not set first-byte/token metrics.
	return track(&bounded).Do(request)
}

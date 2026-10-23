package executor

import (
	"context"
	"net/http"
	"strings"

	"github.com/router-for-me/CLIProxyAPI/v7/internal/util"
	cliproxyauth "github.com/router-for-me/CLIProxyAPI/v7/sdk/cliproxy/auth"
)

// A documented mandatory session header cannot be silently omitted when a
// client provides no conversation identity. Do not invent relationships here.
func validateProviderSession(ctx context.Context, auth *cliproxyauth.Auth) error {
	if auth == nil {
		return nil
	}
	required := false
	for name, value := range auth.Attributes {
		if strings.EqualFold(name, "header:x-opencode-session") && value == "$CPA-SESSION-ID" {
			required = true
		}
	}
	if !required {
		return nil
	}
	session := util.SessionIDFromContext(ctx)
	if session == "" || len(session) > 256 {
		return statusErr{code: http.StatusBadRequest, msg: "provider requires a stable conversation session identifier"}
	}
	for _, value := range session {
		if value < 0x21 || value > 0x7e {
			return statusErr{code: http.StatusBadRequest, msg: "invalid provider conversation session identifier"}
		}
	}
	return nil
}

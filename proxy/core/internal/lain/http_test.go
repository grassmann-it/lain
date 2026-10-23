package lain

import (
	"github.com/gin-gonic/gin"
	auth "github.com/router-for-me/CLIProxyAPI/v7/sdk/cliproxy/auth"
	"net/http/httptest"
	"strings"
	"testing"
)

func TestHTTPRoutesAuthenticateAndBindExactMembership(t *testing.T) {
	gin.SetMode(gin.TestMode)
	i, _, _ := dispatchFixture(t)
	manager := auth.NewManager(nil, nil, nil)
	g := gin.New()
	g.Use(i.Middleware("management-token", manager, "inference-token"))
	called := 0
	g.POST("/v1/chat/completions", func(c *gin.Context) {
		called++
		if c.GetString("lain.pinned_auth_id") != "credential-v2" {
			t.Error("HTTP pin missing")
		}
		c.Status(200)
	})
	for _, test := range []struct {
		path, method, body, token, account string
		want                               int
	}{
		{"/v1/models", "GET", "", "inference-token", "", 200},
		{"/v1/models", "GET", "", "management-token", "", 401},
		{"/lain/events", "GET", "", "inference-token", "", 401},
		{"/lain/events", "GET", "", "management-token", "", 200},
		{"/v1/chat/completions", "POST", `{"model":"alias"}`, "inference-token", "", 200},
		{"/v1/chat/completions", "POST", `{"model":"alias"}`, "inference-token", "foreign", 403},
		{"/v1/chat/completions", "POST", `{"model":"model"}`, "inference-token", "", 400},
		{"/v0/management/auth-files", "GET", "", "management-token", "", 404},
	} {
		request := httptest.NewRequest(test.method, test.path, strings.NewReader(test.body))
		request.Header.Set("Authorization", "Bearer "+test.token)
		request.Header.Set("X-Lain-Account-ID", test.account)
		recorder := httptest.NewRecorder()
		g.ServeHTTP(recorder, request)
		if recorder.Code != test.want {
			t.Errorf("%s %d want %d: %s", test.path, recorder.Code, test.want, recorder.Body.String())
		}
		if test.path == "/v1/models" && recorder.Code == 200 {
			if !strings.Contains(recorder.Body.String(), `"id":"alias"`) || strings.Contains(recorder.Body.String(), `"id":"model"`) {
				t.Fatal(recorder.Body.String())
			}
		}
	}
	if called != 1 {
		t.Fatal(called)
	}
}

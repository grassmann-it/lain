package lain

import (
	"encoding/json"
	"net/http/httptest"
	"strings"
	"testing"
	"time"

	"github.com/gin-gonic/gin"
	auth "github.com/router-for-me/CLIProxyAPI/v8/sdk/cliproxy/auth"
)

func TestDrainTracksWholeHTTPHandlerAndResumes(t *testing.T) {
	gin.SetMode(gin.TestMode)
	i, _, _ := dispatchFixture(t)
	g := gin.New()
	g.Use(i.Middleware("management", auth.NewManager(nil, nil, nil), "inference"))
	entered, release, done := make(chan struct{}), make(chan struct{}), make(chan struct{})
	g.POST("/v1/responses", func(c *gin.Context) {
		// This deliberately outlives any executor: admission must remain active
		// until the HTTP stream's last write and handler return.
		c.Writer.WriteHeader(200)
		c.Writer.Flush()
		close(entered)
		<-release
		_, _ = c.Writer.Write([]byte("data: [DONE]\n\n"))
	})
	request := func(method, path, token, body string) *httptest.ResponseRecorder {
		r := httptest.NewRequest(method, path, strings.NewReader(body))
		r.Header.Set("Authorization", "Bearer "+token)
		w := httptest.NewRecorder()
		g.ServeHTTP(w, r)
		return w
	}
	activity := func(method, path string, draining bool, active int) {
		t.Helper()
		w := request(method, path, "management", "")
		var got struct {
			Draining bool `json:"draining"`
			Active   int  `json:"activeRequests"`
		}
		if w.Code != 200 || json.Unmarshal(w.Body.Bytes(), &got) != nil || got.Draining != draining || got.Active != active || w.Header().Get("Cache-Control") != "no-store" {
			t.Fatalf("%s %s: %d %s", method, path, w.Code, w.Body.String())
		}
	}
	go func() { defer close(done); request("POST", "/v1/responses", "inference", `{"model":"alias"}`) }()
	select {
	case <-entered:
	case <-time.After(5 * time.Second):
		t.Fatal("handler did not start")
	}
	activity("GET", "/lain/activity", false, 1)
	for _, path := range []string{"/lain/drain", "/lain/activity"} {
		if w := request("POST", path, "inference", ""); w.Code != 401 {
			t.Fatalf("unauthenticated management %s: %d", path, w.Code)
		}
	}
	activity("POST", "/lain/drain", true, 1)
	activity("POST", "/lain/drain", true, 1)
	for _, path := range []string{"/v1/responses", "/v1/responses/compact", "/v1/chat/completions", "/v1/messages"} {
		if w := request("POST", path, "inference", `{"model":"alias"}`); w.Code != 503 || !strings.Contains(w.Body.String(), "runtime_draining") || w.Header().Get("Retry-After") != "1" {
			t.Fatalf("not drained %s: %d %s", path, w.Code, w.Body.String())
		}
	}
	activity("GET", "/lain/activity", true, 1)
	if w := request("GET", "/v1/models", "inference", ""); w.Code != 200 {
		t.Fatal("models blocked by drain")
	}
	if w := request("GET", "/lain/events", "management", ""); w.Code != 200 {
		t.Fatal("replay blocked by drain")
	}
	close(release)
	select {
	case <-done:
	case <-time.After(5 * time.Second):
		t.Fatal("handler did not return")
	}
	activity("GET", "/lain/activity", true, 0)
	activity("DELETE", "/lain/drain", false, 0)
	// An invalid request is admitted again and decremented on its early return.
	if w := request("POST", "/v1/responses", "inference", `{}`); w.Code != 400 {
		t.Fatalf("resume: %d", w.Code)
	}
	activity("GET", "/lain/activity", false, 0)
	if w := request("GET", "/lain/drain", "management", ""); w.Code != 405 {
		t.Fatal(w.Code)
	}
	if w := request("DELETE", "/lain/activity", "management", ""); w.Code != 405 {
		t.Fatal(w.Code)
	}
}

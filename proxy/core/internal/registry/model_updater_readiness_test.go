package registry

import (
	"context"
	"net/http"
	"net/http/httptest"
	"testing"
	"time"
)

func TestWaitForInitialModelsRefreshWithoutUpdater(t *testing.T) {
	resetInitialModelsRefreshForTest(t)

	if err := WaitForInitialModelsRefresh(context.Background()); err != nil {
		t.Fatalf("WaitForInitialModelsRefresh() = %v, want nil when updater is disabled", err)
	}
}

func TestWaitForInitialModelsRefreshContinuesAfterCompletion(t *testing.T) {
	resetInitialModelsRefreshForTest(t)
	startInitialModelsRefreshForTest()

	ctx, cancel := context.WithTimeout(context.Background(), 20*time.Millisecond)
	defer cancel()
	if err := WaitForInitialModelsRefresh(ctx); err == nil {
		t.Fatal("WaitForInitialModelsRefresh() = nil before startup refresh completes")
	}

	completeInitialModelsRefresh()
	if err := WaitForInitialModelsRefresh(context.Background()); err != nil {
		t.Fatalf("WaitForInitialModelsRefresh() = %v, want nil after startup attempt", err)
	}
}

func TestTryRefreshModelsReturnsFetchFailure(t *testing.T) {
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, _ *http.Request) {
		w.WriteHeader(http.StatusServiceUnavailable)
	}))
	defer server.Close()

	oldURLs := modelsURLs
	modelsURLs = []string{server.URL}
	t.Cleanup(func() { modelsURLs = oldURLs })

	if err := tryRefreshModels(context.Background(), "test model refresh"); err == nil {
		t.Fatal("tryRefreshModels() = nil, want fetch failure")
	}
}

func TestInitialModelsRefreshFailureDoesNotBlockServing(t *testing.T) {
	resetInitialModelsRefreshForTest(t)
	startInitialModelsRefreshForTest()

	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, _ *http.Request) {
		w.WriteHeader(http.StatusServiceUnavailable)
	}))
	defer server.Close()

	oldURLs := modelsURLs
	modelsURLs = []string{server.URL}
	t.Cleanup(func() { modelsURLs = oldURLs })

	if errStartup := tryStartupRefresh(context.Background()); errStartup == nil {
		t.Fatal("tryStartupRefresh() = nil, want remote fetch failure")
	}
	completeInitialModelsRefresh()
	if errWait := WaitForInitialModelsRefresh(context.Background()); errWait != nil {
		t.Fatalf("WaitForInitialModelsRefresh() = %v, want nil so serving can continue with current catalog", errWait)
	}
}

func TestTryStartupRefreshPropagatesContextCancellation(t *testing.T) {
	ctx, cancel := context.WithCancel(context.Background())
	cancel()

	oldURLs := modelsURLs
	modelsURLs = []string{"http://127.0.0.1:1/models.json"}
	t.Cleanup(func() { modelsURLs = oldURLs })

	if err := tryStartupRefresh(ctx); err == nil {
		t.Fatal("tryStartupRefresh() = nil, want context cancellation")
	}
}

func resetInitialModelsRefreshForTest(t *testing.T) {
	t.Helper()
	oldStarted := initialModelsRefreshStarted
	oldDone := initialModelsRefreshDone
	oldClosed := initialModelsRefreshClosed
	t.Cleanup(func() {
		initialModelsRefreshMu.Lock()
		initialModelsRefreshStarted = oldStarted
		initialModelsRefreshDone = oldDone
		initialModelsRefreshClosed = oldClosed
		initialModelsRefreshMu.Unlock()
	})

	initialModelsRefreshMu.Lock()
	initialModelsRefreshStarted = false
	initialModelsRefreshDone = nil
	initialModelsRefreshClosed = false
	initialModelsRefreshMu.Unlock()
}

func startInitialModelsRefreshForTest() {
	initialModelsRefreshMu.Lock()
	initialModelsRefreshStarted = true
	initialModelsRefreshDone = make(chan struct{})
	initialModelsRefreshMu.Unlock()
}

package cliproxy

import (
	"context"
	"errors"
	"net"
	"os"
	"path/filepath"
	"testing"
	"time"

	"github.com/router-for-me/CLIProxyAPI/v7/internal/pluginhost"
	"github.com/router-for-me/CLIProxyAPI/v7/internal/registry"
	coreauth "github.com/router-for-me/CLIProxyAPI/v7/sdk/cliproxy/auth"
	"github.com/router-for-me/CLIProxyAPI/v7/sdk/config"
)

func TestPrepareModelRuntimeForServingWaitsBeforeRegisteringModels(t *testing.T) {
	manager := coreauth.NewManager(nil, nil, nil)
	auth := &coreauth.Auth{
		ID:       "startup-readiness-auth",
		Provider: "codex",
		Status:   coreauth.StatusActive,
	}
	if _, errRegister := manager.Register(context.Background(), auth); errRegister != nil {
		t.Fatalf("failed to register auth: %v", errRegister)
	}
	t.Cleanup(func() { registry.GetGlobalRegistry().UnregisterClient(auth.ID) })

	entered := make(chan struct{})
	release := make(chan struct{})
	service := &Service{
		cfg:         &config.Config{},
		coreManager: manager,
		pluginHost:  pluginhost.New(),
	}
	service.waitInitialModelsRefresh = func(context.Context) error {
		close(entered)
		<-release
		return nil
	}

	prepareDone := make(chan error, 1)
	go func() { prepareDone <- service.prepareModelRuntimeForServing(context.Background()) }()

	select {
	case <-entered:
	case <-time.After(time.Second):
		t.Fatal("timed out waiting for initial model refresh barrier")
	}
	if models := registry.GetGlobalRegistry().GetModelsForClient(auth.ID); len(models) != 0 {
		t.Fatalf("models registered before initial refresh completed: %d", len(models))
	}

	close(release)
	select {
	case errPrepare := <-prepareDone:
		if errPrepare != nil {
			t.Fatalf("prepareModelRuntimeForServing() = %v, want nil", errPrepare)
		}
	case <-time.After(5 * time.Second):
		t.Fatal("timed out waiting for model runtime preparation")
	}
	if models := registry.GetGlobalRegistry().GetModelsForClient(auth.ID); len(models) == 0 {
		t.Fatal("expected auth models to be registered after initial refresh completed")
	}
}

func TestRunDoesNotInvokeBeforeStartUntilInitialModelsRefreshCompletes(t *testing.T) {
	listener, errListen := net.Listen("tcp", "127.0.0.1:0")
	if errListen != nil {
		t.Fatalf("failed to reserve port: %v", errListen)
	}
	port := listener.Addr().(*net.TCPAddr).Port
	if errClose := listener.Close(); errClose != nil {
		t.Fatalf("failed to close reserved port: %v", errClose)
	}

	authDir := t.TempDir()
	cfg := &config.Config{
		Host:    "127.0.0.1",
		Port:    port,
		AuthDir: authDir,
	}
	cfgPath := filepath.Join(authDir, "config.yaml")
	if errWrite := os.WriteFile(cfgPath, []byte("{}"), 0o600); errWrite != nil {
		t.Fatalf("failed to write test config: %v", errWrite)
	}

	entered := make(chan struct{})
	release := make(chan struct{})
	beforeStart := make(chan struct{})
	service, errBuild := NewBuilder().
		WithConfig(cfg).
		WithConfigPath(cfgPath).
		WithHooks(Hooks{OnBeforeStart: func(*config.Config) {
			close(beforeStart)
		}}).
		Build()
	if errBuild != nil {
		t.Fatalf("failed to build service: %v", errBuild)
	}
	service.waitInitialModelsRefresh = func(context.Context) error {
		close(entered)
		<-release
		return nil
	}

	runCtx, cancel := context.WithCancel(context.Background())
	defer cancel()
	runDone := make(chan error, 1)
	go func() { runDone <- service.Run(runCtx) }()

	select {
	case <-entered:
	case <-time.After(time.Second):
		t.Fatal("service did not reach initial model refresh barrier")
	}
	select {
	case <-beforeStart:
		t.Fatal("OnBeforeStart ran before initial model refresh completed")
	case <-time.After(30 * time.Millisecond):
	}

	close(release)
	select {
	case <-beforeStart:
	case <-time.After(5 * time.Second):
		t.Fatal("OnBeforeStart did not run after initial model refresh completed")
	}
	cancel()

	select {
	case errRun := <-runDone:
		if errRun != nil && !errors.Is(errRun, context.Canceled) {
			t.Fatalf("service.Run() = %v, want canceled or nil", errRun)
		}
	case <-time.After(10 * time.Second):
		t.Fatal("timed out waiting for service.Run to exit")
	}
}

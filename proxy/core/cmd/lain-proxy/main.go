package main

import (
	"context"
	"encoding/json"
	"errors"
	"flag"
	"fmt"
	"github.com/router-for-me/CLIProxyAPI/v7/internal/api"
	"github.com/router-for-me/CLIProxyAPI/v7/internal/lain"
	"github.com/router-for-me/CLIProxyAPI/v7/sdk/cliproxy"
	auth "github.com/router-for-me/CLIProxyAPI/v7/sdk/cliproxy/auth"
	"github.com/router-for-me/CLIProxyAPI/v7/sdk/config"
	_ "github.com/router-for-me/CLIProxyAPI/v7/sdk/translator/builtin"
	"os"
	"os/signal"
	"path/filepath"
	"strings"
	"syscall"
)

func main() {
	if err := run(); err != nil {
		fmt.Fprintln(os.Stderr, "lain-proxy:", err)
		os.Exit(1)
	}
}
func run() error {
	configPath := flag.String("config", "", "isolated core YAML path")
	bindingsPath := flag.String("bindings", "", "account and model route bindings JSON")
	journalPath := flag.String("journal", "", "durable event journal path")
	tokenPath := flag.String("management-token-file", "", "private management bearer token file")
	ownedPath := flag.String("owned-auth-dir", "", "separate directory for credentials created by lain login")
	flag.Parse()
	for _, p := range []string{*configPath, *bindingsPath, *journalPath, *tokenPath} {
		if p == "" || !filepath.IsAbs(p) {
			return errors.New("all four path flags are required and must be absolute")
		}
	}
	token, err := os.ReadFile(*tokenPath)
	if err != nil {
		return err
	}
	managementToken := strings.TrimSpace(string(token))
	if len(managementToken) < 24 {
		return errors.New("management token must be at least 24 characters")
	}
	cfg, err := config.LoadConfig(*configPath)
	if err != nil {
		return err
	}
	if cfg.Host != "127.0.0.1" && cfg.Host != "::1" {
		return errors.New("core host must be explicit loopback")
	}
	if cfg.Port <= 0 || cfg.AuthDir == "" || !filepath.IsAbs(cfg.AuthDir) {
		return errors.New("isolated port and absolute auth-dir required")
	}
	if len(cfg.APIKeys) == 0 {
		return errors.New("inference API key required")
	}
	for _, key := range cfg.APIKeys {
		if key == managementToken {
			return errors.New("management and inference tokens must differ")
		}
	}
	cfg.RemoteManagement.SecretKey = ""
	cfg.RemoteManagement.DisableControlPanel = true
	cfg.RemoteManagement.DisableAutoUpdatePanel = true
	cfg.RequestLog = false
	cfg.LoggingToFile = false
	cfg.Plugins.Enabled = false
	cfg.Discovery.Enabled = false
	cfg.Pprof.Enable = false
	data, err := os.ReadFile(*bindingsPath)
	if err != nil {
		return err
	}
	var bindings lain.Bindings
	if err = json.Unmarshal(data, &bindings); err != nil {
		return err
	}
	journal, err := lain.OpenJournal(*journalPath)
	if err != nil {
		return err
	}
	defer func() { _ = journal.Close() }()
	integration, err := lain.New(journal, bindings)
	if err != nil {
		return err
	}
	integration.Install()
	snapshotStore, err := lain.NewSnapshotStore(cfg.AuthDir)
	if err != nil {
		return err
	}
	if *ownedPath == "" {
		*ownedPath = filepath.Join(filepath.Dir(cfg.AuthDir), "auth-owned")
	}
	if !filepath.IsAbs(*ownedPath) {
		return errors.New("owned-auth-dir must be absolute")
	}
	if err = os.MkdirAll(*ownedPath, 0700); err != nil {
		return err
	}
	snapshotReal, err := filepath.EvalSymlinks(cfg.AuthDir)
	if err != nil {
		return err
	}
	ownedReal, err := filepath.EvalSymlinks(*ownedPath)
	if err != nil {
		return err
	}
	if snapshotReal == ownedReal || strings.HasPrefix(ownedReal, snapshotReal+string(os.PathSeparator)) || strings.HasPrefix(snapshotReal, ownedReal+string(os.PathSeparator)) {
		return errors.New("owned and snapshot auth directories must be separate")
	}
	ownedStore, err := lain.NewOwnedStore(snapshotStore, *ownedPath, bindings)
	if err != nil {
		return err
	}
	auth.LainCredentialWritable = ownedStore.Allowed
	integration.Login = lain.NewLoginManager(ownedStore, cfg)
	defer integration.Login.Close()
	manager := auth.NewManager(ownedStore, nil, nil)
	service, err := cliproxy.NewBuilder().WithConfig(cfg).WithConfigPath(*configPath).WithCoreAuthManager(manager).WithWatcherFactory(cliproxy.LainSnapshotWatcher).WithServerOptions(api.WithMiddleware(integration.Middleware(managementToken, manager, cfg.APIKeys...))).Build()
	if err != nil {
		return err
	}
	ctx, cancel := signal.NotifyContext(context.Background(), os.Interrupt, syscall.SIGTERM)
	defer cancel()
	return service.Run(ctx)
}

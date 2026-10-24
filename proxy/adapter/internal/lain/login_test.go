package lain

import (
	"context"
	"encoding/json"
	auth "github.com/router-for-me/CLIProxyAPI/v8/sdk/cliproxy/auth"
	codex "github.com/router-for-me/CLIProxyAPI/v8/sdk/oauth/codex"
	"net"
	"net/http"
	"os"
	"path/filepath"
	"testing"
)

func loginFixture(t *testing.T) (*LoginManager, *OwnedStore, *string) {
	t.Helper()
	snapshot, err := NewSnapshotStore(filepath.Join(t.TempDir(), "snapshot"))
	if err != nil {
		t.Fatal(err)
	}
	store, err := NewOwnedStore(snapshot, filepath.Join(t.TempDir(), "owned"), Bindings{})
	if err != nil {
		t.Fatal(err)
	}
	manager := NewLoginManager(store, nil)
	address := ""
	manager.listen = func() (net.Listener, error) {
		listener, err := net.Listen("tcp4", "127.0.0.1:0")
		if err == nil {
			address = listener.Addr().String()
		}
		return listener, err
	}
	t.Cleanup(manager.Close)
	return manager, store, &address
}
func TestOwnedLoginDurableSeparateAndOnlySelectedCredentialWritable(t *testing.T) {
	manager, store, address := loginFixture(t)
	manager.exchange = func(context.Context, string, *codex.PKCECodes) (*codex.CodexAuthBundle, error) {
		return &codex.CodexAuthBundle{TokenData: codex.CodexTokenData{AccessToken: "own-access", RefreshToken: "own-refresh", AccountID: "provider-account", Expire: "2030-01-01T00:00:00Z"}}, nil
	}
	state, err := manager.Begin()
	if err != nil {
		t.Fatal(err)
	}
	response, err := http.Get("http://" + *address + "/auth/callback?state=" + state.State + "&code=synthetic-code")
	if err != nil {
		t.Fatal(err)
	}
	_ = response.Body.Close()
	done, _ := manager.Get(state.State)
	if done.Status != "completed" || done.OwnedAuthFile == "" || done.AccountID != "provider-account" {
		t.Fatal(done)
	}
	raw, err := os.ReadFile(filepath.Join(store.dir, done.OwnedAuthFile))
	if err != nil {
		t.Fatal(err)
	}
	var data map[string]any
	if err = json.Unmarshal(raw, &data); err != nil {
		t.Fatal(err)
	}
	if data["refresh_token"] != "own-refresh" || data["access_token"] != "own-access" {
		t.Fatal("owned token not persisted")
	}
	list, err := store.List(context.Background())
	if err != nil || len(list) != 0 {
		t.Fatal("unselected owned credential became active", err)
	}
	id := "owned/" + done.OwnedAuthFile
	store.active[id] = true
	list, err = store.List(context.Background())
	if err != nil || len(list) != 1 || list[0].ID != id || !store.Allowed(list[0]) {
		t.Fatal(list, err)
	}
	list[0].Metadata["access_token"] = "refreshed-own"
	if _, err = store.Save(context.Background(), list[0]); err != nil {
		t.Fatal(err)
	}
	external := &auth.Auth{ID: "external.json", Provider: "codex", Metadata: map[string]any{"refresh_token": "must-never-use"}}
	if store.Allowed(external) {
		t.Fatal("external snapshot writable")
	}
	if _, err = store.Save(context.Background(), external); err != nil {
		t.Fatal(err)
	}
	if _, err = os.Stat(filepath.Join(store.dir, "external.json")); !os.IsNotExist(err) {
		t.Fatal("external auth written")
	}
}
func TestCanceledOwnedLoginNeverPersistsLateExchange(t *testing.T) {
	manager, store, address := loginFixture(t)
	started := make(chan struct{})
	release := make(chan struct{})
	manager.exchange = func(context.Context, string, *codex.PKCECodes) (*codex.CodexAuthBundle, error) {
		close(started)
		<-release
		return &codex.CodexAuthBundle{TokenData: codex.CodexTokenData{AccessToken: "access", AccountID: "account"}}, nil
	}
	status, err := manager.Begin()
	if err != nil {
		t.Fatal(err)
	}
	done := make(chan struct{})
	go func() {
		defer close(done)
		response, _ := http.Get("http://" + *address + "/auth/callback?state=" + status.State + "&code=synthetic")
		if response != nil {
			_ = response.Body.Close()
		}
	}()
	<-started
	if _, err = manager.Cancel(status.State); err != nil {
		t.Fatal(err)
	}
	close(release)
	<-done
	state, _ := manager.Get(status.State)
	if state.Status != "canceled" {
		t.Fatal(state)
	}
	entries, err := os.ReadDir(store.dir)
	if err != nil || len(entries) != 0 {
		t.Fatal("canceled exchange persisted credentials", entries, err)
	}
}
func TestSnapshotRejectsRefreshAndOwnedRegistryCannotPromoteUnknownFile(t *testing.T) {
	dir := t.TempDir()
	if err := os.WriteFile(filepath.Join(dir, "external.json"), []byte(`{"type":"codex","refresh_token":"external"}`), 0600); err != nil {
		t.Fatal(err)
	}
	if _, err := NewSnapshotStore(dir); err == nil {
		t.Fatal("refresh snapshot accepted")
	}
	_, store, _ := loginFixture(t)
	name := "foreign.json"
	if err := os.WriteFile(filepath.Join(store.dir, name), []byte(`{"type":"codex","access_token":"x","refresh_token":"external"}`), 0600); err != nil {
		t.Fatal(err)
	}
	store.active["owned/"+name] = true
	list, err := store.List(context.Background())
	if err != nil || len(list) != 0 {
		t.Fatal("unregistered file became owned", err)
	}
}

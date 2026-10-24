package lain

import (
	"context"
	"encoding/json"
	"errors"
	"github.com/google/uuid"
	sdkAuth "github.com/router-for-me/CLIProxyAPI/v8/sdk/auth"
	auth "github.com/router-for-me/CLIProxyAPI/v8/sdk/cliproxy/auth"
	codex "github.com/router-for-me/CLIProxyAPI/v8/sdk/oauth/codex"
	"os"
	"path/filepath"
	"strings"
	"sync"
)

// OwnedStore keeps tokens created by this launcher separate from imported snapshots.
// The private ownership registry plus the applied bindings are required for refresh.
type OwnedStore struct {
	snapshot *SnapshotStore
	dir      string
	reader   *sdkAuth.FileTokenStore
	mu       sync.Mutex
	owned    map[string]bool
	active   map[string]bool
}

func NewOwnedStore(snapshot *SnapshotStore, dir string, bindings Bindings) (*OwnedStore, error) {
	if !filepath.IsAbs(dir) {
		return nil, errors.New("owned auth directory must be absolute")
	}
	if err := os.MkdirAll(dir, 0700); err != nil {
		return nil, err
	}
	if err := os.Chmod(dir, 0700); err != nil {
		return nil, err
	}
	s := &OwnedStore{snapshot: snapshot, dir: dir, reader: sdkAuth.NewFileTokenStore(), owned: map[string]bool{}, active: map[string]bool{}}
	s.reader.SetBaseDir(dir)
	data, err := os.ReadFile(filepath.Join(dir, "ownership"))
	if err == nil {
		if err = json.Unmarshal(data, &s.owned); err != nil {
			return nil, err
		}
	} else if !errors.Is(err, os.ErrNotExist) {
		return nil, err
	}
	for _, route := range bindings.Routes {
		for _, account := range route.Accounts {
			id := route.Credentials[account]
			if id == "" {
				id = bindings.Accounts[account]
			}
			s.active[id] = true
		}
	}
	return s, nil
}
func (s *OwnedStore) Allowed(a *auth.Auth) bool {
	if a == nil || a.Disabled || a.Provider != "codex" || !strings.HasPrefix(a.ID, "owned/") {
		return false
	}
	name := strings.TrimPrefix(a.ID, "owned/")
	s.mu.Lock()
	defer s.mu.Unlock()
	return filepath.Base(name) == name && s.owned[name] && s.active[a.ID]
}
func (s *OwnedStore) List(ctx context.Context) ([]*auth.Auth, error) {
	result, err := s.snapshot.List(ctx)
	if err != nil {
		return nil, err
	}
	owned, err := s.reader.List(ctx)
	if err != nil {
		return nil, err
	}
	for _, a := range owned {
		a.ID = "owned/" + a.ID
		if s.Allowed(a) {
			result = append(result, a)
		}
	}
	return result, nil
}
func (s *OwnedStore) Save(_ context.Context, a *auth.Auth) (string, error) {
	if !strings.HasPrefix(a.ID, "owned/") {
		return a.ID, nil
	}
	if !s.Allowed(a) {
		return "", errors.New("credential is not active and owned")
	}
	name := strings.TrimPrefix(a.ID, "owned/")
	s.mu.Lock()
	defer s.mu.Unlock()
	if err := writePrivateJSON(filepath.Join(s.dir, name), a.Metadata); err != nil {
		return "", err
	}
	return a.ID, nil
}
func (s *OwnedStore) Delete(context.Context, string) error {
	return errors.New("owned credential removal requires host lifecycle operation")
}
func (s *OwnedStore) PersistLogin(bundle *codex.CodexAuthBundle) (string, error) {
	if bundle == nil || bundle.TokenData.AccessToken == "" || bundle.TokenData.AccountID == "" {
		return "", errors.New("OAuth result missing account identity or access token")
	}
	name := "codex-owned-" + uuid.NewString() + ".json"
	data := map[string]any{"type": "codex", "access_token": bundle.TokenData.AccessToken, "refresh_token": bundle.TokenData.RefreshToken, "id_token": bundle.TokenData.IDToken, "account_id": bundle.TokenData.AccountID, "email": bundle.TokenData.Email, "expired": bundle.TokenData.Expire, "last_refresh": bundle.LastRefresh, "disabled": false}
	s.mu.Lock()
	defer s.mu.Unlock()
	if err := writePrivateJSON(filepath.Join(s.dir, name), data); err != nil {
		return "", err
	}
	next := make(map[string]bool, len(s.owned)+1)
	for k, v := range s.owned {
		next[k] = v
	}
	next[name] = true
	if err := writePrivateJSON(filepath.Join(s.dir, "ownership"), next); err != nil {
		return "", err
	}
	s.owned = next
	return name, nil
}
func writePrivateJSON(path string, v any) error {
	data, err := json.Marshal(v)
	if err != nil {
		return err
	}
	tmp, err := os.CreateTemp(filepath.Dir(path), ".lain-write-")
	if err != nil {
		return err
	}
	name := tmp.Name()
	defer func() { _ = os.Remove(name) }()
	if err = tmp.Chmod(0600); err == nil {
		_, err = tmp.Write(data)
	}
	if err == nil {
		err = tmp.Sync()
	}
	closeErr := tmp.Close()
	if err != nil {
		return err
	}
	if closeErr != nil {
		return closeErr
	}
	if err = os.Rename(name, path); err != nil {
		return err
	}
	dir, err := os.Open(filepath.Dir(path))
	if err != nil {
		return err
	}
	defer func() { _ = dir.Close() }()
	return dir.Sync()
}

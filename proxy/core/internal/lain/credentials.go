package lain

import (
	"context"
	"encoding/json"
	"errors"
	sdkAuth "github.com/router-for-me/CLIProxyAPI/v7/sdk/auth"
	auth "github.com/router-for-me/CLIProxyAPI/v7/sdk/cliproxy/auth"
	"os"
	"path/filepath"
	"strings"
)

// SnapshotStore loads only isolated Codex access-token snapshots and never writes them.
type SnapshotStore struct{ reader *sdkAuth.FileTokenStore }

func NewSnapshotStore(dir string) (*SnapshotStore, error) {
	if err := os.MkdirAll(dir, 0700); err != nil {
		return nil, err
	}
	err := filepath.WalkDir(dir, func(path string, d os.DirEntry, walkErr error) error {
		if walkErr != nil {
			return walkErr
		}
		if d.IsDir() {
			return nil
		}
		if d.Type()&os.ModeSymlink != 0 {
			return errors.New("auth snapshot symlinks are not supported")
		}
		if !strings.HasSuffix(path, ".json") {
			return nil
		}
		data, err := os.ReadFile(path)
		if err != nil {
			return err
		}
		var v map[string]any
		if err = json.Unmarshal(data, &v); err != nil {
			return err
		}
		if v["type"] != "codex" {
			return errors.New("only Codex OAuth snapshots are qualified for import")
		}
		if _, exists := v["refresh_token"]; exists {
			return errors.New("OAuth snapshot must not contain refresh_token")
		}
		return nil
	})
	if err != nil {
		return nil, err
	}
	reader := sdkAuth.NewFileTokenStore()
	reader.SetBaseDir(dir)
	return &SnapshotStore{reader: reader}, nil
}
func (s *SnapshotStore) List(ctx context.Context) ([]*auth.Auth, error)       { return s.reader.List(ctx) }
func (s *SnapshotStore) Save(_ context.Context, a *auth.Auth) (string, error) { return a.ID, nil }
func (s *SnapshotStore) Delete(context.Context, string) error {
	return errors.New("snapshot credentials are owned by the lain host")
}

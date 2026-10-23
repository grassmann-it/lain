package cliproxy

import "github.com/router-for-me/CLIProxyAPI/v7/sdk/config"

// LainSnapshotWatcher keeps a startup configuration immutable. The owning host
// applies a new configuration and credential/binding revision by restarting.
func LainSnapshotWatcher(string, string, func(*config.Config)) (*WatcherWrapper, error) {
	return &WatcherWrapper{}, nil
}

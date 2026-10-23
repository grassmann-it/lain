package lain

import (
	"context"
	"crypto/subtle"
	"errors"
	"github.com/gin-gonic/gin"
	"github.com/google/uuid"
	codex "github.com/router-for-me/CLIProxyAPI/v7/internal/auth/codex"
	"github.com/router-for-me/CLIProxyAPI/v7/sdk/config"
	"net"
	"net/http"
	"strings"
	"sync"
	"time"
)

type LoginStatus struct {
	State            string `json:"state"`
	Status           string `json:"status"`
	AuthorizationURL string `json:"authorizationUrl,omitempty"`
	OwnedAuthFile    string `json:"ownedAuthFile,omitempty"`
	AccountID        string `json:"accountId,omitempty"`
	ExpiresAt        string `json:"expiresAt,omitempty"`
	Error            string `json:"error,omitempty"`
}
type loginSession struct {
	LoginStatus
	cancel context.CancelFunc
	server *http.Server
}
type LoginManager struct {
	mu       sync.Mutex
	store    *OwnedStore
	client   *codex.CodexAuth
	sessions map[string]*loginSession
	listen   func() (net.Listener, error)
	exchange func(context.Context, string, *codex.PKCECodes) (*codex.CodexAuthBundle, error)
}

func NewLoginManager(store *OwnedStore, cfg *config.Config) *LoginManager {
	client := codex.NewCodexAuth(cfg)
	return &LoginManager{store: store, client: client, sessions: map[string]*loginSession{}, listen: func() (net.Listener, error) { return net.Listen("tcp4", "127.0.0.1:1455") }, exchange: client.ExchangeCodeForTokens}
}
func (l *LoginManager) Begin() (LoginStatus, error) {
	l.mu.Lock()
	defer l.mu.Unlock()
	for _, s := range l.sessions {
		if s.Status == "pending" || s.Status == "exchanging" {
			return LoginStatus{}, errors.New("a login is already active")
		}
	}
	pkce, err := codex.GeneratePKCECodes()
	if err != nil {
		return LoginStatus{}, err
	}
	listener, err := l.listen()
	if err != nil {
		return LoginStatus{}, errors.New("OAuth callback port 1455 is unavailable")
	}
	state := uuid.NewString()
	url, err := l.client.GenerateAuthURL(state, pkce)
	if err != nil {
		_ = listener.Close()
		return LoginStatus{}, err
	}
	ctx, cancel := context.WithTimeout(context.Background(), 10*time.Minute)
	session := &loginSession{LoginStatus: LoginStatus{State: state, Status: "pending", AuthorizationURL: url}, cancel: cancel}
	mux := http.NewServeMux()
	session.server = &http.Server{Handler: mux, ReadHeaderTimeout: 5 * time.Second}
	mux.HandleFunc("/auth/callback", func(w http.ResponseWriter, r *http.Request) {
		if r.Method != "GET" || subtle.ConstantTimeCompare([]byte(r.URL.Query().Get("state")), []byte(state)) != 1 {
			http.Error(w, "Invalid OAuth state", 400)
			return
		}
		l.mu.Lock()
		if session.Status != "pending" {
			l.mu.Unlock()
			http.Error(w, "Login is no longer pending", 409)
			return
		}
		session.Status = "exchanging"
		l.mu.Unlock()
		code := r.URL.Query().Get("code")
		if code == "" {
			l.finish(session, nil, errors.New("OAuth authorization was not granted"))
			http.Error(w, "Authorization was not granted", 400)
			return
		}
		bundle, exchangeErr := l.exchange(ctx, code, pkce)
		l.finish(session, bundle, exchangeErr)
		l.mu.Lock()
		status := session.Status
		l.mu.Unlock()
		w.Header().Set("Content-Type", "text/plain; charset=utf-8")
		if status != "completed" {
			w.WriteHeader(400)
			_, _ = w.Write([]byte("Login did not complete. Return to lain."))
			return
		}
		_, _ = w.Write([]byte("Login completed. Return to lain."))
	})
	// Bound retained status storage. Active login is always preserved.
	if len(l.sessions) >= 20 {
		for id, s := range l.sessions {
			if s.Status != "pending" && s.Status != "exchanging" {
				delete(l.sessions, id)
				break
			}
		}
	}
	l.sessions[state] = session
	go func() { _ = session.server.Serve(listener) }()
	go func() {
		<-ctx.Done()
		l.mu.Lock()
		if session.Status == "pending" || session.Status == "exchanging" {
			session.Status = "failed"
			session.Error = "Login expired"
		}
		l.mu.Unlock()
		_ = session.server.Close()
	}()
	return session.LoginStatus, nil
}
func (l *LoginManager) finish(s *loginSession, bundle *codex.CodexAuthBundle, err error) {
	l.mu.Lock()
	defer l.mu.Unlock()
	if s.Status != "exchanging" {
		return
	}
	if err != nil {
		s.Status = "failed"
		s.Error = "OAuth token exchange failed"
		s.cancel()
		return
	}
	name, err := l.store.PersistLogin(bundle)
	if err != nil {
		s.Status = "failed"
		s.Error = "Could not durably save owned credential"
		s.cancel()
		return
	}
	s.Status = "completed"
	s.AuthorizationURL = ""
	s.OwnedAuthFile = name
	s.AccountID = bundle.TokenData.AccountID
	s.ExpiresAt = bundle.TokenData.Expire
	// Allow the callback response to complete before closing its listener.
	go func() {
		ctx, cancel := context.WithTimeout(context.Background(), time.Second)
		defer cancel()
		_ = s.server.Shutdown(ctx)
		s.cancel()
	}()
}
func (l *LoginManager) Get(state string) (LoginStatus, bool) {
	l.mu.Lock()
	defer l.mu.Unlock()
	s, ok := l.sessions[state]
	if !ok {
		return LoginStatus{}, false
	}
	return s.LoginStatus, true
}
func (l *LoginManager) Cancel(state string) (LoginStatus, error) {
	l.mu.Lock()
	defer l.mu.Unlock()
	s, ok := l.sessions[state]
	if !ok {
		return LoginStatus{}, errors.New("login not found")
	}
	if s.Status == "completed" {
		return s.LoginStatus, errors.New("completed login cannot be canceled")
	}
	s.Status = "canceled"
	s.AuthorizationURL = ""
	s.cancel()
	_ = s.server.Close()
	return s.LoginStatus, nil
}
func (l *LoginManager) Close() {
	l.mu.Lock()
	defer l.mu.Unlock()
	for _, s := range l.sessions {
		if s.Status == "pending" || s.Status == "exchanging" {
			s.Status = "canceled"
		}
		s.cancel()
		_ = s.server.Close()
	}
}
func (l *LoginManager) Handle(c *gin.Context) bool {
	path := c.Request.URL.Path
	if path == "/lain/login/codex" && c.Request.Method == "POST" {
		status, err := l.Begin()
		if err != nil {
			c.AbortWithStatusJSON(409, gin.H{"error": err.Error()})
		} else {
			c.AbortWithStatusJSON(200, status)
		}
		return true
	}
	if !strings.HasPrefix(path, "/lain/login/") {
		return false
	}
	state := strings.TrimPrefix(path, "/lain/login/")
	if c.Request.Method == "DELETE" {
		s, err := l.Cancel(state)
		if err != nil {
			c.AbortWithStatusJSON(409, gin.H{"error": err.Error()})
		} else {
			c.AbortWithStatusJSON(200, s)
		}
		return true
	}
	if c.Request.Method == "GET" {
		if s, ok := l.Get(state); ok {
			c.AbortWithStatusJSON(200, s)
		} else {
			c.AbortWithStatus(404)
		}
		return true
	}
	c.AbortWithStatus(405)
	return true
}

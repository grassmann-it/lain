package lain

import (
	"crypto/subtle"
	"errors"
	"strconv"
	"strings"

	"github.com/gin-gonic/gin"
)

// HandleManagement must run inside the existing authenticated /lain boundary,
// after its management-token check. It is intentionally not its own middleware.
func (r *Recorder) HandleManagement(c *gin.Context) bool {
	switch c.Request.URL.Path {
	case "/lain/recording/events":
		if r == nil {
			c.AbortWithStatusJSON(503, gin.H{"error": "recording_unavailable"})
			return true
		}
		select {
		case r.httpGate <- struct{}{}:
			defer func() { <-r.httpGate }()
		default:
			c.AbortWithStatusJSON(429, gin.H{"error": "recording_read_busy"})
			return true
		}
		if c.Request.Method != "GET" {
			c.AbortWithStatus(405)
			return true
		}
		after, err := strconv.ParseInt(c.DefaultQuery("after", "0"), 10, 64)
		if err != nil {
			c.AbortWithStatus(400)
			return true
		}
		limit, err := strconv.Atoi(c.DefaultQuery("limit", "100"))
		if err != nil {
			c.AbortWithStatus(400)
			return true
		}
		batch, err := r.Read(c.Query("sourceEpoch"), after, limit)
		c.Header("Cache-Control", "no-store")
		if err != nil {
			status := 400
			if errors.Is(err, ErrRecordingReadBusy) {
				status = 429
			}
			if err.Error() == "recording_epoch_changed" {
				status = 409
			}
			c.AbortWithStatusJSON(status, gin.H{"error": err.Error()})
			return true
		}
		c.AbortWithStatusJSON(200, batch)
		return true
	case "/lain/recording/status":
		if c.Request.Method != "GET" {
			c.AbortWithStatus(405)
			return true
		}
		c.Header("Cache-Control", "no-store")
		c.AbortWithStatusJSON(200, r.Status())
		return true
	case "/lain/recording/disable":
		if c.Request.Method != "POST" {
			c.AbortWithStatus(405)
			return true
		}
		r.Disable()
		c.Header("Cache-Control", "no-store")
		c.AbortWithStatusJSON(200, r.Status())
		return true
	default:
		return false
	}
}

// RecordingInferenceAuthorized is a passive duplicate of the gateway's bearer
// check, needed because adapter admission runs before the SDK auth middleware.
// Its result affects recording only and must never reject or redirect inference.
func RecordingInferenceAuthorized(authorization string, tokens []string) bool {
	if !strings.HasPrefix(authorization, "Bearer ") {
		return false
	}
	received := strings.TrimPrefix(authorization, "Bearer ")
	authorized := false
	for _, token := range tokens {
		if token != "" && subtle.ConstantTimeCompare([]byte(received), []byte(token)) == 1 {
			authorized = true
		}
	}
	return authorized
}

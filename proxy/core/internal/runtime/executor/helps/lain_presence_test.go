package helps

import "testing"

func TestLainUsagePresenceDistinguishesAbsentAndZero(t *testing.T) {
	for _, test := range []struct {
		body                                string
		input, output, total, cache, reason bool
	}{
		{`{"usage":{"prompt_tokens":3,"completion_tokens":2,"total_tokens":5}}`, true, true, true, false, false},
		{`{"usage":{"prompt_tokens":0,"completion_tokens":0,"total_tokens":0,"prompt_tokens_details":{"cached_tokens":0},"completion_tokens_details":{"reasoning_tokens":0}}}`, true, true, true, true, true},
		{`{"usage":{"prompt_tokens":3,"completion_tokens":2}}`, true, true, false, false, false},
		{`{"usage":{"prompt_tokens":null,"completion_tokens":"0","total_tokens":-1}}`, false, false, false, false, false},
	} {
		detail := ParseOpenAIUsage([]byte(test.body))
		p := detail.Reported
		if p == nil {
			t.Fatal("presence missing")
		}
		if (p.InputTokens != nil) != test.input || (p.OutputTokens != nil) != test.output || (p.TotalTokens != nil) != test.total || (p.CacheReadTokens != nil) != test.cache || (p.ReasoningTokens != nil) != test.reason {
			t.Fatal(test.body, p)
		}
	}
	detail, ok := ParseCodexUsage([]byte(`{"response":{"usage":{"input_tokens":1,"output_tokens":0}}}`))
	if !ok || detail.Reported == nil || detail.Reported.OutputTokens == nil || *detail.Reported.OutputTokens != 0 || detail.Reported.TotalTokens != nil {
		t.Fatal(detail, ok)
	}
}

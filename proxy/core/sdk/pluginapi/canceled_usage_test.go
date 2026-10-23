package pluginapi

import (
	"encoding/json"
	"testing"
)

func TestCanceledUsageRPCExplicitUnknownOmitsCounters(t *testing.T) {
	reported := false
	r := UsageRecord{RequestID: "execution", ParentRequestID: "parent", Failed: true, Outcome: "canceled", UsageReported: &reported, Detail: UsageDetail{TotalTokens: 999}}
	b, err := json.Marshal(r)
	if err != nil {
		t.Fatal(err)
	}
	var fields map[string]json.RawMessage
	if err = json.Unmarshal(b, &fields); err != nil {
		t.Fatal(err)
	}
	if string(fields["outcome"]) != `"canceled"` || string(fields["usage_reported"]) != "false" || string(fields["Failed"]) != "true" || fields["Detail"] != nil {
		t.Fatalf("wrong cancellation contract: %s", b)
	}
	for _, v := range []*bool{nil, func() *bool { x := true; return &x }()} {
		r.UsageReported = v
		r.Detail = UsageDetail{} // reported zero still stays zero, absence is explicit
		b, err = json.Marshal(r)
		if err != nil {
			t.Fatal(err)
		}
		_ = json.Unmarshal(b, &fields)
		if fields["Detail"] == nil {
			t.Fatal("legacy/explicitly present detail was omitted")
		}
	}
}

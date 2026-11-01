using Avalonia.Controls;
using Lain.Core;
using Lain.Desktop.Presentation;

var assertions = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception(name); assertions++; }
AttemptDto Attempt(string account = "account-a", string? requested = "router-quality", string model = "provider-model", int? status = 200, string state = "success") =>
    new("attempt-a", "request-a", account, "route-observed", "provider-observed", model, DateTimeOffset.UtcNow,
        null, status, state, new(null, null, null, null, null), RequestedModel: requested);
var selected = RouteSelectionSummary.From(Attempt());
Check(selected.RequestedTarget == "router-quality" && selected.ActualModel == "provider-model", "requested router and executed model remain separate");
Check(selected.ActualProvider == "provider-observed" && selected.ActualAccount == "account-a" && selected.ActualRoute == "route-observed",
    "observed provider, account and route retained");
Check(selected.SelectionReason == RouteSelectionSummary.Unknown && selected.SkippedReason == RouteSelectionSummary.Unknown,
    "no selection or skipped reason fabricated from the requested alias");
var missing = RouteSelectionSummary.From(Attempt(requested: null));
Check(missing.RequestedTarget == RouteSelectionSummary.Unknown && missing.ActualModel == "provider-model", "missing requested target is never filled with executed model");
var sameModelOtherAccount = RouteSelectionSummary.From(Attempt(account: "account-b"));
Check(selected.ActualModel == sameModelOtherAccount.ActualModel && selected.ActualAccount != sameModelOtherAccount.ActualAccount,
    "same model does not merge or guess accounts");
var rateLimited = RouteSelectionSummary.From(Attempt(status: 429, state: "failed"));
var rateLines = string.Join('\n', rateLimited.Lines());
Check(rateLimited.Outcome.Contains("HTTP 429") && rateLimited.SelectionReason == RouteSelectionSummary.Unknown &&
    !rateLines.Contains("quota", StringComparison.OrdinalIgnoreCase) && !rateLines.Contains("fallback", StringComparison.OrdinalIgnoreCase),
    "HTTP 429 conveys observed outcome without quota exhaustion or fallback inference");
var record = new RecordingNormalizedRecord("record-a", "capture-a", "request-a", null, DateTimeOffset.UtcNow,
    "openai_chat", [], [], Model: "observed-model", RequestedModel: "requested-alias", Complete: false, GapReason: "source_gap");
var recording = RouteSelectionSummary.From(record);
Check(recording.ActualAccount == RouteSelectionSummary.Unknown && recording.ActualProvider == RouteSelectionSummary.Unknown &&
    recording.Outcome.Contains("capture partial") && !recording.Outcome.Contains("success", StringComparison.OrdinalIgnoreCase),
    "partial recording leaves missing execution identity and success unknown");
var reported = RouteSelectionSummary.From(record with { Complete = true, GapReason = null, FinishReason = "stop" });
Check(reported.Outcome == "Reported finish: stop", "reported finish remains reported evidence");
var hostile = RouteSelectionSummary.From(Attempt(account: "a\r\nb\u202ec", requested: new string('x', 100_000)));
Check(hostile.RequestedTarget.Length <= RouteSelectionSummary.MaximumFieldCharacters && hostile.ActualAccount == "a b c" &&
    hostile.Lines().All(line => !line.Any(char.IsControl)), "untrusted labels are bounded plain lines with bidi/control removal");
var control = (Border)hostile.CreateControl();
var children = ((StackPanel)control.Child!).Children;
Check(control.MaxHeight == RouteSelectionSummary.MaximumControlHeight && control.ClipToBounds &&
    children.Count == RouteSelectionSummary.MaximumLines + 1 && children.All(child => child is TextBlock),
    "compact control contains bounded read-only text only");
Check(children.OfType<TextBlock>().All(text => text.MaxLines is 1 or 2), "every displayed field has bounded wrapping");
Console.WriteLine($"PASS {assertions} observed route presentation assertions");

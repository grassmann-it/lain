using Lain.Client;
using Lain.Core;

var checks = 0;
var now = DateTimeOffset.Parse("2026-10-25T12:00:00Z");
QuotaDto Quota(string name, double? remaining = 20, string? kind = null) => new(
    "account", null, "https://provider.invalid/api#" + name, "ok", now, now, null,
    "credits", null, null, remaining, false, WindowLabel: name, MeasurementKind: kind);
void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
    checks++;
}
var five = Quota("5 hours");
var sevenHour = Quota("7h");
var weekly = Quota("7 days");
var monthly = Quota("Monthly credits");
var purchased = Quota("Purchased credits", 4);
var freeZero = Quota("Free credits", 0);
var unknown = Quota("Unknown", null);
var ordered = QuotaPresentation.Order([unknown, monthly, purchased, weekly, sevenHour, five]);
Check(ordered.SequenceEqual(new[] { five, sevenHour, weekly, monthly, purchased, unknown }), "Short windows precede month, wallet and missing, ordered by semantic duration");
var summary = QuotaPresentation.ForSummary([unknown, monthly, purchased, weekly, sevenHour, five, freeZero]);
Check(summary.Primary.SequenceEqual(new[] { five, sevenHour }), "At most two relevant windows are primary");
Check(summary.Additional.SequenceEqual(new[] { weekly, monthly, purchased }), "Further real windows and positive credits remain available as additional");
Check(summary.Missing.SequenceEqual(new[] { unknown }) && summary.HiddenCount == 1 && summary.HasMore, "Missing values remain separate and only zero additional credit is hidden");
Check(QuotaPresentation.Order([weekly, weekly]).Count == 2, "Identical input rows are not combined");
Check(QuotaPresentation.ForSummary([five, five, five]).Additional.Count == 1, "Summary preserves duplicate rows beyond primary positions");

var emptyFive = five with { Remaining = 0 };
var emptyWallet = Quota("Wallet balance", 0, "balance");
var emptyMonthly = monthly with { Remaining = 0 };
Check(QuotaPresentation.HasReading(emptyFive) && QuotaPresentation.ForSummary([emptyFive]).Primary.Single() == emptyFive, "An exhausted short main window remains primary");
Check(QuotaPresentation.ForSummary([emptyWallet]).Primary.Single() == emptyWallet, "An actual zero wallet remains the main balance");
Check(QuotaPresentation.ForSummary([emptyMonthly]).Primary.Single() == emptyMonthly, "An actual zero monthly balance remains visible when it is the only main reading");
Check(QuotaPresentation.ForSummary([monthly, five]).Primary.Single() == five, "A monthly allowance is secondary to a short window");
Check(QuotaPresentation.ForSummary([freeZero]).Primary.Count == 0 && !QuotaPresentation.HasQuotaReading([freeZero]), "Only zero extra credit does not become a main account quota");
Check(QuotaPresentation.HasQuotaReading([emptyWallet]) && QuotaPresentation.HasQuotaReading([purchased]), "Main zero wallet and positive extra credit are genuine account readings");
Check(!QuotaPresentation.IsZeroOnlyAdditionalCredit(emptyMonthly) && !QuotaPresentation.IsZeroOnlyAdditionalCredit(emptyWallet), "Main zero balances cannot be hidden by the extra-credit rule");
Check(!QuotaPresentation.IsZeroOnlyAdditionalCredit(freeZero with { Remaining = null }), "Missing extra-credit value is never treated as zero");
Check(!QuotaPresentation.IsZeroOnlyAdditionalCredit(freeZero with { Used = 3 }), "Exhausted credit with nonzero reported usage remains additional");

Check(!QuotaPresentation.HasReading(unknown), "No supplied measurement means no reading");
Check(!QuotaPresentation.HasReading(unknown with { Remaining = double.NaN, Used = double.PositiveInfinity, Limit = -1, UsedPercent = 101 }), "Nonfinite and invalid amounts do not rank as readings");
Check(!QuotaPresentation.HasReading(five with { State = "unsupported" }), "Unsupported placeholders never rank as actual readings");
Check(QuotaPresentation.HasReading(five with { State = "stale" }), "Last-known actual measurements remain visible");
Check(QuotaPresentation.HasReading(emptyWallet with { Remaining = -2 }), "A reported negative balance remains a balance, not a fabricated zero");
Check(!QuotaPresentation.HasReading(unknown with { MeasurementKind = "limit", Remaining = 10 }), "A limit-only measurement does not acquire a synthetic remaining balance");
Check(QuotaPresentation.HasReading(unknown with { MeasurementKind = "limit", Limit = 0 }), "An explicit zero limit remains an actual measurement");
Check(!QuotaPresentation.HasReading(unknown with { MeasurementKind = "rate", Limit = 10 }), "A rate sample needs its actual rate value");

var ambiguous = Quota("Primary window") with { Source = "https://provider.invalid/api#primary_window", ResetsAt = now.AddHours(5) };
Check(QuotaPresentation.Order([ambiguous, sevenHour]).First() == sevenHour, "An upcoming reset cannot imply a five-hour duration");
Check(!QuotaPresentation.IsLongWindow(ambiguous), "A reset instant also cannot imply a long window");
var bounded = Quota("Provider period") with { WindowStart = now, WindowEnd = now.AddHours(7) };
Check(QuotaPresentation.Order([weekly, bounded]).First() == bounded, "An explicit complete window period supplies duration");
Check(QuotaPresentation.IsLongWindow(bounded with { WindowEnd = now.AddDays(8) }), "Explicit windows longer than seven days are secondary");
Check(QuotaPresentation.IsLongWindow(monthly with { WindowStart = now.AddDays(-2), WindowEnd = now }), "A monthly semantic label is not shortened by its partial sample period");
var fragmentFive = five with { WindowLabel = null, Source = "https://provider.invalid/api#fiveHour" };
Check(QuotaPresentation.Order([sevenHour, fragmentFive]).First() == fragmentFive, "Known semantic source fragment identifies a short window");
var creditWindow = Quota("Credits · 5 hours · 2");
Check(!QuotaPresentation.IsAdditionalCredit(creditWindow) && !QuotaPresentation.IsSecondary(creditWindow), "Rolling credit allowances stay primary rather than being mistaken for wallets");
var minute = Quota("model · Tokens per minute");
var day = Quota("model · Requests per day");
Check(QuotaPresentation.Order([day, five, minute]).SequenceEqual(new[] { minute, five, day }), "Provider minute and day labels have semantic durations");
Check(!QuotaPresentation.IsSecondary(Quota("Weekly plan credits")), "Weekly subscription credit windows remain short limits");
var otherA = Quota("Unspecified A");
var otherB = Quota("Unspecified B");
Check(QuotaPresentation.Order([otherB, otherA]).SequenceEqual(new[] { otherB, otherA }), "Equivalent relevance retains original order");
Check(QuotaPresentation.ForSummary([]) is { Primary.Count: 0, Additional.Count: 0, Missing.Count: 0, HiddenCount: 0, HasMore: false }, "An empty account has no invented values");
Console.WriteLine($"PASS quota presentation: {checks} checks");

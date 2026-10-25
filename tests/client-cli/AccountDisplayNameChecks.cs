using Lain.Client;
using Lain.Core;

internal static class AccountDisplayNameChecks
{
    internal static int Run()
    {
        var checks = 0;
        void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
        AccountDto Account(string id, string name, string provider = "gemini-api") => new(id, name, provider, true, "api-key", "ready");
        var imported = Account("one", "My Gemini API account");
        Check(AccountDisplayNames.Get(imported, [imported]) == "Gemini" && imported.Name == "My Gemini API account",
            "compact labels do not rewrite saved import names");
        var custom = Account("custom", "Science account");
        Check(AccountDisplayNames.Get(custom, [custom]) == custom.Name, "custom labels are preserved");
        var second = imported with { Id = "two" };
        AccountDto[] inventory = [imported, second, custom];
        Check(AccountDisplayNames.Get(imported, inventory) == "Gemini 1" && AccountDisplayNames.Get(second, inventory) == "Gemini 2",
            "all siblings contribute to unique labels before filtering or sorting");
        var collision = Account("collision", "Gemini 1");
        AccountDto[] colliding = [imported, second, collision, Account("another", "Gemini 1 [1]")];
        Check(colliding.Select(item => AccountDisplayNames.Get(item, colliding)).Distinct(StringComparer.OrdinalIgnoreCase).Count() == colliding.Length,
            "custom names cannot collide with generated labels or their disambiguators");
        var sameName = custom with { Id = "duplicate" };
        AccountDto[] duplicate = [custom, sameName];
        Check(AccountDisplayNames.Get(custom, duplicate) == "Science account (Gemini 1)"
            && AccountDisplayNames.Get(sameName, duplicate) == "Science account (Gemini 2)", "duplicate own names remain distinct");
        var email = Account("email", "person@example.invalid", "codex");
        Check(AccountDisplayNames.Get(email, [email]) == "Codex", "single imported email uses provider label");
        var email2 = email with { Id = "email2", Name = "work@example.invalid" };
        Check(AccountDisplayNames.Get(email, [email, email2]) == email.Name, "multiple imported emails retain distinct names");
        var reimported = imported with { State = "unverified", Generation = imported.Generation + 1 };
        Check(AccountDisplayNames.Get(reimported, [reimported, second, custom]) == "Gemini 1" && reimported.Id == imported.Id,
            "reimport state and credential generation do not change display identity");
        var now = DateTimeOffset.Parse("2026-10-25T00:00:00Z");
        var free = new QuotaDto("free", null, "fixture", "stale", now.AddHours(-1), now.AddHours(-1), now,
            "requests", 10, 50, null, false, WindowId: "free-model-daily-requests", Scope: "account", WindowEnd: now);
        Check(QuotaDisplayFacts.IsElapsedFreeDay(free, now) && !QuotaDisplayFacts.IsElapsedFreeDay(free, now.AddTicks(-1)),
            "free day expires at the exact UTC boundary");
        Check(!QuotaDisplayFacts.IsElapsedFreeDay(free with { WindowId = "key-spending-limit" }, now)
            && !QuotaDisplayFacts.IsElapsedFreeDay(free with { Scope = "key" }, now)
            && !QuotaDisplayFacts.IsElapsedFreeDay(free with { WindowEnd = null }, now),
            "elapsed-free-day rule never affects unrelated, key-scoped or unknown windows");
        return checks;
    }
}

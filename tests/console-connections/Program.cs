using System.Text;
using System.Text.Json;
using Lain.Host.Accounts.ConsoleConnections;
using Lain.Host.Accounts.ConsoleConnections.MiMo;
using Lain.Host.Quota;

static void Check(bool good, string name) { if (!good) throw new Exception(name); }
static void Reject(Action action, string name)
{
    try { action(); } catch (InvalidOperationException) { return; }
    throw new Exception(name);
}
var fixture = Path.Combine(Directory.GetCurrentDirectory(), "tests/console-connections/fixtures/mimo");
using var identity = JsonDocument.Parse(File.ReadAllText(Path.Combine(fixture, "identity.json")));
using var individual = JsonDocument.Parse(File.ReadAllText(Path.Combine(fixture, "individual.json")));
using var team = JsonDocument.Parse(File.ReadAllText(Path.Combine(fixture, "team.json")));
var binding = new ConsoleResourceBinding("synthetic-uid", "CN", "individual-token-plan");
var now = DateTimeOffset.Parse("2026-10-24T12:00:00Z");
var rows = MiMoConsoleQuotaReader.Individual(identity.RootElement, individual.RootElement, binding, now);
Check(rows.Error is null && rows.Readings.Count == 2, "all individual pools retained");
Check(rows.Readings[0].UsedPercent == 12 && rows.Readings[1].Remaining == 8, "fractions normalized independently");
Check(rows.Readings.All(r => r.ResetsAt is null), "plan validity never invented as reset");
Check(MiMoConsoleQuotaReader.Individual(identity.RootElement, individual.RootElement, binding with { Subject = "wrong" }, now).Readings.Count == 0, "wrong UID");
Check(MiMoConsoleQuotaReader.Individual(identity.RootElement, individual.RootElement, binding with { Region = "EU" }, now).Readings.Count == 0, "wrong region");
foreach (var json in new[] {"{\"code\":0,\"data\":{\"userId\":\"synthetic-uid\",\"idc\":999}}", "{\"code\":0,\"data\":{\"userId\":\"synthetic-uid\",\"id\":\"wrong\",\"idc\":310}}", "{\"code\":401,\"data\":{\"userId\":\"synthetic-uid\",\"idc\":310}}", "{\"code\":0,\"data\":{\"userId\":\"synthetic-uid\",\"idc\":\"310\"}}"})
{
    using var bad = JsonDocument.Parse(json);
    Check(!MiMoConsoleIdentity.Matches(bad.RootElement, binding), "identity schema fails closed");
}
using (var partial = JsonDocument.Parse("{\"code\":0,\"data\":{\"usage\":{\"items\":[{\"name\":\"period\",\"used\":5,\"percent\":25}]}}}"))
{
    var reading = MiMoConsoleQuotaReader.Individual(identity.RootElement, partial.RootElement, binding, now);
    Check(reading.Error is not null && reading.Readings.Single().Used == 5 && reading.Readings.Single().Remaining is null
        && reading.Readings.Single().UsedPercent is null, "partial is not zero and invalid fraction not clamped");
}
using (var failed = JsonDocument.Parse("{\"code\":429,\"data\":{\"usage\":{\"items\":[{\"name\":\"period\",\"used\":0,\"limit\":100,\"percent\":0}]}}}"))
    Check(MiMoConsoleQuotaReader.Individual(identity.RootElement, failed.RootElement, binding, now).Readings.Count == 0, "application error not zero usage");
var teamRows = MiMoConsoleQuotaReader.TeamPercent(team.RootElement, "project-1", now);
Check(teamRows.Readings.Single().UsedPercent == 17.5 && teamRows.Readings.Single().Used is null, "team percentage not multiplied or treated as credits");
ConsoleReadLease Lease() => new("synthetic-operation", "account-1", 7, binding, ConsoleReadOperation.MiMoIndividualUsage, now);
ConsoleReadReply Reply(ConsoleReadLease lease, string? nonce = null, string? origin = null, int status = 200, string? body = null) =>
    new(lease.OperationId, lease.AccountId, lease.Revision, nonce ?? lease.Nonce, lease.Binding, lease.Operation,
        origin ?? lease.Route.Origin, status, Encoding.UTF8.GetBytes(body ?? "{\"code\":0,\"data\":{}}"));
var lease = Lease();
Reject(() => lease.Accept(Reply(lease, nonce: "wrong"), 7, true, now), "wrong nonce");
Reject(() => lease.Accept(Reply(lease, origin: "https://evil.invalid"), 7, true, now), "wrong origin");
Reject(() => lease.Accept(Reply(lease), 8, true, now), "revision changed");
Reject(() => lease.Accept(Reply(lease), 7, false, now), "cancelled operation");
Reject(() => lease.Accept(Reply(lease), 7, true, now.AddMinutes(2)), "expiry");
using var accepted = lease.Accept(Reply(lease), 7, true, now);
Reject(() => lease.Accept(Reply(lease), 7, true, now), "replay");
var unauthorized = Lease();
Reject(() => unauthorized.Accept(Reply(unauthorized, status: 401), 7, true, now), "unauthorized has no key retry");
var large = Lease();
Reject(() => large.Accept(Reply(large, body: new string('x', 256 * 1024 + 1)), 7, true, now), "response size cap");
Reject(() => ConsoleReadRoutes.Resolve((ConsoleReadOperation)999), "unlisted operation forbidden");
Check(!lease.ToString().Contains(lease.Nonce) && !Reply(lease).ToString().Contains(lease.Nonce), "private envelopes redacted");
Console.WriteLine("PASS console boundary and MiMo synthetic identity/normalization fixtures; no browser or provider qualification implied");

using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Lain.Cli.Diagnostics;

/// <summary>Static configuration observations never establish a running thread's binding or authentication.</summary>
public sealed class DoctorEngine(IDoctorEnvironment environment)
{
    public async Task<DoctorReport> DiagnoseAsync(DoctorRequest request, CancellationToken ct = default)
    {
        var findings = new List<DoctorFinding>();
        void Add(string code, string state, string explanation, string source, string next) => findings.Add(new(code, state, explanation, source, next));
        DoctorHost host;
        try { host = await environment.ReadHostAsync(ct); }
        catch (Exception) when (!ct.IsCancellationRequested) { host = new(null, null); }
        ct.ThrowIfCancellationRequested();
        Add("host.reachable", host.Status is null ? "unknown" : "ok", host.Status is null ? "Host status could not be read." : "Host status was read.", "public status API", "Check lain service status if needed.");
        if (host.Status is { } status)
        {
            Add("host.gateway", status.RuntimeMode == "gateway" ? (status.ProxyReady ? "ok" : "blocked") : "blocked",
                status.RuntimeMode == "gateway" ? (status.ProxyReady ? "Gateway reports ready." : "Gateway is not ready.") : "Monitor mode does not provide a client gateway.", "public status API", "Review gateway mode and readiness.");
            Add("profile.revision", status.Revision == status.AppliedRevision ? "ok" : "warning", status.Revision == status.AppliedRevision ? "Desired and applied revisions agree." : "Desired and applied revisions differ.", "public status API", "Review the current profile preview before applying changes.");
        }
        Add("accounts.setup", host.Configuration is null ? "unknown" : host.Configuration.Accounts.Any(a => a.Enabled) ? "ok" : "blocked",
            host.Configuration is null ? "Account setup was not observed." : host.Configuration.Accounts.Any(a => a.Enabled) ? "An enabled account is configured; authentication is unverified." : "No enabled account is configured.", "public configuration API", "Review accounts through the public account commands.");
        var routeAccounts = host.Configuration?.Accounts.Where(a => a.Enabled &&
            (a.Capabilities is null || a.Capabilities.Any(c => c.Purpose == "inference"))).ToArray();
        var missingRoutes = routeAccounts?.Any(a => !host.Configuration!.Routes.Any(r =>
            r.Enabled && r.Visible && r.AccountIds.Contains(a.Id, StringComparer.Ordinal))) == true;
        Add("accounts.routes", host.Configuration is null ? "unknown" : missingRoutes ? "warning" : "ok",
            host.Configuration is null ? "Configured model routes were not observed." : missingRoutes ?
            "At least one enabled inference account has no enabled visible model route; an account alone adds no client models." :
            "No enabled inference account without a visible configured route was observed; profile inclusion is separate.",
            "public configuration API", "Configure the intended subscription model routes and profile inclusion through supported management operations; then review the export.");
        Add("provider.acceptance", "unknown", "No authentication, inference or response-quality probe was performed.", "read-only scope", "Use a separately authorized provider probe when required.");
        var versions = new Dictionary<string, string>();
        // Never echo arbitrary server version strings (including malicious fixture payloads).
        if (host.Status?.Version is { } version && Regex.IsMatch(version, @"\A[0-9]+\.[0-9]+\.[0-9]+(?:[-+][a-zA-Z0-9.-]{1,64})?\z")) versions["host"] = version;
        if (request.Target is not null)
        {
            try { DiagnoseLocal(request, host, Add); }
            catch (Exception) when (!ct.IsCancellationRequested)
            { Add("client.local_read", "unknown", "Local metadata could not be safely interpreted within the read budget.", "local metadata", "Review supported configuration syntax and file access."); }
            try { DoctorMaintenance.Diagnose(environment, request.Target, versions, Add, ct); }
            catch (Exception) when (!ct.IsCancellationRequested)
            { Add("client.maintenance", "unknown", "Maintenance metadata could not be safely verified.", "local maintenance metadata", "Review the qualified maintenance status; no client was started."); }
            Add("client.activation", "unknown", request.Target == "codex-vscode" ? "VSIX selection and running extension activation are not established by configuration files." : "Running thread overrides and activation are not established by configuration files.", "read-only scope", "Verify the selected client and a new thread in the intended workspace.");
            Add("payload.limits", "unknown", "No request payload was inspected; tool count and token context are distinct unmeasured limits.", "read-only scope", "Correlate the concrete failing attempt before diagnosing a limit.");
        }
        return new(1, environment.UtcNow, request.Target, request.Project is null ? "service-or-root" : "project", versions, findings);
    }

    private void DiagnoseLocal(DoctorRequest request, DoctorHost host, Action<string, string, string, string, string> add)
    {
        var home = environment.CodexHome;
        var profile = host.Status?.ActiveProfileId;
        // --profile selects a native Codex profile; export staleness uses the active Host identity.
        var revision = profile is not null && profile == host.Status?.ActiveProfileId ? host.Status.ActiveProfileRevision : null;
        var installed = environment.Inspect(home, profile, revision);
        add("export.installed", installed.State == "installed" ? (installed.Stale == true ? "warning" : "ok") : installed.State == "recovery-required" ? "blocked" : "unknown",
            installed.State == "installed" ? (installed.Stale == true ? "Installed export differs from the active profile revision." : "Installed export integrity was observed; effective selection is checked separately.") : "A consistent installed export could not be established.", "CodexArtifactInstaller.Inspect", "Prepare and install the selected current export when needed.");
        var rootText = environment.ReadText(Path.Combine(home, "config.toml"));
        var root = Parse(rootText);
        var settings = new Dictionary<string, string>(root.GetValueOrDefault("") ?? new(), StringComparer.Ordinal);
        var selectedProfile = request.Profile;
        if (selectedProfile is null && settings.TryGetValue("profile", out var defaultProfile)) selectedProfile = defaultProfile;
        if (selectedProfile is not null)
        {
            if (!root.TryGetValue("profiles." + selectedProfile, out var overlay))
            { add("client.profile", "unknown", "Requested native profile section was not observed.", "root configuration", "Check the launcher and native Lain profile binding."); return; }
            foreach (var pair in overlay) settings[pair.Key] = pair.Value;
        }
        if (request.Project is not null)
        {
            var project = Path.GetFullPath(request.Project);
            // Ancestor layers and trust determine native Codex loading. Observe all known layers,
            // but do not pretend their presence establishes trust or complete precedence.
            var layer = false;
            var depth = 0;
            for (var dir = new DirectoryInfo(project); dir is not null; dir = dir.Parent)
            {
                if (++depth > 64) throw new InvalidDataException();
                var content = environment.ReadText(Path.Combine(dir.FullName, ".codex", "config.toml"));
                if (content is not null)
                {
                    var parsed = Parse(content);
                    layer = true;
                    if (parsed.GetValueOrDefault("") is { } overrides && overrides.Any(pair =>
                        pair.Key is "model_provider" or "model_catalog_json" or "model" or "model_reasoning_effort" or
                            "model_context_window" or "model_max_context_window" or "model_auto_compact_token_limit" &&
                        settings.GetValueOrDefault(pair.Key) != pair.Value))
                        add("workspace.override", "warning", "An observed workspace layer conflicts with the selected root/profile model binding; effective loading depends on trust.", "project ancestor configuration", "Review workspace overrides and confirm native effective configuration.");
                }
            }
            add("workspace.binding", "unknown", layer ? "Workspace configuration exists; trust and native precedence require client confirmation." : "No workspace layer was found; workspace trust and launch overrides remain unobserved.", "project ancestor configuration", "Confirm the client's effective configuration in this project.");

        }
        var providerOk = settings.GetValueOrDefault("model_provider") == "lain";
        add("client.provider", rootText is null ? "unknown" : providerOk ? "ok" : "blocked", rootText is null ? "Root configuration was not found; static provider selection is unobserved." : providerOk ? "Selected static configuration names the Lain provider." : "Selected static configuration does not name the Lain provider.", "root/profile configuration", "Select the intended Lain root or profile binding.");
        var catalogPath = settings.GetValueOrDefault("model_catalog_json");
        var catalogOk = false;
        if (catalogPath is not null && Path.IsPathFullyQualified(catalogPath) && installed.GenerationId is not null)
            catalogOk = Path.GetFullPath(catalogPath) == Path.GetFullPath(Path.Combine(home, ".lain-artifacts", installed.GenerationId, "lain.models.json"));
        add("client.catalog", catalogOk ? "ok" : "warning", catalogOk ? "Selected static catalog matches the installed export generation." : "Selected static catalog does not establish the installed export binding.", "root/profile configuration and install identity", "Review catalog selection against the current installed export.");
        var provider = root.GetValueOrDefault("model_providers.lain.auth");
        var helper = root.ContainsKey("!auth-unsupported") ? null : provider?.GetValueOrDefault("command");
        var helperExists = helper is not null && Path.IsPathFullyQualified(helper) && environment.IsFile(helper);
        var argsOk = provider?.GetValueOrDefault("args") == "[\"auth\",\"token\",\"--audience\",\"codex\"]";
        add("client.helper", helper is null ? "unknown" : !helperExists || !argsOk ? "blocked" : "unknown",
            helper is null ? "A supported helper declaration was not observed." : !helperExists ? "Declared helper file is missing or is not an absolute path." : !argsOk ? "Declared helper arguments differ from the qualified auth token contract." : "Helper file and auth arguments were observed; executable identity is unverified.", "root provider auth declaration", "Compare the helper identity with the installed Lain executable.");
        // Export identity and selected catalog validity are independent observations.
        if (catalogPath is not null && Path.IsPathFullyQualified(catalogPath))
            DiagnoseCatalog(environment.ReadText(catalogPath), settings, host, add);
    }

    private static void DiagnoseCatalog(string? text, Dictionary<string, string> settings, DoctorHost host,
        Action<string, string, string, string, string> add)
    {
        if (text is null) throw new InvalidDataException();
        using var document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 32 });
        RejectDuplicates(document.RootElement);
        var models = document.RootElement.GetProperty("models");
        var selected = settings.GetValueOrDefault("model");
        var matches = models.EnumerateArray().Where(m => m.GetProperty("slug").GetString() == selected).ToArray();
        if (matches.Length != 1) { add("client.model", "blocked", "Selected model is absent or ambiguous in the catalog.", "selected catalog", "Export the selected current model catalog."); return; }
        var model = matches[0];
        if ((model.TryGetProperty("visibility", out var visibility) && visibility.GetString() == "hide") ||
            (model.TryGetProperty("supported_in_api", out var available) && available.ValueKind == JsonValueKind.False))
        { add("client.model", "blocked", "Selected model is hidden or unavailable in the catalog.", "selected catalog", "Select an available exported model."); return; }
        var levels = model.TryGetProperty("supported_reasoning_levels", out var list) ? list.EnumerateArray().Select(l => l.GetProperty("effort").GetString()).ToArray() : [];
        var effort = settings.GetValueOrDefault("model_reasoning_effort");
        if (levels.Distinct().Count() != levels.Length) throw new InvalidDataException();
        var valid = effort is not null && levels.Contains(effort);
        add("client.effort", levels.Length == 0 ? "unknown" : valid ? "ok" : "blocked",
            levels.Length == 0 ? "Selected model has no qualified effort list; Auto effort compatibility is unverified." : valid ? "Selected static effort is advertised by the catalog." : "Selected static effort is missing or is not advertised by the catalog.", "selected catalog and root/profile configuration", "Choose an explicitly supported effort or prepare a qualified catalog.");
        var configuredNativeCodex = IsSelectedConfiguredCodexRoute(host, selected);
        DiagnoseInputModalities(model, configuredNativeCodex, add);
        DiagnoseContextMetadata(model, settings, configuredNativeCodex, add);
    }

    private static bool IsSelectedConfiguredCodexRoute(DoctorHost host, string? selected)
    {
        if (host.Configuration is not { } configuration || string.IsNullOrEmpty(selected)) return false;
        var routes = configuration.Routes.Where(route => route.Enabled && route.Visible && route.Alias == selected).ToArray();
        if (host.Status?.ActiveProfileId is { } profileId)
        {
            var profile = configuration.Profiles.SingleOrDefault(candidate => candidate.Id == profileId);
            if (profile is null) return false;
            routes = routes.Where(route => profile.RouteIds.Contains(route.Id, StringComparer.Ordinal)).ToArray();
        }
        return routes.Length == 1 && routes[0].Provider == "codex";
    }

    private static void DiagnoseInputModalities(JsonElement model, bool configuredNativeCodex,
        Action<string, string, string, string, string> add)
    {
        if (!model.TryGetProperty("input_modalities", out var metadata))
        {
            add("client.input_modalities", configuredNativeCodex ? "warning" : "unknown",
                configuredNativeCodex ? "Configured native Codex route metadata omits image input; omission is not proof that images are unsupported." :
                "Selected generic model has no input-modality metadata; image capability remains unknown.",
                "selected catalog and enabled route scope", configuredNativeCodex ?
                "Refresh or export the current qualified catalog; native client qualification is separately verified." :
                "Use separately qualified route evidence before sending images.");
            return;
        }
        if (metadata.ValueKind != JsonValueKind.Array || metadata.GetArrayLength() == 0)
        { add("client.input_modalities", "blocked", "Selected model input-modality metadata is not a non-empty array.", "selected catalog", "Repair the selected catalog entry."); return; }
        var values = new List<string>();
        foreach (var value in metadata.EnumerateArray())
        {
            if (value.ValueKind != JsonValueKind.String || value.GetString() is not { } modality)
            { add("client.input_modalities", "blocked", "Selected model input-modality metadata contains a non-string value.", "selected catalog", "Repair the selected catalog entry."); return; }
            values.Add(modality);
        }
        if (values.Distinct(StringComparer.Ordinal).Count() != values.Count ||
            values.Any(value => value is not ("text" or "image")) || !values.Contains("text", StringComparer.Ordinal))
        { add("client.input_modalities", "blocked", "Selected model input-modality metadata has duplicate, unknown, or text-missing values.", "selected catalog", "Repair the selected catalog entry."); return; }
        if (configuredNativeCodex)
        {
            var image = values.Contains("image", StringComparer.Ordinal);
            add("client.input_modalities", image ? "ok" : "warning",
                image ? "Configured native Codex route metadata advertises text and image input; this is catalog structure, not provider acceptance." :
                "Configured native Codex route advertises text only; the known metadata regression remains a warning rather than an unsupported claim.",
                "selected catalog and enabled route scope", "Refresh or export the current qualified catalog; qualify image behavior separately.");
            return;
        }
        if (values.Count == 1 && values[0] == "text")
        { add("client.input_modalities", "unknown", "Generic model text-only metadata is intentional and does not establish or deny image support.", "selected catalog", "Use separately qualified route evidence before sending images."); return; }
        add("client.input_modalities", "ok", "Selected generic model has structurally valid text-and-image metadata; provider image acceptance remains unverified.", "selected catalog", "Qualify the concrete route before sending images.");
    }

    private enum PositiveNumberState { Missing, Positive, Invalid }
    private readonly record struct OptionalPositiveNumber(PositiveNumberState State, long Value = 0);

    private static void DiagnoseContextMetadata(JsonElement model, Dictionary<string, string> settings, bool configuredNativeCodex,
        Action<string, string, string, string, string> add)
    {
        var context = SettingNumber(settings, "model_context_window");
        if (context.State == PositiveNumberState.Missing) context = JsonNumber(model, "context_window");
        var maximum = SettingNumber(settings, "model_max_context_window");
        if (maximum.State == PositiveNumberState.Missing) maximum = JsonNumber(model, "max_context_window");
        var explicitCompact = SettingNumber(settings, "model_auto_compact_token_limit");
        if (explicitCompact.State == PositiveNumberState.Missing) explicitCompact = JsonNumber(model, "auto_compact_token_limit");
        // A root/profile override is effective when present. A maximum is optional; when
        // supplied it must be positive and no smaller than the effective context window.
        var contextInvalid = context.State == PositiveNumberState.Invalid || maximum.State == PositiveNumberState.Invalid ||
            (context.State == PositiveNumberState.Positive && maximum.State == PositiveNumberState.Positive && maximum.Value < context.Value);
        var compactInvalid = explicitCompact.State == PositiveNumberState.Invalid;
        add("client.context_window", contextInvalid ? "blocked" : context.State == PositiveNumberState.Positive ? "ok" : configuredNativeCodex ? "warning" : "unknown",
            contextInvalid ? "Effective context metadata is structurally invalid: a value is non-positive or a supplied maximum is below the window." :
            context.State == PositiveNumberState.Positive ? "A positive effective context window was observed; any supplied maximum is not smaller." :
            configuredNativeCodex ? "Configured native Codex route metadata has no positive effective context window; no provider entitlement is inferred." :
            "Generic model context remains unobserved; this is not a free-endpoint failure.",
            "selected catalog and root/profile context overrides", "Repair the catalog or explicit context override when structurally invalid; otherwise retain a known window or explicit positive override.");
        add("client.auto_compact", compactInvalid ? "blocked" : explicitCompact.State == PositiveNumberState.Positive ? "ok" :
            contextInvalid ? "blocked" : context.State == PositiveNumberState.Positive ? "ok" : configuredNativeCodex ? "warning" : "unknown",
            compactInvalid ? "An explicit automatic-compaction token limit is structurally invalid or non-positive." :
            explicitCompact.State == PositiveNumberState.Positive ? "An explicit positive automatic-compaction limit was observed." :
            contextInvalid ? "Automatic compaction cannot be derived while effective context metadata is structurally invalid." :
            context.State == PositiveNumberState.Positive ? "A known effective context window can provide the client-derived compaction threshold." :
            configuredNativeCodex ? "Configured native Codex route metadata supplies neither an explicit compaction limit nor a known derivable window." :
            "Generic model compaction remains unknown; this is not a free-endpoint failure.",
            "selected catalog and root/profile context overrides", "Retain an explicit positive compaction limit or a known context window.");
    }

    private static OptionalPositiveNumber SettingNumber(Dictionary<string, string> settings, string key)
    {
        if (!settings.TryGetValue(key, out var value)) return new(PositiveNumberState.Missing);
        return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) && number > 0 ?
            new(PositiveNumberState.Positive, number) : new(PositiveNumberState.Invalid);
    }

    private static OptionalPositiveNumber JsonNumber(JsonElement model, string name)
    {
        if (!model.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) return new(PositiveNumberState.Missing);
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var number) || number <= 0)
            return new(PositiveNumberState.Invalid);
        return new(PositiveNumberState.Positive, number);
    }

    private static string StripComment(string value)
    {
        char quote = '\0'; var escaped = false;
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (escaped) { escaped = false; continue; }
            if (quote == '"' && c == '\\') { escaped = true; continue; }
            if (quote != '\0') { if (c == quote) quote = '\0'; continue; }
            if (c is '"' or '\'') quote = c;
            if (quote == '\0' && c == '#') return value[..i].TrimEnd();
        }
        return value;
    }

    private static void RejectDuplicates(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            { if (!names.Add(property.Name)) throw new InvalidDataException(); RejectDuplicates(property.Value); }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) RejectDuplicates(item);
    }

    // A bounded subset for observations, not general TOML validation. Irrelevant
    // sections are discarded structurally, so their model-like keys cannot leak.
    private static Dictionary<string, Dictionary<string, string>> Parse(string? text)
    {
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal) { [""] = new(StringComparer.Ordinal) };
        if (text is null) return result;
        if (text.Length > 2 * 1024 * 1024) throw new InvalidDataException();
        string? section = "";
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            // Do not mistake content of unsupported multiline values for tables.
            if (line.Contains("\"\"\"", StringComparison.Ordinal) || line.Contains("'" + "''", StringComparison.Ordinal)) throw new InvalidDataException();
            if (line.StartsWith('['))
            {
                var array = line.StartsWith("[[", StringComparison.Ordinal);
                var clean = StripComment(line).TrimEnd();
                var end = array ? "]]" : "]";
                if (!clean.EndsWith(end, StringComparison.Ordinal)) throw new InvalidDataException();
                var body = clean[(array ? 2 : 1)..^end.Length];
                var parts = HeaderParts(body);
                section = parts.Count == 2 && parts[0] == "profiles" ? "profiles." + parts[1] :
                    parts.SequenceEqual(new[] { "model_providers", "lain", "auth" }) ? "model_providers.lain.auth" :
                    parts.SequenceEqual(new[] { "model_providers", "lain" }) ? "model_providers.lain" : null;
                if (section is not null)
                {
                    if (array || !result.TryAdd(section, new(StringComparer.Ordinal))) throw new InvalidDataException();
                }
                continue;
            }
            var equal = line.IndexOf('=');
            if (equal < 0) throw new InvalidDataException();
            var literal = StripComment(line[(equal + 1)..].Trim());
            RequireSingleLineValue(literal);
            if (section is null) continue;
            var key = line[..equal].Trim();
            if (key.StartsWith('"')) key = JsonSerializer.Deserialize<string>(key) ?? throw new InvalidDataException();
            else if (key.StartsWith('\''))
            {
                if (key.Length < 2 || !key.EndsWith('\'') || key[1..^1].Contains('\'')) throw new InvalidDataException();
                key = key[1..^1];
            }
            if ((section == "model_providers.lain" && key == "auth") ||
                (section == "" && (key == "model_providers" || key.StartsWith("model_providers.", StringComparison.Ordinal))))
            {
                result.TryAdd("!auth-unsupported", new(StringComparer.Ordinal));
                continue;
            }
            var modelSection = section == "" || section.StartsWith("profiles.", StringComparison.Ordinal);
            if (modelSection ? key is not ("model" or "model_provider" or "model_catalog_json" or "model_reasoning_effort" or "profile" or
                    "model_context_window" or "model_max_context_window" or "model_auto_compact_token_limit") :
                section != "model_providers.lain.auth" || key is not ("command" or "args")) continue;
            string value;
            if (key == "args") value = JsonSerializer.Serialize(JsonSerializer.Deserialize<string[]>(literal) ?? throw new InvalidDataException());
            else if (key is "model_context_window" or "model_max_context_window" or "model_auto_compact_token_limit")
            {
                if (!long.TryParse(literal, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var integer)) throw new InvalidDataException();
                value = integer.ToString(CultureInfo.InvariantCulture);
            }
            else value = JsonSerializer.Deserialize<string>(literal) ?? throw new InvalidDataException();
            if (!result[section].TryAdd(key, value)) throw new InvalidDataException();
        }
        return result;
    }

    private static List<string> HeaderParts(string body)
    {
        var parts = new List<string>();
        var i = 0;
        while (i < body.Length)
        {
            while (i < body.Length && char.IsWhiteSpace(body[i])) i++;
            if (i == body.Length) throw new InvalidDataException();
            string part;
            if (body[i] is '"' or '\'')
            {
                var quote = body[i++]; var start = i - 1; var escaped = false;
                while (i < body.Length)
                {
                    var c = body[i++];
                    if (escaped) { escaped = false; continue; }
                    if (quote == '"' && c == '\\') { escaped = true; continue; }
                    if (c != quote) continue;
                    break;
                }
                if (i <= start + 1 || body[i - 1] != quote) throw new InvalidDataException();
                part = quote == '"' ? JsonSerializer.Deserialize<string>(body[start..i]) ?? throw new InvalidDataException() : body[(start + 1)..(i - 1)];
            }
            else
            {
                var start = i;
                while (i < body.Length && (char.IsAsciiLetterOrDigit(body[i]) || body[i] is '_' or '-')) i++;
                if (i == start) throw new InvalidDataException();
                part = body[start..i];
            }
            parts.Add(part);
            while (i < body.Length && char.IsWhiteSpace(body[i])) i++;
            if (i == body.Length) break;
            if (body[i++] != '.' || i == body.Length) throw new InvalidDataException();
        }
        if (parts.Count == 0) throw new InvalidDataException();
        return parts;
    }

    private static void RequireSingleLineValue(string value)
    {
        char quote = '\0'; var escaped = false;
        var stack = new Stack<char>();
        foreach (var c in value)
        {
            if (quote != '\0')
            {
                if (escaped) { escaped = false; continue; }
                if (quote == '"' && c == '\\') { escaped = true; continue; }
                if (c == quote) quote = '\0';
                continue;
            }
            if (c is '"' or '\'') quote = c;
            else if (c is '[' or '{') stack.Push(c);
            else if (c is ']' or '}')
            { if (stack.Count == 0 || stack.Pop() != (c == ']' ? '[' : '{')) throw new InvalidDataException(); }
        }
        if (quote != '\0' || stack.Count != 0) throw new InvalidDataException();
    }
}

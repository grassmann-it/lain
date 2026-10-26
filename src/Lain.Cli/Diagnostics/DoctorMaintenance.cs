using System.Text.Json;

namespace Lain.Cli.Diagnostics;

/// <summary>Pins copied from clients/codex/qualified-releases.json and scripts/clients/vscode_groups.py.
/// These establish identity of a few observed files, never complete package integrity or activation.</summary>
internal static class DoctorMaintenance
{
    private static readonly HashSet<string> CliPins = new(StringComparer.Ordinal)
    {
        "5b3a058525925731f12eec3366fd6dff74f716fb2967791e4531e88e7a06d484",
        "0d45eb864e1773cdd692194f3d1e7b2056614560444df5ed48e027733aab349a"
    };
    private const string HelperPin = "8ee4f472e92ac226a189e98998633b2d07f989f208515750764db660369f2926";
    internal static void Diagnose(IDoctorEnvironment env, string target, Dictionary<string, string> versions,
        Action<string, string, string, string, string> add, CancellationToken ct)
    {
        if (target == "codex-vscode") { DiagnoseExtension(env, versions, add); return; }
        var text = env.MaintenanceDirectory is { } directory ? env.ReadText(Path.Combine(directory, "manifest.json")) : null;
        if (text is null)
        { add("client.version", "unknown", "No supported launcher maintenance manifest was found.", "launcher manifest", "Review the client maintenance installation."); return; }
        using var document = JsonDocument.Parse(text);
        var manifest = document.RootElement;
        if (manifest.GetProperty("schema").GetInt32() is not (2 or 3)) throw new InvalidDataException();
        var version = manifest.GetProperty("patchedVersion").GetString();
        var observed = Version.TryParse(version, out var parsed);
        var old = observed && parsed! < new Version(0, 164, 0);
        // Do not echo untrusted manifest strings. Only numeric parsed version components.
        if (observed) versions["clientManifest"] = parsed!.ToString();
        add("client.version", old ? "warning" : "unknown", old ? "Launcher metadata declares a version older than the qualified Codex CLI baseline." : "Launcher version metadata was read; selected executable version remains unverified.", "launcher manifest", "Compare the launcher binding with the qualified 0.164.0 release.");
        var relative = manifest.TryGetProperty("patchedRelativePath", out var location) ? location.GetString() : "codex-lain";
        if (relative is not ("codex-lain" or "runtime/bin/codex")) throw new InvalidDataException();
        var binary = Path.Combine(env.MaintenanceDirectory!, relative);
        var hash = env.Sha256(binary, ct);
        var pin = manifest.GetProperty("patchedSha256").GetString();
        var matches = hash is not null && hash == pin;
        var qualified = matches && CliPins.Contains(hash!);
        add("client.binary", hash is null ? "unknown" : !matches ? "blocked" : qualified ? "ok" : "unknown",
            hash is null ? "Managed client bytes were not observed." : !matches ? "Managed client bytes differ from the maintenance manifest." : qualified ? "Managed client bytes match a source-pinned qualified CLI binary; launcher selection is unverified." : "Managed client bytes match the manifest but lack a known qualification pin.", "managed binary SHA256 and launcher manifest", "Use the qualified client maintenance status before rebinding.");
        if (qualified) versions["managedClient"] = "0.164.0";
        var helperPath = Path.Combine(Path.GetDirectoryName(binary)!, "codex-code-mode-host");
        if (!env.IsFile(helperPath))
        { add("client.runtime_helper", "blocked", "The managed code-mode helper is missing.", "managed runtime layout", "Restore the qualified helper through client maintenance."); return; }
        var helperHash = env.Sha256(helperPath, ct);
        add("client.runtime_helper", helperHash is null ? "unknown" : helperHash == HelperPin ? "ok" : "blocked",
            helperHash is null ? "Managed helper identity could not be observed." : helperHash == HelperPin ? "Managed code-mode helper matches the source-pinned helper binary; execution was not tested." : "Managed helper does not match the qualified code-mode helper pin.", "managed helper SHA256", "Review qualified helper maintenance; no helper was started.");
    }

    private static void DiagnoseExtension(IDoctorEnvironment env, Dictionary<string, string> versions,
        Action<string, string, string, string, string> add)
    {
        var text = env.ExtensionsDirectory is { } directory ? env.ReadText(Path.Combine(directory, "extensions.json")) : null;
        if (text is null)
        { add("client.version", "unknown", "The default VS Code extension registry was not observed.", "default VS Code registry", "Check the intended VS Code profile and extensions directory."); return; }
        using var document = JsonDocument.Parse(text);
        var matches = document.RootElement.EnumerateArray().Where(entry =>
            entry.GetProperty("identifier").GetProperty("id").GetString()?.Equals("openai.chatgpt", StringComparison.OrdinalIgnoreCase) == true).ToArray();
        if (matches.Length != 1)
        { add("client.vsix_selection", "unknown", "Default registry selection is missing or ambiguous.", "default VS Code registry", "Review the intended VS Code profile extension selection."); return; }
        var entry = matches[0];
        var relative = entry.GetProperty("relativeLocation").GetString();
        if (string.IsNullOrWhiteSpace(relative) || relative is "." or ".." || relative.IndexOfAny(['/', '\\']) >= 0) throw new InvalidDataException();
        var location = Path.Combine(env.ExtensionsDirectory!, relative);
        var registryLocation = entry.GetProperty("location");
        if (registryLocation.GetProperty("scheme").GetString() != "file" || registryLocation.GetProperty("path").GetString() != location) throw new InvalidDataException();
        using var package = JsonDocument.Parse(env.ReadText(Path.Combine(location, "package.json")) ?? throw new InvalidDataException());
        var version = entry.GetProperty("version").GetString();
        if (package.RootElement.GetProperty("version").GetString() != version ||
            package.RootElement.GetProperty("publisher").GetString() != "openai" || package.RootElement.GetProperty("name").GetString() != "chatgpt") throw new InvalidDataException();
        var observed = Version.TryParse(version, out var parsed);
        if (observed) versions["selectedExtensionMetadata"] = parsed!.ToString();
        var old = observed && parsed! < new Version(26, 1021, 22028);
        add("client.version", old ? "warning" : "unknown", old ? "Selected default-registry extension metadata is older than the qualified local extension baseline." : "Selected extension metadata was observed; complete extension qualification is unverified.", "default registry and package metadata", "Compare with qualified local extension 26.1021.22028.");
        add("client.vsix_selection", "ok", "Default registry and package metadata agree on the selected extension; profile overrides and running activation remain unknown.", "default VS Code registry and package metadata", "Confirm selection in the intended VS Code profile before reload.");
    }
}

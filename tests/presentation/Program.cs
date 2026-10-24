using Lain.Desktop.Presentation;

var assertions = 0;
var root = Path.Combine(Path.GetTempPath(), "lain-presentation-tests-" + Guid.NewGuid().ToString("N"));
var originalHome = Environment.GetEnvironmentVariable("LAIN_HOME");
Directory.CreateDirectory(root);
try
{
    var path = Path.Combine(root, "explicit", "preferences.json");
    var store = new WorkspacePreferencesStore(path);
    Check(store.Current.Level == InformationLevel.Simple && store.Current.AccountSort == "favorites" &&
          store.Current.Densities.Count == 0 && store.Current.PinnedAccountIds.Length == 0,
        "a missing preference file defaults to Simple, Normal, favorites and no pins");
    Check(store.Error is null && !Directory.Exists(Path.GetDirectoryName(path)), "loading missing preferences has no write side effect");
    var requested = store.Current with
    {
        Level = InformationLevel.Advanced,
        Densities = new() { ["Accounts"] = ViewDensity.Compact, ["Live"] = ViewDensity.Normal },
        AccountSort = "provider",
        GroupAccountsByProvider = true,
        PinnedAccountIds = ["account-b", "account-a"]
    };
    Check(store.Save(requested), "valid preferences save");
    var reloaded = new WorkspacePreferencesStore(path);
    Check(reloaded.Error is null && reloaded.Current.Level == InformationLevel.Advanced &&
          reloaded.Current.Densities["Accounts"] == ViewDensity.Compact &&
          reloaded.Current.Densities["Live"] == ViewDensity.Normal &&
          reloaded.Current.AccountSort == "provider" && reloaded.Current.GroupAccountsByProvider &&
          reloaded.Current.PinnedAccountIds.SequenceEqual(new[] { "account-b", "account-a" }),
        "all presentation fields and pin ordering survive reload");
    if (!OperatingSystem.IsWindows())
    {
        Check(File.GetUnixFileMode(path) == (UnixFileMode.UserRead | UnixFileMode.UserWrite), "preference file is private");
        Check(File.GetUnixFileMode(Path.GetDirectoryName(path)!) ==
              (UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute), "new preference directory is private");
    }
    requested.Densities["Accounts"] = ViewDensity.Normal;
    requested.PinnedAccountIds[0] = "caller-mutated";
    var snapshot = store.Current;
    snapshot.Densities.Clear();
    snapshot.PinnedAccountIds[0] = "snapshot-mutated";
    Check(store.Current.Densities["Accounts"] == ViewDensity.Compact && store.Current.PinnedAccountIds[0] == "account-b",
        "caller mutation cannot alter stored or loaded state");

    var beforeInvalidSave = File.ReadAllText(path);
    WorkspacePreferences[] invalid =
    [
        store.Current with { Version = 2 },
        store.Current with { Level = (InformationLevel)80 },
        store.Current with { Densities = new() { ["Overview"] = ViewDensity.Normal } },
        store.Current with { Densities = new() { ["Accounts"] = (ViewDensity)7 } },
        store.Current with { Densities = null! },
        store.Current with { AccountSort = "remaining-quota" },
        store.Current with { AccountSort = "Five-hour" },
        store.Current with { PinnedAccountIds = ["duplicate", "duplicate"] },
        store.Current with { PinnedAccountIds = [""] },
        store.Current with { PinnedAccountIds = ["../outside"] },
        store.Current with { PinnedAccountIds = [new string('a', 101)] },
        store.Current with { PinnedAccountIds = Enumerable.Range(0, 513).Select(i => "account-" + i).ToArray() },
        store.Current with { PinnedAccountIds = null! },
        null!
    ];
    foreach (var value in invalid)
    {
        Check(!store.Save(value) && store.Error is not null, "invalid in-memory preferences are rejected");
        Check(File.ReadAllText(path) == beforeInvalidSave && store.Current.Level == InformationLevel.Advanced,
            "invalid save retains last successful state and file");
    }

    var corruptPath = Path.Combine(root, "corrupt.json");
    foreach (var json in new[]
    {
        "{broken", "null", "{\"version\":2}", "{\"level\":\"Impossible\"}", "{\"level\":99}",
        "{\"densities\":{\"Setup\":\"Normal\"}}", "{\"densities\":null}",
        "{\"pinnedAccountIds\":[\"a\",\"a\"]}", "{\"accountSort\":\"quota\"}", "{\"unexpected\":true}"
    })
    {
        File.WriteAllText(corruptPath, json);
        var corrupt = new WorkspacePreferencesStore(corruptPath);
        Check(corrupt.Error is not null && corrupt.Current.Level == InformationLevel.Simple &&
              corrupt.Current.Densities.Count == 0, "corrupt or invalid preferences warn and fall back as one unit");
        Check(File.ReadAllText(corruptPath) == json, "loading corrupt preferences preserves original evidence");
    }
    File.WriteAllText(corruptPath, new string(' ', 256 * 1024 + 1));
    var oversized = new WorkspacePreferencesStore(corruptPath);
    Check(oversized.Error is not null, "oversized preference file is rejected before deserialization");
    Check(oversized.Save(oversized.Current with { Level = InformationLevel.Barebones }) && oversized.Error is null &&
          new WorkspacePreferencesStore(corruptPath).Current.Level == InformationLevel.Barebones,
        "an explicit successful save repairs invalid preferences and clears the warning");

    // A directory at the destination forces rename failure even when tests run as root.
    File.Move(path, path + ".backup");
    Directory.CreateDirectory(path);
    Check(!store.Save(store.Current with { Level = InformationLevel.Barebones }) &&
          store.Current.Level == InformationLevel.Advanced && store.Error is not null,
        "failed atomic replacement preserves the current snapshot");
    Check(Directory.Exists(path) && !Directory.EnumerateFiles(Path.GetDirectoryName(path)!, "*.tmp").Any(),
        "failed writes preserve destination and remove temporary files");
    Directory.Delete(path);
    File.Move(path + ".backup", path);
    Check(File.ReadAllText(path) == beforeInvalidSave, "failed replacement leaves the previous durable file intact");
    using (var heldLock = new FileStream(path + ".lock", FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        Check(!store.Save(store.Current with { Level = InformationLevel.Barebones }) && store.Current.Level == InformationLevel.Advanced,
            "busy inter-window lock fails without advancing local state");
    Check(store.Save(store.Current) && store.Error is null, "a later successful save recovers after write failure");

    var sharedPath = Path.Combine(root, "multiwindow.json");
    var firstWindow = new WorkspacePreferencesStore(sharedPath);
    var secondWindow = new WorkspacePreferencesStore(sharedPath);
    Check(firstWindow.Save(firstWindow.Current with { Level = InformationLevel.Advanced,
        Densities = new() { ["Accounts"] = ViewDensity.Compact } }), "first window saves edits");
    Check(secondWindow.Save(secondWindow.Current with { AccountSort = "name",
        Densities = new() { ["Live"] = ViewDensity.Compact }, PinnedAccountIds = ["saved-pin"] }), "stale window saves independent edits");
    Check(secondWindow.Current.Level == InformationLevel.Advanced && secondWindow.Current.Densities.Count == 2,
        "independent field and per-section changes merge across stale windows");
    Check(firstWindow.Save(firstWindow.Current with { GroupAccountsByProvider = true }), "first stale window saves another independent edit");
    var merged = new WorkspacePreferencesStore(sharedPath).Current;
    Check(merged.GroupAccountsByProvider && merged.AccountSort == "name" && merged.PinnedAccountIds[0] == "saved-pin" &&
          merged.Densities.Count == 2, "unmodified sort, pins and densities are retained from the latest disk version");

    var fiveHourPath = Path.Combine(root, "five-hour.json");
    var sortWindow = new WorkspacePreferencesStore(fiveHourPath);
    var densityWindow = new WorkspacePreferencesStore(fiveHourPath);
    Check(sortWindow.Save(sortWindow.Current with { AccountSort = "five-hour" }), "explicit five-hour account order saves");
    Check(new WorkspacePreferencesStore(fiveHourPath).Current.AccountSort == "five-hour",
        "five-hour account order survives restart without changing the default order");
    Check(densityWindow.Save(densityWindow.Current with { Densities = new() { ["Accounts"] = ViewDensity.Compact } }) &&
          densityWindow.Current.AccountSort == "five-hour", "a stale density edit preserves the explicit five-hour order");
    Check(sortWindow.Save(sortWindow.Current with { AccountSort = "provider" }) &&
          sortWindow.Current.Densities["Accounts"] == ViewDensity.Compact &&
          new WorkspacePreferencesStore(fiveHourPath).Current.AccountSort == "provider",
        "changing away from five-hour order persists and retains independent presentation edits");

    if (!OperatingSystem.IsWindows())
    {
        var target = Path.Combine(root, "unrelated.json");
        File.WriteAllText(target, "unchanged");
        var linked = Path.Combine(root, "linked.json");
        File.CreateSymbolicLink(linked, target);
        var linkedStore = new WorkspacePreferencesStore(linked);
        Check(linkedStore.Error is not null && !linkedStore.Save(new()) && File.ReadAllText(target) == "unchanged",
            "symbolic preference targets are rejected without modifying their target");
        var linkedDirectory = Path.Combine(root, "linked-directory");
        Directory.CreateSymbolicLink(linkedDirectory, Path.GetDirectoryName(path)!);
        Check(!new WorkspacePreferencesStore(Path.Combine(linkedDirectory, "other.json")).Save(new()),
            "symbolic parent directories are rejected");
    }

    var homeA = Path.Combine(root, "home-a");
    var homeB = Path.Combine(root, "home-b");
    Directory.CreateDirectory(homeA);
    File.WriteAllText(Path.Combine(homeA, "configuration.json"), "{\"revision\":41}");
    Environment.SetEnvironmentVariable("LAIN_HOME", homeA);
    var isolatedA = new WorkspacePreferencesStore();
    Check(isolatedA.Save(isolatedA.Current with { Level = InformationLevel.Barebones }), "LAIN_HOME preferences save without a running host");
    Environment.SetEnvironmentVariable("LAIN_HOME", homeB);
    var isolatedB = new WorkspacePreferencesStore();
    Check(isolatedB.Current.Level == InformationLevel.Simple && isolatedB.Error is null &&
          !Directory.Exists(homeB), "separate LAIN_HOME has defaults and no writes from reads");
    Check(isolatedB.Save(isolatedB.Current with { Level = InformationLevel.Advanced }), "second isolated home saves independently");
    Environment.SetEnvironmentVariable("LAIN_HOME", homeA);
    Check(new WorkspacePreferencesStore().Current.Level == InformationLevel.Barebones &&
          File.ReadAllText(Path.Combine(homeA, "configuration.json")) == "{\"revision\":41}" &&
          Directory.EnumerateFiles(homeA, "*", SearchOption.AllDirectories).Count() == 3,
        "preferences persist separately and never change configuration or revision state");
    Check(isolatedA.Save(isolatedA.Current with { AccountSort = "name" }), "a constructed store keeps its original home if environment changes");
    Console.WriteLine($"Presentation preferences: {assertions} checks passed.");
}
finally
{
    Environment.SetEnvironmentVariable("LAIN_HOME", originalHome);
    Directory.Delete(root, recursive: true);
}

void Check(bool condition, string description)
{
    assertions++;
    if (!condition) throw new InvalidOperationException("FAILED: " + description);
}

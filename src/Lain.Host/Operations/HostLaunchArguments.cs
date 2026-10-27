namespace Lain.Host.Operations;

/// <summary>Direct native service launchers can pass their installation paths without a shell wrapper.</summary>
public static class HostLaunchArguments
{
    public static string[] Apply(string[] arguments)
    {
        var remaining = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < arguments.Length; index++)
        {
            var name = arguments[index];
            var variable = name switch { "--lain-home" => "LAIN_HOME", "--lain-proxy-path" => "LAIN_PROXY_PATH", _ => null };
            if (variable is null) { remaining.Add(name); continue; }
            if (!seen.Add(name) || ++index >= arguments.Length || string.IsNullOrWhiteSpace(arguments[index]) ||
                !Path.IsPathFullyQualified(arguments[index])) throw new ArgumentException("Invalid owned service launch path.");
            Environment.SetEnvironmentVariable(variable, Path.GetFullPath(arguments[index]));
        }
        return remaining.ToArray();
    }
}

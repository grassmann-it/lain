using System.Globalization;
using Lain.Core;

namespace Lain.Client;

public static class UsageDisplayFacts
{
    public static IEnumerable<(string Name, TokenComponentMeasurementDto Value)> Components(TokenMeasurementsDto values)
    {
        yield return ("Total", values.Total);
        yield return ("Input", values.Input);
        yield return ("Output", values.Output);
        yield return ("Cached", values.Cached);
        yield return ("Cache creation", values.CacheCreation);
        yield return ("Reasoning", values.Reasoning);
    }

    public static string Describe(string name, TokenComponentMeasurementDto value) =>
        name + " · known reported " + (value.KnownReported?.ToString("N0", CultureInfo.CurrentCulture) ?? "unknown")
        + $" · measured {value.Samples:N0}/{value.Eligible:N0} attempts";
}

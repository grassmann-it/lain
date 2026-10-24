using System.Globalization;
using System.Net;
using Lain.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Lain.Host.Inspector;

public static class InspectorEndpoints
{
    /// <summary>Registers RAM storage and the own-core reader. Host authentication remains in Program's middleware.</summary>
    public static IServiceCollection AddInspector(this IServiceCollection services, string managementToken, Uri? coreAddress = null,
        Func<bool>? shouldCollect = null)
    {
        coreAddress ??= new Uri("http://127.0.0.1:18417/");
        if (coreAddress.Scheme != "http" || !IPAddress.TryParse(coreAddress.Host, out var ip) || !IPAddress.IsLoopback(ip) ||
            !string.IsNullOrEmpty(coreAddress.UserInfo) || string.IsNullOrWhiteSpace(managementToken))
            throw new ArgumentException("Inspector requires the authenticated owned loopback core.");
        services.AddSingleton<InspectorStore>();
        services.AddSingleton<InspectorCollector>(provider =>
        {
            var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false })
                { BaseAddress = coreAddress, Timeout = TimeSpan.FromSeconds(10) };
            http.DefaultRequestHeaders.Authorization = new("Bearer", managementToken);
            return new(http, provider.GetRequiredService<InspectorStore>(), provider.GetRequiredService<ILogger<InspectorCollector>>(), shouldCollect);
        });
        services.AddHostedService(provider => provider.GetRequiredService<InspectorCollector>());
        return services;
    }

    public static IEndpointRouteBuilder MapInspectorEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(Api.Prefix + "/inspector", (HttpRequest request, InspectorStore store) =>
            store.Snapshot(new(Get(request, "requestId"), Number(request, "limit", 100))));
        endpoints.MapGet(Api.Prefix + "/inspector/events", (HttpRequest request, InspectorStore store) =>
            store.Events(new(Get(request, "sourceId"), Long(request, "after", 0), Number(request, "limit", 100), Get(request, "requestId"))));
        return endpoints;
    }

    private static string? Get(HttpRequest request, string name)
    {
        if (!request.Query.TryGetValue(name, out var values)) return null;
        if (values.Count != 1 || string.IsNullOrEmpty(values[0]) || values[0]!.Length > 256 || values[0]!.Any(char.IsControl))
            throw new ArgumentException("Invalid inspector query value.");
        return values[0];
    }
    private static int Number(HttpRequest request, string name, int fallback) => Get(request, name) is { } value ? int.Parse(value, NumberStyles.None, CultureInfo.InvariantCulture) : fallback;
    private static long Long(HttpRequest request, string name, long fallback) => Get(request, name) is { } value ? long.Parse(value, NumberStyles.None, CultureInfo.InvariantCulture) : fallback;
}

using System.Net.Http;
using Lain.Core;

namespace Lain.Client;

public sealed partial class LainApiClient
{
    public Task<WorkerPlan> PlanWorkersAsync(WorkerPlanRequest request, CancellationToken ct = default) =>
        Send<WorkerPlan>(HttpMethod.Post, "workers/plan", request, ct);
}

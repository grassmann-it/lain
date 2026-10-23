namespace Lain.Host.Providers.NvidiaStep;

/// <summary>Public configuration checks; accepts no credentials and performs no network requests.</summary>
public static class NvidiaStepConfiguration
{
    public static IReadOnlyList<string> Validate(string providerId, string billingPlanId, string protocol, string baseUrl)
    {
        var normalized = baseUrl?.TrimEnd('/');
        if (providerId == "nvidia-build")
            return billingPlanId == "developer" && protocol == "openai-chat" && normalized == "https://integrate.api.nvidia.com/v1"
                ? [] : ["NVIDIA Build requires the developer plan and official OpenAI Chat Completions base URL."];
        if (providerId != "stepfun") return ["Provider is not handled by the NVIDIA/StepFun configuration validator."];
        if (billingPlanId == "payg")
            return protocol == "openai-chat" && normalized == "https://api.stepfun.com/v1"
                ? [] : ["StepFun PAYG requires https://api.stepfun.com/v1 with OpenAI Chat Completions; Step Plan is a separate budget."];
        if (billingPlanId is not ("step-plan-mini" or "step-plan-plus" or "step-plan-pro" or "step-plan-max"))
            return ["Select a documented PAYG or Step Plan billing plan explicitly."];
        return (protocol, normalized) switch
        {
            ("openai-chat", "https://api.stepfun.com/step_plan/v1") => [],
            ("anthropic-messages", "https://api.stepfun.com/step_plan") => [],
            _ => ["Step Plan requires its dedicated path. Anthropic SDK base URL omits /v1; never substitute the PAYG endpoint."]
        };
    }
}

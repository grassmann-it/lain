namespace Lain.Host.Accounts.CliConnections;

/// <summary>No process is started until each specific manufacturer boundary has evidence.</summary>
public abstract class UnavailableCliLoginAdapter : IAccountLoginAdapter
{
    public abstract string Provider { get; }
    public string Method => "official-cli";
    public abstract string QualificationBlocker { get; }
    public Task<LoginAdapterResult> BeginAsync(LoginOperationContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (context.Request.Provider != Provider || context.Request.Method != Method) throw new ArgumentException("Invalid CLI connection context.");
        return Task.FromResult(new LoginAdapterResult("failed", Error: QualificationBlocker, NextAction: "use-api-key"));
    }
    public Task<LoginAdapterResult> StatusAsync(LoginOperationContext context, CancellationToken ct) => BeginAsync(context, ct);
    public Task CancelAsync(LoginOperationContext context, CancellationToken ct) => Task.CompletedTask;
}

using AskLucy.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AskLucy.Persistence.Identity;

internal static partial class SystemAccountProvisioningHostedServiceLog
{
    [LoggerMessage(Level = LogLevel.Error, Message = "System account provisioning failed; downstream provisioners will each attempt their own creation and defer to the next startup on failure")]
    public static partial void Failed(ILogger logger, Exception exception);
}

/// <summary>
/// Ensures the shared <c>"system"</c> account exists <b>before</b> any other startup provisioner
/// runs, by actually awaiting the work inside <see cref="StartAsync"/> rather than the
/// fire-and-forget <c>Task.Run</c> shape every other provisioning hosted service uses. The .NET
/// Generic Host starts every registered <see cref="IHostedService"/> in registration order,
/// awaiting each one's <see cref="StartAsync"/> fully before calling the next — registering this
/// one first (in <c>AddPersistence</c>, called before <c>AddInfrastructure</c> in
/// <c>Program.cs</c>) turns "two provisioners race to create the same row" into "one provisioner
/// creates it, the rest find it already there," eliminating the race rather than only catching
/// its consequence.
///
/// <para>
/// Failures are caught and logged, never thrown: a transient failure here (e.g. an unreachable
/// database) must not abort the entire host's startup. <c>SystemAgentProvisioner</c>/
/// <c>SystemWorkflowProvisioner</c> each still call <see cref="ISystemAccountProvisioner.EnsureSystemAccountExistsAsync"/>
/// themselves as a defensive fallback — ordinarily a fast no-op once this service has already
/// created the row, but still a correct, if slower, path if this service's own attempt failed.
/// </para>
/// </summary>
public sealed class SystemAccountProvisioningHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<SystemAccountProvisioningHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var provisioner = scope.ServiceProvider.GetRequiredService<ISystemAccountProvisioner>();
            await provisioner.EnsureSystemAccountExistsAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            SystemAccountProvisioningHostedServiceLog.Failed(logger, ex);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

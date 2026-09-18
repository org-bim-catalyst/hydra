using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AskLucy.Infrastructure.SiteAnalysis;

internal static partial class SystemWorkflowProvisioningHostedServiceLog
{
    [LoggerMessage(Level = LogLevel.Error, Message = "Site analysis system workflow provisioning crashed unexpectedly; it will retry on the next startup")]
    public static partial void Crashed(ILogger logger, Exception exception);
}

/// <summary>
/// Runs <see cref="SystemWorkflowProvisioner.ProvisionAsync"/> once as the app starts, mirroring
/// <c>SystemAgentProvisioningHostedService</c>'s fire-and-forget <c>Task.Run</c>-inside-<c>StartAsync</c>
/// shape exactly. A fresh <see cref="IServiceScope"/> is required because
/// <see cref="SystemWorkflowProvisioner"/> and its repository dependencies are scoped, while this
/// hosted service, like every <see cref="IHostedService"/>, is a singleton.
/// </summary>
public sealed class SystemWorkflowProvisioningHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<SystemWorkflowProvisioningHostedService> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var provisioner = scope.ServiceProvider.GetRequiredService<SystemWorkflowProvisioner>();
                await provisioner.ProvisionAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                // The provisioner itself never throws (constitution §2.VIII) — this guards against
                // a failure in resolving the scope/service graph itself, which must not take the
                // host down.
                SystemWorkflowProvisioningHostedServiceLog.Crashed(logger, ex);
            }
        }, CancellationToken.None);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

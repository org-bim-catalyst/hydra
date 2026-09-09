using AskLucy.Application.Conversations.SystemAgents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AskLucy.Infrastructure.Conversations;

internal static partial class SystemAgentProvisioningHostedServiceLog
{
    [LoggerMessage(Level = LogLevel.Information,
        Message = "System agent provisioning completed: {Created} created, {Upgraded} upgraded, {Unchanged} unchanged")]
    public static partial void Completed(ILogger logger, int created, int upgraded, int unchanged);

    [LoggerMessage(Level = LogLevel.Error, Message = "System agent provisioning crashed unexpectedly; it will retry on the next startup")]
    public static partial void Crashed(ILogger logger, Exception exception);
}

/// <summary>
/// Runs <see cref="ISystemAgentProvisioner.ProvisionAsync"/> once as the app starts (specs/045
/// FR-033, SC-011), following <c>WhisperWarmupHostedService</c>'s fire-and-forget
/// <c>Task.Run</c>-inside-<c>StartAsync</c> shape — a one-time startup task, not a periodic loop
/// like <c>ProviderHealthCheckHostedService</c>. A fresh <see cref="IServiceScope"/> is required
/// because <see cref="ISystemAgentProvisioner"/> and its repository dependencies are scoped, while
/// this hosted service, like every <see cref="IHostedService"/>, is a singleton.
/// </summary>
public sealed class SystemAgentProvisioningHostedService(
    IServiceScopeFactory scopeFactory,
    ISystemAgentProvisioningStatus status,
    ILogger<SystemAgentProvisioningHostedService> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var provisioner = scope.ServiceProvider.GetRequiredService<ISystemAgentProvisioner>();
                var result = await provisioner.ProvisionAsync(CancellationToken.None);

                status.RecordResult(result);

                if (!result.Deferred)
                {
                    SystemAgentProvisioningHostedServiceLog.Completed(logger, result.Created, result.Upgraded, result.Unchanged);
                }
            }
            catch (Exception ex)
            {
                // The provisioner itself never throws (constitution §2.VIII) — this guards against
                // a failure in resolving the scope/service graph itself, which must not take the
                // host down (.NET's default BackgroundServiceExceptionBehavior would otherwise
                // apply, but this is a plain IHostedService.StartAsync, not a BackgroundService,
                // so an unhandled exception inside this fire-and-forget Task would only crash the
                // task, not the host — caught anyway so the deferred status is still recorded).
                status.RecordResult(SystemAgentProvisioningResult.DeferredResult);
                SystemAgentProvisioningHostedServiceLog.Crashed(logger, ex);
            }
        }, CancellationToken.None);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

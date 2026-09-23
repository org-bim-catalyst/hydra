using AskLucy.Application.CustomModels.Jobs;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AskLucy.Infrastructure.CustomModels;

/// <summary>
/// specs/072 research D6 "Startup sweep" (FR-013). Runs <see cref="CustomModelDeploymentRecovery"/>
/// once per boot, so no deployment interrupted by a restart is left in progress forever.
/// </summary>
public sealed partial class CustomModelDeploymentRecoveryHostedService(
    CustomModelDeploymentRecovery recovery,
    TimeProvider timeProvider,
    ILogger<CustomModelDeploymentRecoveryHostedService> logger) : BackgroundService
{
    private readonly DateTime bootUtc = timeProvider.GetUtcNow().UtcDateTime;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await recovery.RunAsync(bootUtc, stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // BackgroundServiceExceptionBehavior.StopHost would take the whole app down for this.
            LogSweepFailed(logger, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Custom model startup sweep failed; interrupted deployments stay in progress until the next restart")]
    private static partial void LogSweepFailed(ILogger logger, Exception exception);
}

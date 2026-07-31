using AskLucy.Application.Abstractions;
using AskLucy.Domain.Ai;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AskLucy.Infrastructure.Ai;

internal static partial class ProviderHealthCheckLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Health check failed for provider {ProviderKey}")]
    public static partial void HealthCheckFailed(ILogger logger, Exception exception, string providerKey);

    [LoggerMessage(Level = LogLevel.Error, Message = "Provider health check cycle failed (e.g. the database was unreachable) — will retry next interval")]
    public static partial void CheckCycleFailed(ILogger logger, Exception exception);
}

/// <summary>
/// Periodically checks every enabled <see cref="AIProvider"/>'s health (FR-027, research.md
/// Decision 7) — mirrors <see cref="WhisperWarmupHostedService"/>'s background-work pattern,
/// extended to run on a recurring interval via <see cref="BackgroundService"/> instead of a
/// one-time warmup. Deliberately interval-based, not triggered per chat request, so health
/// checks never add latency to a user-facing send (spec.md Assumptions).
/// </summary>
public sealed class ProviderHealthCheckHostedService(
    IServiceScopeFactory scopeFactory,
    IOptions<ProviderHealthCheckOptions> options,
    ILogger<ProviderHealthCheckHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.Interval);

        do
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A failure here (e.g. the database itself is unreachable, not just one
                // provider) must not propagate: .NET's default BackgroundServiceExceptionBehavior
                // is StopHost, so an unhandled exception in a BackgroundService.ExecuteAsync
                // takes down the entire application, not just this periodic check.
                ProviderHealthCheckLog.CheckCycleFailed(logger, ex);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        // BackgroundService itself is a singleton; every dependency it uses here is scoped
        // (the DbContext chief among them), so a fresh scope is created per check cycle
        // rather than injected directly into the constructor.
        using var scope = scopeFactory.CreateScope();
        var providerRepository = scope.ServiceProvider.GetRequiredService<IAIProviderRepository>();
        var healthCheckRepository = scope.ServiceProvider.GetRequiredService<IProviderHealthCheckRepository>();
        var resolver = scope.ServiceProvider.GetRequiredService<IAIProviderResolver>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var providers = await providerRepository.ListEnabledAsync(cancellationToken);
        var checkedAtUtc = DateTime.UtcNow;

        foreach (var provider in providers)
        {
            bool isHealthy;
            string? detail;

            try
            {
                var aiProvider = resolver.Resolve(provider.ProviderKey);
                isHealthy = await aiProvider.CheckHealthAsync(cancellationToken);
                detail = isHealthy ? null : "Health check returned an unsuccessful response.";
            }
            catch (Exception ex)
            {
                isHealthy = false;
                // Never the raw exception message verbatim — some vendor error bodies echo
                // request headers, which could include the credential (constitution §14).
                detail = $"{ex.GetType().Name} during health check.";
                ProviderHealthCheckLog.HealthCheckFailed(logger, ex, provider.ProviderKey);
            }

            provider.UpdateHealthStatus(isHealthy, checkedAtUtc);
            healthCheckRepository.Add(ProviderHealthCheck.Create(provider.Id, checkedAtUtc, isHealthy, detail, actor: "system:health-check"));
        }

        if (providers.Count > 0)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }
}

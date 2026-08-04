using AskLucy.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AskLucy.Infrastructure.KnowledgeBases;

internal static partial class KnowledgeBasePurgeLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Automatic purge failed for knowledge base {KnowledgeBaseId} — will retry next cycle")]
    public static partial void PurgeFailed(ILogger logger, Exception exception, Guid knowledgeBaseId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Knowledge base purge sweep cycle failed (e.g. the database was unreachable) — will retry next interval")]
    public static partial void CheckCycleFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Automatically purged knowledge base {KnowledgeBaseId} (30-day retention elapsed, FR-036)")]
    public static partial void KnowledgeBasePurged(ILogger logger, Guid knowledgeBaseId);
}

/// <summary>
/// Periodically sweeps soft-deleted knowledge bases past their 30-day
/// <c>PurgeScheduledAtUtc</c> (FR-036, SC-009) — mirrors <see cref="Ai.ProviderHealthCheckHostedService"/>'s
/// background-work pattern. Uses the injectable <see cref="TimeProvider"/> (not
/// <c>DateTime.UtcNow</c> directly) so the sweep's "is this past due" logic is deterministically
/// unit-testable without a real 30-day wait.
/// </summary>
public sealed class KnowledgeBasePurgeHostedService(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    IOptions<KnowledgeBasePurgeOptions> options,
    ILogger<KnowledgeBasePurgeHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.Interval, timeProvider);

        do
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A failure here (e.g. the database itself is unreachable) must not propagate:
                // .NET's default BackgroundServiceExceptionBehavior is StopHost, so an
                // unhandled exception in BackgroundService.ExecuteAsync takes down the entire
                // application, not just this periodic sweep.
                KnowledgeBasePurgeLog.CheckCycleFailed(logger, ex);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>Runs one sweep cycle immediately, outside the periodic timer loop — public so it is directly unit-testable with an injected <see cref="TimeProvider"/> rather than waiting on a real interval.</summary>
    public async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        // BackgroundService itself is a singleton; every dependency it uses here is scoped
        // (the DbContext chief among them), so a fresh scope is created per sweep cycle rather
        // than injected directly into the constructor.
        using var scope = scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IKnowledgeBaseRepository>();
        var documentRepository = scope.ServiceProvider.GetRequiredService<IKnowledgeBaseDocumentRepository>();
        var auditLogRepository = scope.ServiceProvider.GetRequiredService<IKnowledgeBaseAuditLogRepository>();
        var fileStorage = scope.ServiceProvider.GetRequiredService<IFileStorage>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var dueKnowledgeBases = await repository.ListPastPurgeScheduleAsync(timeProvider.GetUtcNow().UtcDateTime, cancellationToken);

        foreach (var knowledgeBase in dueKnowledgeBases)
        {
            try
            {
                await PurgeOneAsync(knowledgeBase.Id, repository, documentRepository, auditLogRepository, fileStorage, unitOfWork, cancellationToken);
                KnowledgeBasePurgeLog.KnowledgeBasePurged(logger, knowledgeBase.Id);
            }
            catch (Exception ex)
            {
                // One knowledge base's purge failing (e.g. a locked file) must not block the
                // rest of the sweep or take down the host.
                KnowledgeBasePurgeLog.PurgeFailed(logger, ex, knowledgeBase.Id);
            }
        }
    }

    private static async Task PurgeOneAsync(
        Guid knowledgeBaseId,
        IKnowledgeBaseRepository repository,
        IKnowledgeBaseDocumentRepository documentRepository,
        IKnowledgeBaseAuditLogRepository auditLogRepository,
        IFileStorage fileStorage,
        IUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        // Same ordering guarantee as the owner-triggered purge (PurgeKnowledgeBaseCommandHandler,
        // FR-036 edge case): the audit log entry is committed before any file is deleted.
        auditLogRepository.Add(Domain.KnowledgeBases.KnowledgeBaseAuditLog.Create(
            knowledgeBaseId, "system:knowledge-base-purge", Domain.KnowledgeBases.KnowledgeBaseAuditAction.PermanentlyDeleted,
            "Automatically purged after 30-day retention window elapsed", "system:knowledge-base-purge"));
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var documents = await documentRepository.ListByKnowledgeBaseIdIncludingDeletedAsync(knowledgeBaseId, cancellationToken);
        foreach (var document in documents)
        {
            await fileStorage.DeleteAsync(document.StoredFileName, cancellationToken);
        }

        await repository.PurgeAsync(knowledgeBaseId, cancellationToken);
    }
}

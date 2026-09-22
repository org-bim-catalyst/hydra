using System.Security.Cryptography;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AskLucy.Web.StartupTasks;

/// <summary>
/// One-time backfill for <see cref="AskLucy.Domain.Ai.AIProvider.CredentialHint"/> on rows whose
/// credential was configured before specs/066 shipped (FR-009/research.md Decision 4). Invoked
/// unconditionally from Program.cs — unlike the <c>DevSeed/</c> seeders it otherwise mirrors in
/// shape, it must also run in production, since real deployed credentials predate this feature.
/// Idempotent: scoped to rows where <c>CredentialHint IS NULL</c>, so repeated runs (restarts
/// before a successful pass, multi-instance deploys) are a cheap no-op once complete. A single bad
/// row (e.g. encrypted under a rotated key ring) is logged and skipped, never allowed to block
/// startup or the rest of the batch.
/// </summary>
public static class CredentialHintBackfillService
{
    public static async Task RunAsync(IServiceProvider services, ILogger logger)
    {
        using var scope = services.CreateScope();
        var providers = scope.ServiceProvider.GetRequiredService<IAIProviderRepository>();
        var protector = scope.ServiceProvider.GetRequiredService<IAiCredentialProtector>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var candidates = (await providers.ListAllAsync())
            .Where(p => p.CredentialCiphertext is not null && p.CredentialHint is null)
            .ToList();

        if (candidates.Count == 0)
        {
            return;
        }

        var backfilledCount = 0;
        foreach (var provider in candidates)
        {
            try
            {
                var plaintext = protector.Unprotect(provider.CredentialCiphertext!);
                provider.BackfillCredentialHint(CredentialHintFormatter.Format(plaintext));
                backfilledCount++;
            }
            catch (CryptographicException ex)
            {
#pragma warning disable CA1848
                logger.LogWarning(ex, "Skipped credential hint backfill for provider {ProviderId} — could not decrypt its stored credential.", provider.Id);
#pragma warning restore CA1848
            }
        }

        if (backfilledCount > 0)
        {
            await unitOfWork.SaveChangesAsync();
        }

#pragma warning disable CA1848, CA1873
        logger.LogInformation("Credential hint backfill: {BackfilledCount}/{CandidateCount} provider(s) updated.", backfilledCount, candidates.Count);
#pragma warning restore CA1848, CA1873
    }
}

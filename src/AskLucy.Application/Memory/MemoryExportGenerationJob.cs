using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AskLucy.Application.Abstractions;
using Hangfire;
using Microsoft.Extensions.Logging;

namespace AskLucy.Application.Memory;

internal static partial class MemoryExportGenerationJobLog
{
    [LoggerMessage(Level = LogLevel.Error, Message = "Memory export generation failed for job {ExportJobId}")]
    public static partial void GenerationFailed(ILogger logger, Guid exportJobId, Exception exception);
}

/// <summary>
/// Implements <see cref="IMemoryExportGenerationJob"/> (spec.md FR-024, User Story 4 AC3,
/// research.md Decision 14) — a single structured, human-readable JSON file grouped by category,
/// written via <see cref="IFileStorage"/> (never a raw physical path, CLAUDE.md File Management
/// convention). An account with zero memories still produces a valid, empty export (spec.md Edge
/// Cases), not an error — an empty <c>Categories</c> array falls out naturally from grouping an
/// empty list.
/// </summary>
[AutomaticRetry(Attempts = 3, DelaysInSeconds = [30, 120, 600])]
public sealed class MemoryExportGenerationJob(
    IMemoryExportJobRepository exportJobRepository, IMemoryRepository memoryRepository,
    IUnitOfWork unitOfWork, IFileStorage fileStorage, ILogger<MemoryExportGenerationJob> logger) : IMemoryExportGenerationJob
{
    private const string SystemActor = "system:memory-export";

    private static readonly JsonSerializerOptions ExportJsonOptions = new() { WriteIndented = true };

    private sealed record ExportedMemory(
        [property: JsonPropertyName("content")] string Content,
        [property: JsonPropertyName("state")] string State,
        [property: JsonPropertyName("sourceType")] string SourceType,
        [property: JsonPropertyName("createdAtUtc")] DateTime CreatedAtUtc);

    private sealed record ExportedCategory(
        [property: JsonPropertyName("category")] string Category,
        [property: JsonPropertyName("memories")] IReadOnlyList<ExportedMemory> Memories);

    private sealed record ExportDocument(
        [property: JsonPropertyName("exportedAtUtc")] DateTime ExportedAtUtc,
        [property: JsonPropertyName("categories")] IReadOnlyList<ExportedCategory> Categories);

    public async Task RunAsync(Guid exportJobId, CancellationToken cancellationToken = default)
    {
        var job = await exportJobRepository.GetByIdAsync(exportJobId, cancellationToken);
        if (job is null)
        {
            return; // The job row itself was somehow removed before this ran — nothing to report to.
        }

        try
        {
            var memories = await memoryRepository.GetAllByUserAsync(job.UserId, cancellationToken);

            var document = new ExportDocument(
                DateTime.UtcNow,
                memories
                    .GroupBy(m => m.Category)
                    .Select(g => new ExportedCategory(
                        g.Key.ToString(),
                        g.Select(m => new ExportedMemory(m.Content, m.State.ToString(), m.SourceType.ToString(), m.CreatedAtUtc)).ToList()))
                    .ToList());

            var json = JsonSerializer.Serialize(document, ExportJsonOptions);
            await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
            var storedFileName = await fileStorage.SaveAsync(stream, "memory-export.json", cancellationToken);

            job.MarkReady(storedFileName, SystemActor);
        }
        catch (Exception ex)
        {
            MemoryExportGenerationJobLog.GenerationFailed(logger, exportJobId, ex);
            job.MarkFailed("Export generation failed. Please try again.", SystemActor);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

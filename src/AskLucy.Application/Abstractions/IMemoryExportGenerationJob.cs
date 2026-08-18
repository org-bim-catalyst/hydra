namespace AskLucy.Application.Abstractions;

/// <summary>
/// Background generation for one <see cref="AskLucy.Domain.Memory.MemoryExportJob"/> (spec.md
/// FR-024, research.md Decision 14). The concrete implementation lives in
/// <c>AskLucy.Application</c> (not <c>Infrastructure</c>) — pure orchestration over
/// <see cref="IMemoryRepository"/>/<see cref="IFileStorage"/>, the same reasoning as
/// <c>IMemoryExtractionJob</c>'s doc comment.
/// </summary>
public interface IMemoryExportGenerationJob
{
    Task RunAsync(Guid exportJobId, CancellationToken cancellationToken = default);
}

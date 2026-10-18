namespace AskLucy.Application.Abstractions;

/// <summary>specs/069 FR-011b: moves owners' items again after an administrator changes the replacement.</summary>
public interface IModelDeprecationReplacementChangeJob
{
    Task RunAsync(Guid batchId, CancellationToken cancellationToken = default);
}

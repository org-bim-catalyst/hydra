namespace AskLucy.Application.CustomModels.Abstractions;

/// <summary>
/// specs/072 research D9. An engine that runs on this server from a Hugging Face repository a
/// custom model can deploy. Kept apart from the engine's own interface (ISP), so any hosted engine
/// can opt in without a meaningless property on the engines that don't run a local model.
/// </summary>
public interface IHostedModelEngine
{
    /// <summary>The engine's name as admins know it, e.g. "Supertonic".</summary>
    string EngineName { get; }

    /// <summary>The Hugging Face repository the engine's model comes from, e.g. <c>Supertone/supertonic-3</c>.</summary>
    string ModelRepositoryId { get; }

    /// <summary>specs/072 FR-037 — why a request would fail right now (the backing custom model is
    /// Unavailable, or its files are missing), or null when the model can load. Read-only: it neither
    /// loads nor unloads the model, so an admin page can ask on every read.</summary>
    Task<string?> FindModelProblemAsync(CancellationToken cancellationToken = default);
}

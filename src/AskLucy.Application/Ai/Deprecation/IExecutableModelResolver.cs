using AskLucy.Domain.Ai;

namespace AskLucy.Application.Ai.Deprecation;

/// <summary>
/// specs/069 FR-014/FR-015 research D3: a run that names a deprecated model follows its
/// replacement chain to an Available model. Published versions can't be rewritten, so this is
/// where those runs are redirected; when no usable successor exists the run fails loudly.
/// </summary>
public interface IExecutableModelResolver
{
    Task<ExecutableModel> ResolveAsync(AIModel model, CancellationToken cancellationToken = default);
}

/// <param name="Model">The model that may actually run: Available, or not part of any deprecation.</param>
/// <param name="RetiredModelId">The deprecated model this run originally named, when it was redirected.</param>
public sealed record ExecutableModel(AIModel Model, Guid? RetiredModelId);

/// <summary>The model (and every replacement after it) is deprecated and none is usable.</summary>
public sealed class ModelRetiredException(AIModel retired)
    : Exception($"The model '{retired.DisplayName}' was retired by an administrator. Choose another model to continue.")
{
    public AIModel RetiredModel { get; } = retired;

    public string UserSafeMessage => Message;
}

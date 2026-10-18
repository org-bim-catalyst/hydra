using AskLucy.Application.Abstractions;
using AskLucy.Domain.Ai;

namespace AskLucy.Application.Ai.Deprecation;

public sealed class ExecutableModelResolver(
    IAIModelRepository models,
    IModelDeprecationRepository deprecations) : IExecutableModelResolver
{
    // A replacement chain longer than this is a data problem, not a legitimate succession.
    private const int MaxHops = 10;

    public async Task<ExecutableModel> ResolveAsync(AIModel model, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);

        var current = model;
        Guid? retiredId = null;
        var visited = new HashSet<Guid>();

        for (var hop = 0; hop <= MaxHops; hop++)
        {
            if (current.Status != AIModelStatus.Deprecated)
            {
                return new ExecutableModel(current, retiredId);
            }

            retiredId ??= current.Id;
            if (!visited.Add(current.Id))
            {
                break;
            }

            var record = await deprecations.GetByModelIdAsync(current.Id, cancellationToken);
            if (record?.ReplacementModelId is not { } successorId)
            {
                break;
            }

            var successor = await models.GetByIdAsync(successorId, cancellationToken);
            if (successor is null)
            {
                break;
            }

            current = successor;
        }

        throw new ModelRetiredException(model);
    }
}

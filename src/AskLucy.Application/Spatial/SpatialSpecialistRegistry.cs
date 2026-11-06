namespace AskLucy.Application.Spatial;

/// <summary>
/// The single place a new spatial capability becomes reachable (FR-006/FR-028). Built from the
/// injected specialists, keyed by operation kind; a duplicate registration fails at startup rather
/// than silently shadowing one specialist with another.
/// </summary>
public sealed class SpatialSpecialistRegistry
{
    private readonly Dictionary<string, ISpatialSpecialist> _byKind;

    public SpatialSpecialistRegistry(IEnumerable<ISpatialSpecialist> specialists)
    {
        var byKind = new Dictionary<string, ISpatialSpecialist>(StringComparer.OrdinalIgnoreCase);
        foreach (var specialist in specialists)
        {
            if (!byKind.TryAdd(specialist.OperationKind, specialist))
            {
                throw new InvalidOperationException(
                    $"Two spatial specialists are registered for operation kind '{specialist.OperationKind}'. " +
                    "An operation kind must have exactly one specialist.");
            }
        }

        _byKind = byKind;
    }

    /// <summary>The kinds this deployment can execute. A declared-but-unregistered kind is not performed.</summary>
    public IReadOnlyCollection<string> RegisteredKinds => _byKind.Keys;

    public bool TryGet(string? operationKind, out ISpatialSpecialist specialist)
    {
        specialist = null!;
        return operationKind is not null && _byKind.TryGetValue(operationKind, out specialist!);
    }
}

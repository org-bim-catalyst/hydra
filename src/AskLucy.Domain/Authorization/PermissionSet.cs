using System.Collections.ObjectModel;

namespace AskLucy.Domain.Authorization;

/// <summary>
/// Immutable set of permission keys for a role. Rejects unknown keys and empty sets.
/// On construction normalizes Manage permissions to include their corresponding View key.
/// </summary>
public sealed class PermissionSet : IEquatable<PermissionSet>
{
    private readonly ReadOnlyCollection<string> _keys;

    /// <summary>The entire catalogue as a built-in permission set.</summary>
    public static PermissionSet Full { get; } = new(AdminPermissionCatalog.All.Select(p => p.Key).ToHashSet());

    /// <summary>No permissions — a user with no role, or a role whose every stored key has since been retired from the catalogue.</summary>
    public static PermissionSet Empty { get; } = new([]);

    public IReadOnlyCollection<string> Keys => _keys;

    public bool IsEmpty => _keys.Count == 0;

    private PermissionSet(IEnumerable<string> keys)
    {
        var sorted = new SortedSet<string>(keys);
        _keys = new ReadOnlyCollection<string>(sorted.ToList());
    }

    /// <summary>
    /// Creates a permission set from catalogue keys.
    /// </summary>
    public static PermissionSet Create(params string[] keys) => Create(keys.AsEnumerable());

    /// <summary>
    /// Creates a permission set from a collection of catalogue keys.
    /// </summary>
    public static PermissionSet Create(IEnumerable<string> keys)
    {
        var normalized = new HashSet<string>(StringComparer.InvariantCulture);

        foreach (var key in keys)
        {
            if (!AdminPermissionCatalog.All.Any(p => p.Key == key))
                throw new ArgumentException($"Unknown permission: {key}");

            normalized.Add(key);

            // Normalize Manage -> also include View.
            var viewKey = $"{key.Substring(0, key.LastIndexOf('.'))}.view";
            if (key.EndsWith(".manage", StringComparison.InvariantCultureIgnoreCase) && !normalized.Contains(viewKey))
                normalized.Add(viewKey);
        }

        if (normalized.Count == 0)
            throw new ArgumentException("At least one permission is required.");

        return new PermissionSet(normalized);
    }

    public bool Contains(string key) => _keys.Contains(key);

    public static PermissionSet Union(PermissionSet a, PermissionSet b)
    {
        var combined = new HashSet<string>(a._keys, StringComparer.InvariantCulture);
        combined.UnionWith(b._keys);
        return new PermissionSet(combined);
    }

    public bool Equals(PermissionSet? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return _keys.SequenceEqual(other._keys);
    }

    public override bool Equals(object? obj) => Equals(obj as PermissionSet);

    public override int GetHashCode()
    {
        int hash = 17;
        foreach (var k in _keys)
            hash = hash * 31 + StringComparer.InvariantCulture.GetHashCode(k);
        return hash;
    }

    public override string ToString() => $"PermissionSet({string.Join(", ", _keys)})";
}

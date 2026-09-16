namespace AskLucy.Domain.Authorization;

/// <summary>
/// Value object representing a role name with validation rules.
/// Trimmed, 2–50 characters, not whitespace-only, rejects reserved names.
/// Equality is case-insensitive (matches ASP.NET Identity NormalizedName semantics).
/// </summary>
public sealed class RoleName : IEquatable<RoleName>
{
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "ADMINISTRATOR",
        "SUPER USER",
        "REGULAR",
        "NO ROLE"
    };

    public string Value { get; }

    private RoleName(string value) => Value = value;

    /// <summary>
    /// True if <paramref name="value"/> (trimmed) is one of the reserved names — usable for
    /// pre-validation before calling <see cref="From"/>, which rejects a reserved name outright
    /// rather than returning an instance with this flag set.
    /// </summary>
    public static bool IsNameReserved(string value) => Reserved.Contains(value.Trim());

    /// <summary>
    /// Creates a validated role name.
    /// </summary>
    /// <param name="raw">Raw input string.</param>
    /// <returns>A valid RoleName instance.</returns>
    /// <exception cref="ArgumentException">Thrown when the name is invalid.</exception>
    public static RoleName From(string raw)
    {
        ArgumentNullException.ThrowIfNull(raw);

        var trimmed = raw.Trim();

        if (string.IsNullOrWhiteSpace(trimmed))
            throw new ArgumentException("Role name cannot be empty or whitespace.", nameof(raw));

        if (trimmed.Length < 2 || trimmed.Length > 50)
            throw new ArgumentException(
                $"Role name must be between 2 and 50 characters, got {trimmed.Length}.",
                nameof(raw));

        if (Reserved.Contains(trimmed))
            throw new ArgumentException($"The role name '{trimmed}' is reserved.", nameof(raw));

        return new RoleName(trimmed);
    }

    public override bool Equals(object? obj) => obj is RoleName other && Equals(other);

    public bool Equals(RoleName? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return string.Equals(Value, other.Value, StringComparison.OrdinalIgnoreCase);
    }

    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Value);

    public static bool operator ==(RoleName? a, RoleName? b) => EqualityComparer<RoleName>.Default.Equals(a, b);

    public static bool operator !=(RoleName? a, RoleName? b) => !(a == b);

    public override string ToString() => Value;
}

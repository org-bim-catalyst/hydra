namespace AskLucy.Application.Notifications.Admin;

/// <summary>What an administrator sees of a recipient's address: the first letter and the domain (FR-009c, FR-056).</summary>
public static class AdminAddressMask
{
    public static string? Mask(string? address)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return null;
        }

        var at = address.IndexOf('@', StringComparison.Ordinal);
        return at <= 0 ? "•••" : $"{address[0]}•••{address[at..]}";
    }

    /// <summary>A name as initials ("M. S."): enough to tell two accounts apart, not enough to identify one (contract example).</summary>
    public static string? Initials(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return null;
        }

        var initials = displayName.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(word => char.ToUpperInvariant(word[0]) + ".");
        return string.Join(' ', initials);
    }
}

using System.Buffers;
using System.Diagnostics.CodeAnalysis;

namespace AskLucy.Domain.CustomModels;

/// <summary>
/// specs/072 research D5. A canonical path relative to the deployment root, for example
/// <c>Models/supertonic-3</c>, that is guaranteed to stay below one of the allowed prefixes. The
/// root itself is deliberately not a member: only the Infrastructure upload adapter joins the two,
/// so records, DTOs and progress events can never carry it (FR-018).
/// </summary>
public sealed class DeploymentDestination
{
    public const int MaxLength = 512;

    private static readonly HashSet<string> ReservedFileNames = new(StringComparer.OrdinalIgnoreCase) { "web.config", "app_offline.htm" };

    private static readonly HashSet<string> WindowsDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM0", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT0", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    private static readonly SearchValues<char> ForbiddenCharacters = SearchValues.Create(['<', '>', ':', '"', '|', '?', '*', '\\']);

    /// <summary>ONE DOT LEADER, FULLWIDTH FULL STOP and IDEOGRAPHIC FULL STOP — look like '.' and are folded to it by some file systems and FTP servers.</summary>
    private static readonly SearchValues<char> LookAlikeDots = SearchValues.Create(['․', '．', '。']);

    public string Value { get; }

    private DeploymentDestination(string value) => Value = value;

    /// <summary>Validates and canonicalises an admin-typed destination (FR-005, FR-005a).</summary>
    public static bool TryCreate(
        string? raw,
        IReadOnlyList<string> allowedPrefixes,
        [NotNullWhen(true)] out DeploymentDestination? destination,
        [NotNullWhen(false)] out string? error)
    {
        destination = null;
        if (string.IsNullOrWhiteSpace(raw))
        {
            error = "A destination is required.";
            return false;
        }

        var decoded = Uri.UnescapeDataString(raw);
        if (!string.Equals(Uri.UnescapeDataString(decoded), decoded, StringComparison.Ordinal))
        {
            error = "The destination is percent-encoded more than once.";
            return false;
        }

        if (decoded.StartsWith('/') || decoded.StartsWith('\\'))
        {
            error = "The destination must be a relative path, for example Models/my-model. It must not start with '/'.";
            return false;
        }

        if (decoded.Length >= 2 && char.IsAsciiLetter(decoded[0]) && decoded[1] == ':')
        {
            error = "The destination must be a relative path, not a drive path.";
            return false;
        }

        var segments = decoded.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            error = "A destination is required.";
            return false;
        }

        foreach (var segment in segments)
        {
            if (!TryValidateSegment(segment, out error))
            {
                return false;
            }
        }

        var canonical = string.Join('/', segments);
        if (canonical.Length > MaxLength)
        {
            error = $"The destination must be at most {MaxLength} characters.";
            return false;
        }

        if (!IsStrictlyBelowAnAllowedPrefix(canonical, allowedPrefixes))
        {
            var allowed = string.Join(", ", allowedPrefixes.Select(p => $"{NormalisePrefix(p)}/…"));
            error = $"The destination must be inside one of: {allowed}.";
            return false;
        }

        destination = new DeploymentDestination(canonical);
        error = null;
        return true;
    }

    /// <summary>
    /// Joins a repository file path below this destination (FR-006). The file path gets the same
    /// segment rules, is not percent-decoded (it is a literal name from the repository listing),
    /// and may not name <c>web.config</c> or <c>app_offline.htm</c> at any depth.
    /// </summary>
    public bool TryCombine(string repositoryRelativePath, [NotNullWhen(true)] out string? combined, [NotNullWhen(false)] out string? error)
    {
        combined = null;
        if (string.IsNullOrEmpty(repositoryRelativePath))
        {
            error = "The repository contains a file with an empty path.";
            return false;
        }

        if (IsReservedFileName(repositoryRelativePath))
        {
            error = $"The repository contains '{repositoryRelativePath}', which would replace a file that controls the web application.";
            return false;
        }

        foreach (var segment in repositoryRelativePath.Split('/'))
        {
            if (segment.Length == 0)
            {
                error = $"The repository file path '{repositoryRelativePath}' has an empty segment.";
                return false;
            }

            if (!TryValidateSegment(segment, out var segmentError))
            {
                error = $"The repository file path '{repositoryRelativePath}' is unsafe: {segmentError}";
                return false;
            }
        }

        combined = $"{Value}/{repositoryRelativePath}";
        error = null;
        return true;
    }

    /// <summary>True when any segment is <c>web.config</c> or <c>app_offline.htm</c>, ignoring case. The job reports these as <see cref="CustomModelFailureKind.ReservedFileName"/>.</summary>
    public static bool IsReservedFileName(string repositoryRelativePath) =>
        repositoryRelativePath.Split('/', '\\').Any(ReservedFileNames.Contains);

    /// <summary>FR-015. Equal, or one contains the other on a segment boundary, ignoring case: <c>Models/a</c> overlaps <c>Models/a/b</c> but not <c>Models/ab</c>.</summary>
    public static bool Overlaps(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase)
        || left.StartsWith(right + "/", StringComparison.OrdinalIgnoreCase)
        || right.StartsWith(left + "/", StringComparison.OrdinalIgnoreCase);

    public override string ToString() => Value;

    private static bool TryValidateSegment(string segment, [NotNullWhen(false)] out string? error)
    {
        if (segment is "." or "..")
        {
            error = "'.' and '..' are not allowed in a path.";
            return false;
        }

        if (segment.Any(char.IsControl))
        {
            error = "Control characters are not allowed in a path.";
            return false;
        }

        if (segment.AsSpan().ContainsAny(ForbiddenCharacters))
        {
            error = "The characters < > : \" | ? * and \\ are not allowed in a path.";
            return false;
        }

        if (segment.AsSpan().ContainsAny(LookAlikeDots))
        {
            error = "Characters that look like '.' are not allowed in a path.";
            return false;
        }

        if (segment.EndsWith('.') || char.IsWhiteSpace(segment[^1]) || char.IsWhiteSpace(segment[0]))
        {
            error = "A path segment must not start or end with a space, or end with '.'.";
            return false;
        }

        var stem = segment.Split('.')[0];
        if (WindowsDeviceNames.Contains(stem))
        {
            error = $"'{segment}' is a reserved Windows device name.";
            return false;
        }

        error = null;
        return true;
    }

    private static bool IsStrictlyBelowAnAllowedPrefix(string canonical, IReadOnlyList<string> allowedPrefixes) =>
        allowedPrefixes
            .Select(NormalisePrefix)
            .Where(prefix => prefix.Length > 0)
            .Any(prefix => canonical.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase));

    private static string NormalisePrefix(string prefix) => prefix.Trim().Trim('/');
}

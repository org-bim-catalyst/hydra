namespace AskLucy.Domain.Localization;

/// <param name="Code">A BCP-47 language code.</param>
/// <param name="NativeName">The language's name in itself, as the switch shows it.</param>
/// <param name="IsRightToLeft">Whether it is written right to left.</param>
public sealed record PlatformLanguage(string Code, string NativeName, bool IsRightToLeft)
{
    public string Direction => IsRightToLeft ? "rtl" : "ltr";
}

/// <summary>
/// The languages the platform ships content for (FR-044a): an administrator can only choose among these. Adding a language means
/// adding its template seeds and message catalogs, then listing it here.
/// </summary>
public static class PlatformLanguages
{
    public const string English = "en";

    public const string Arabic = "ar";

    public static readonly IReadOnlyList<PlatformLanguage> All =
    [
        new(English, "English", IsRightToLeft: false),
        new(Arabic, "العربية", IsRightToLeft: true),
    ];

    public static bool IsPlatformLanguage(string? code) => Find(code) is not null;

    public static PlatformLanguage? Find(string? code) =>
        code is null ? null : All.FirstOrDefault(l => string.Equals(l.Code, code.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>The platform's spelling of a code (<c>AR</c> becomes <c>ar</c>), or null when it isn't a platform language.</summary>
    public static string? Normalize(string? code) => Find(code)?.Code;
}

namespace AskLucy.Infrastructure.Auth;

/// <summary>Bound from configuration ("Jwt" section) — never hardcoded (constitution &#167;4/&#167;8).</summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public required string Issuer { get; init; }

    public required string Audience { get; init; }

    public required string SigningKey { get; init; }

    public int AccessTokenLifetimeMinutes { get; init; } = 15;

    public int RefreshTokenLifetimeDays { get; init; } = 14;
}

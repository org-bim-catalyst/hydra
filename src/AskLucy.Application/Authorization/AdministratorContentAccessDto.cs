namespace AskLucy.Application.Authorization;

/// <summary>Whether the built-in Administrator role may view user content (specs/074 FR-016g) — the Roles page switch.</summary>
public sealed record AdministratorContentAccessDto(bool Granted);

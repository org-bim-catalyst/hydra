namespace AskLucy.Domain.Authorization;

/// <summary>
/// A single catalogue permission within an admin area. A reference type (not a struct) because
/// <see cref="Implies"/> refers to another <see cref="AdminPermission"/> — a struct containing
/// its own nullable type is a layout cycle the runtime rejects.
/// </summary>
#pragma warning disable CA1711 // "Permission" is this domain's own precise noun (research.md/data-model.md), not the "Flags enum" pattern CA1711 guards against.
public sealed record AdminPermission(
    string Key,
    AdminArea Area,
    AdminPermissionLevel Level,
    string DisplayName,
    string Description,
    AdminPermission? Implies);
#pragma warning restore CA1711

namespace AskLucy.Application.Panels;

/// <summary>data-model.md "Panel Request" / contracts/panel-hub-events.md — the payload pushed to
/// the browser via <see cref="AskLucy.Application.Abstractions.IPanelNotifier"/>. <c>Data</c> is
/// opaque here (validated client-side against the resolved panel type's schema, FR-017); this DTO
/// only carries it across the wire.</summary>
public sealed record PanelRequestDto(
    string RequestId,
    string TypeKey,
    string Title,
    object Data,
    PanelPositionDto? Position,
    PanelContextAssociationDto? ContextAssociation);

public sealed record PanelPositionDto(double X, double Y);

public sealed record PanelContextAssociationDto(string? LayerId, string? ElementId);

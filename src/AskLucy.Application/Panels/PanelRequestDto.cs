using System.Text.Json;

namespace AskLucy.Application.Panels;

/// <summary>data-model.md "Panel Request" / contracts/panel-request.md — the payload pushed to
/// the browser via <see cref="AskLucy.Application.Abstractions.IPanelNotifier"/>. Discriminated by
/// <see cref="Kind"/> (specs/049 research D5): a <c>"content"</c> request carries a block document
/// Lucy composed freely and already validated against the vocabulary (<see cref="Content"/>); a
/// <c>"live"</c> request names a registered panel kind by <see cref="TypeKey"/>, exactly as every
/// panel request did before this feature, with its own opaque <see cref="Data"/> validated
/// client-side against that kind's schema. The two data fields are mutually exclusive by
/// construction — each factory method below only ever populates the one that applies.</summary>
public sealed record PanelRequestDto(
    string RequestId,
    string Kind,
    string Title,
    object? Content,
    string? TypeKey,
    object? Data,
    PanelChromeDto? Chrome,
    PanelPositionDto? Position,
    PanelContextAssociationDto? ContextAssociation)
{
    public static PanelRequestDto ForContent(
        string requestId, string title, JsonElement content,
        PanelChromeDto? chrome = null, PanelPositionDto? position = null, PanelContextAssociationDto? contextAssociation = null) =>
        new(requestId, "content", title, content, null, null, chrome, position, contextAssociation);

    public static PanelRequestDto ForLive(
        string requestId, string title, string typeKey, JsonElement data,
        PanelChromeDto? chrome = null, PanelPositionDto? position = null, PanelContextAssociationDto? contextAssociation = null) =>
        new(requestId, "live", title, null, typeKey, data, chrome, position, contextAssociation);
}

public sealed record PanelChromeDto(bool? TitleBar, bool? Resizable, PanelSizeDto? DefaultSize);

public sealed record PanelSizeDto(double Width, double Height);

public sealed record PanelPositionDto(double X, double Y);

public sealed record PanelContextAssociationDto(string? LayerId, string? ElementId);

using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.Tools;
using AskLucy.Application.Documents.Commands;
using AskLucy.Application.MapCaptures;
using AskLucy.Application.MapCaptures.Queries.CaptureMap;
using AskLucy.Domain.Agents;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace AskLucy.Application.Conversations.Capabilities;

/// <summary>
/// Captures the current aerial imagery of a place and hands it back as an image in the
/// conversation (specs/083, renamed from "viewer map capture" to "map snapshot").
///
/// <para>
/// <b>A snapshot, not a map move.</b> This capability deliberately does <b>not</b> touch the
/// viewer: it returns a picture, it does not change where the map is looking. The two are
/// easy to confuse — "show me Al Safa Park 2" could mean either — so the wording in
/// <see cref="WhenToUse"/> names the picture explicitly, and the platform offers the choice
/// when a request is genuinely ambiguous.
/// </para>
///
/// <para>
/// <b>Reuses the existing capture pipeline</b> (specs/083) rather than rendering anything of
/// its own: the same geocoder, the same vendor router (Google primary, ESRI as the fallback
/// for satellite), the same crop, the same size cap and the same typed failure taxonomy. A
/// capability that rendered its own imagery would be a second, drifting implementation of a
/// thing the platform already has.
/// </para>
///
/// <para>
/// <b>The image is persisted as a platform Document</b> rather than streamed as a blob: a
/// conversation message has to survive a reload, and a URL that expires is not a message. The
/// document id is what the assistant's image message carries, exactly as a generated image
/// does — the client resolves it to a fresh signed URL on every render.
/// </para>
/// </summary>
public sealed class MapSnapshotCapability(
    ISender sender,
    IUserChatRepository userChatRepository,
    IServiceScopeFactory scopeFactory) : IConversationCapability
{
    public const string CapabilityKey = "map_snapshot";

    public string Name => CapabilityKey;

    public string Description =>
        "Captures the current satellite imagery of a place and returns it as an image in the chat.";

    public string WhenToUse =>
        "Use when the user asks to see a picture, screenshot, snapshot or aerial image of a place " +
        "— \"get me a screenshot for the current location\", \"give me a screenshot for Al Safa " +
        "Park 2\", \"show me what this site looks like from above\" — rather than asking to move " +
        "the map there.";

    public string ArgumentHint => "location: the place name, or the location already confirmed";

    public string UsageGuidance =>
        "This returns a picture; it does not move the map. If the user asked to see the place on " +
        "the map instead, that is a different action — say so rather than running this. Report " +
        "the place by the name the geocoder returned, so they can see which place was matched, " +
        "and say plainly when no match was found rather than guessing a candidate. The imagery is " +
        "the provider's most recent capture of the area; it is not live and it does not carry a " +
        "capture date. When the capture fails, say what failed — the place was not found, the " +
        "imagery provider was unavailable, or the image was too large — without claiming a " +
        "picture was produced.";

    public string Label => "Capture a snapshot";

    public string OfferDescription => "Capture the current aerial view of a place as an image.";

    public string AcknowledgementTemplate => "OK, taking a snapshot.";

    public AgentToolRiskLevel RiskLevel => AgentToolRiskLevel.Low;

    public IReadOnlyList<AgentToolPermission> RequiredPermissions =>
        [AgentToolPermission.ExternalNetwork, AgentToolPermission.WriteFile];

    public string InputSchemaJson =>
        """{"type":"object","properties":{"location":{"type":"string","minLength":1},"latitude":{"type":"number"},"longitude":{"type":"number"},"mode":{"type":"string"},"viewSizeMetres":{"type":"number"},"includeFootprints":{"type":"boolean"},"source":{"type":"string","enum":["esri","google"]},"crop":{"type":"number"},"zoom":{"type":"number"}}}""";

    public string OutputSchemaJson =>
        """{"type":"object","properties":{"documentId":{"type":"string"},"locationName":{"type":"string"},"width":{"type":"number"},"height":{"type":"number"},"mode":{"type":"string"}}}""";

    public CapabilityDuration ExpectedDuration => CapabilityDuration.Noticeable;

    public SubAgentArea Area => SubAgentArea.Location;

    /// <summary>
    /// Always available: a snapshot can be taken of any place, confirmed or not. The place comes
    /// from the arguments, and the chat's own confirmed location is the fallback when the model
    /// named none.
    /// </summary>
    public bool IsAvailable(TurnContext context) => true;

    /// <summary>
    /// Never offered on its own: the user asks for a picture explicitly, and a card offering
    /// "show a map snapshot" after an unrelated answer is noise. The ambiguity case (a picture
    /// or a map move?) is handled by a hand-composed offer instead — see
    /// <see cref="Runtime.MapSnapshotOffer"/>.
    /// </summary>
    public bool IsOfferable(TurnContext context, TurnOutcome justCompleted) => false;

    public async Task<AgentToolResult> ExecuteAsync(
        AgentToolExecutionContext context, JsonDocument input, CancellationToken cancellationToken = default)
    {
        var root = input.RootElement;

        var location = root.TryGetProperty("location", out var locationElement)
            ? locationElement.GetString()
            : null;
        double? latitude = root.TryGetProperty("latitude", out var latitudeElement)
            ? latitudeElement.GetDouble()
            : null;
        double? longitude = root.TryGetProperty("longitude", out var longitudeElement)
            ? longitudeElement.GetDouble()
            : null;

        // "a screenshot for the current location" names no place — the chat's own confirmed
        // location is the place. Standalone dispatch (a later turn, with no same-turn binding)
        // has nowhere else to find it: the Web layer persists the confirmation the moment it
        // happens, so the repository still has it.
        if (string.IsNullOrWhiteSpace(location) && (latitude is null || longitude is null))
        {
            var chat = context.UserChatId is { } chatId
                ? await userChatRepository.GetByIdAsync(chatId, cancellationToken)
                : null;

            if (chat?.ActiveLocation is not { } activeLocation)
            {
                return AgentToolResult.Failure(
                    "A place name, or a confirmed location for this chat, is required to take a snapshot.");
            }

            location = activeLocation.LocationName;
            latitude = activeLocation.Latitude;
            longitude = activeLocation.Longitude;
        }

        // The wire values are validated here, not downstream: CaptureMapQuery's handler throws
        // ArgumentException on an unparsable mode, and a throw inside a capability is a failed
        // turn rather than a reported reason.
        var requestedMode = root.TryGetProperty("mode", out var modeElement) ? modeElement.GetString() : null;
        if (!MapCaptureWireFormat.TryParseMode(requestedMode, out var mode))
        {
            return AgentToolResult.Failure(
                $"'{requestedMode}' is not a map mode. Use satellite, hybrid, roadmap or buildings-only.");
        }

        var includeFootprints = root.TryGetProperty("includeFootprints", out var footprintsElement) &&
                                footprintsElement.ValueKind == JsonValueKind.True;
        var overlays = includeFootprints ? "building-footprints" : null;
        if (overlays is not null && !MapCaptureWireFormat.TryParseOverlays(overlays, out _, out _))
        {
            return AgentToolResult.Failure("The requested overlay is not available.");
        }

        int? viewSizeMetres = null;
        if (root.TryGetProperty("viewSizeMetres", out var viewSizeElement) &&
            viewSizeElement.ValueKind == JsonValueKind.Number)
        {
            viewSizeMetres = viewSizeElement.GetInt32();
        }

        // The provider, the crop and the zoom are optional: when the model names none, the
        // defaults an administrator configured on the admin AI Tools page apply.
        string? source = null;
        if (root.TryGetProperty("source", out var sourceElement) && sourceElement.ValueKind == JsonValueKind.String)
        {
            var requested = sourceElement.GetString();
            if (!string.IsNullOrWhiteSpace(requested) && !MapCaptureSourceWire.TryParse(requested, out _))
            {
                return AgentToolResult.Failure(
                    $"'{requested}' is not an imagery provider. Use esri or google.");
            }

            source = requested;
        }

        int? crop = null;
        if (root.TryGetProperty("crop", out var cropElement) && cropElement.ValueKind == JsonValueKind.Number)
        {
            crop = cropElement.GetInt32();
        }

        int? zoom = null;
        if (root.TryGetProperty("zoom", out var zoomElement) && zoomElement.ValueKind == JsonValueKind.Number)
        {
            zoom = zoomElement.GetInt32();
        }

        MapImageResult capture;
        try
        {
            capture = await sender.Send(
                new CaptureMapQuery(
                    Location: location,
                    Latitude: latitude,
                    Longitude: longitude,
                    ViewSizeMetres: viewSizeMetres,
                    Mode: MapCaptureWireFormatToString(mode),
                    Overlays: overlays,
                    CallerName: $"conversation:{context.UserId}",
                    Source: source,
                    CropPixels: crop,
                    Zoom: zoom),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return AgentToolResult.Failure($"The snapshot failed: {ex.Message}");
        }

        if (!capture.IsSuccess || capture.Png is not { } png)
        {
            return AgentToolResult.Failure(DescribeFailure(capture));
        }

        var documentId = await PersistAsync(context.UserId, capture.ResolvedLocation ?? location ?? "map", png, cancellationToken);
        if (documentId is null)
        {
            return AgentToolResult.Failure("The snapshot was captured but could not be stored.");
        }

        return AgentToolResult.Success(JsonSerializer.SerializeToDocument(new
        {
            documentId = documentId.ToString(),
            locationName = capture.ResolvedLocation ?? location,
            width = capture.Width,
            height = capture.Height,
            mode = mode.ToString(),
        }));
    }

    /// <summary>
    /// Stores the PNG as a platform Document. Runs in its own scope: the capability may execute
    /// inside a turn that shares a scoped <c>DbContext</c> with the controller saving the
    /// assistant's own message, and a <c>DbContext</c> is not safe for concurrent use.
    /// </summary>
    private async Task<Guid?> PersistAsync(
        string ownerId, string locationName, byte[] png, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var finalizer = scope.ServiceProvider.GetRequiredService<DocumentUploadFinalizer>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        // The extension must be exactly ".png": the upload validator sniffs the magic bytes and
        // rejects a PNG whose name says otherwise.
        var fileName = $"map-snapshot-{Guid.CreateVersion7()}.png";
        using var content = new MemoryStream(png, writable: false);

        var result = await finalizer.FinalizeAsync(
            ownerId, fileName, content, content.Length, "system:map-snapshot", cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return result.IsDuplicate ? result.DuplicateOfDocumentId : result.Document?.Id;
    }

    /// <summary>The platform's own failure wording, so the narration reports what actually failed.</summary>
    private static string DescribeFailure(MapImageResult capture) => capture.FailureReason switch
    {
        MapCaptureFailureReason.LocationNotFound =>
            $"No place was found. {capture.FailureDetail}",
        MapCaptureFailureReason.GeocodingUnavailable =>
            "The place lookup is temporarily unavailable, so the snapshot could not be taken. Try again in a moment.",
        MapCaptureFailureReason.ModeUnavailable =>
            "That map view is not available from the imagery provider.",
        MapCaptureFailureReason.OverlayUnavailable =>
            "That overlay is not available on a snapshot.",
        MapCaptureFailureReason.ProviderUnavailable =>
            "The imagery provider is temporarily unavailable, so the snapshot could not be taken. Try again in a moment.",
        MapCaptureFailureReason.CropTooSmall =>
            "The captured image could not be prepared.",
        MapCaptureFailureReason.ImageTooLarge =>
            "The captured image is too large to store.",
        _ => capture.FailureDetail ?? "The snapshot could not be taken.",
    };

    /// <summary>
    /// The wire spelling of a mode. The handler parses the string rather than taking the enum,
    /// so the value crossing that boundary has to be the one the parser knows.
    /// </summary>
    private static string MapCaptureWireFormatToString(MapCaptureMode mode) => mode switch
    {
        MapCaptureMode.Roadmap => "roadmap",
        MapCaptureMode.Hybrid => "hybrid",
        MapCaptureMode.BuildingsOnly => "buildings-only",
        _ => "satellite",
    };
}

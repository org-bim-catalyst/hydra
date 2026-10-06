using System.Globalization;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Chats.Authorization;
using AskLucy.Application.Chats.Commands.AppendMessage;
using AskLucy.Application.Chats.Queries.GetChatById;
using AskLucy.Application.SiteBoundaries;
using AskLucy.Domain.Chats;
using AskLucy.Domain.Common;
using AskLucy.Domain.SiteBoundaries;
using MediatR;

namespace AskLucy.Application.Chats.Commands.SaveSiteBoundaryEdit;

/// <summary>
/// specs/079 - the one place a hand-edited outline is accepted. Checks, in order: the caller owns
/// the chat (a denial is audited), the revision the editor started from is still in force, the
/// shape is acceptable (each ring valid, still overlapping what Lucy found, not grown far beyond
/// it), then writes the user's correction, links the chat and appends the chat line - all in one
/// <c>SaveChanges</c> so the outline and the line recording it can never disagree.
/// </summary>
public sealed class SaveSiteBoundaryEditCommandHandler(
    IUserChatRepository chatRepository,
    ISiteBoundaryCorrectionRepository correctionRepository,
    IMessageRepository messageRepository,
    ISiteRingGeometry geometry,
    EffectiveSiteBoundary effectiveSiteBoundary,
    ChatOwnershipAuditor ownershipAuditor,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser) : IRequestHandler<SaveSiteBoundaryEditCommand, SaveSiteBoundaryEditResult>
{
    /// <summary>A ring may drift this far from what Lucy found and still count as the same site.</summary>
    private const double DriftToleranceMeters = 25;

    /// <summary>The union may be at most this many times what Lucy found.</summary>
    private const double MaxAreaGrowthFactor = 3;

    public async Task<SaveSiteBoundaryEditResult> Handle(SaveSiteBoundaryEditCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var chat = await ownershipAuditor.EnsureOwnedByAsync(
            await chatRepository.GetByIdAsync(request.ChatId, cancellationToken), userId, "save-site-boundary-edit", cancellationToken);

        var found = chat.ActiveBoundary ?? throw new KeyNotFoundException("Chat has no outlined site.");
        var effective = await effectiveSiteBoundary.ResolveAsync(chat, cancellationToken) ?? found;

        if (!Guid.TryParse(request.ExpectedRevision, out var expected) || expected != effective.Revision)
        {
            throw new ConcurrencyConflictException(
                "The outline changed since you started editing.", effective.Revision.ToString());
        }

        // The number of rings may change: the editor can add a circle as a ring of its own, merge rings
        // that now overlap, or split one by cutting across it. The validator bounds the count (1-20), and
        // every ring must still overlap what Lucy found, below.
        var rings = request.Rings.Select(Closed).ToList();
        var voids = request.Voids.Select(ringVoids => (IReadOnlyList<IReadOnlyList<GeoPoint>>)[.. ringVoids.Select(Closed)]).ToList();
        EnsureAcceptable(rings, voids, found);

        var area = geometry.UnionArea(rings, voids);
        var correction = effective.CorrectionId is { } correctionId
            ? await correctionRepository.GetByIdAsync(correctionId, userId, cancellationToken)
            : null;

        if (correction is null)
        {
            correction = SiteBoundaryCorrection.Create(
                userId, found.SiteName, found.CentroidLatitude, found.CentroidLongitude,
                SnapshotOf(found), rings, area, found.Members, userId, voids);
            correctionRepository.Add(correction);
            chat.LinkSiteBoundaryCorrection(correction.Id, userId);
        }
        else
        {
            correction.ReplaceRings(rings, area, userId, voids);
        }

        var content = string.Create(
            CultureInfo.InvariantCulture, $"You edited the outline of {found.SiteName} — now {area:N0} m².");
        var message = Message.Create(chat.Id, MessageRole.Assistant, MessageKind.Text, content, sourceText: null, userId);
        messageRepository.Add(message);
        chat.TouchLastActivity(userId);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        var inForce = found.WithCorrection(correction);
        return new SaveSiteBoundaryEditResult(ChatActiveBoundaryDto.FromEntity(inForce), AppendMessageCommandHandler.ToDto(message));
    }

    /// <summary>Validity per ring, then the drift rules against the outline Lucy found (research D11).</summary>
    private void EnsureAcceptable(
        List<IReadOnlyList<GeoPoint>> rings, List<IReadOnlyList<IReadOnlyList<GeoPoint>>> voids, ActiveSiteBoundary found)
    {
        for (var i = 0; i < rings.Count; i++)
        {
            var reason = geometry.Validate(rings[i]) switch
            {
                RingValidationResult.SelfCrossing => SiteBoundaryGeometryRejectedException.SelfCrossing,
                RingValidationResult.Degenerate => SiteBoundaryGeometryRejectedException.Degenerate,
                RingValidationResult.DuplicateCorner => SiteBoundaryGeometryRejectedException.DuplicateCorner,
                _ => null,
            };
            if (reason is not null)
            {
                throw new SiteBoundaryGeometryRejectedException(i, reason, MessageFor(i, reason));
            }
        }

        // specs/081: each void must be a valid ring lying inside its own ring and clear of the others.
        for (var i = 0; i < voids.Count; i++)
        {
            var verdict = geometry.ValidateVoids(rings[i], voids[i]);
            if (verdict.Result != RingValidationResult.Ok)
            {
                var voidReason = VoidReasonFor(verdict.Result);
                throw new SiteBoundaryGeometryRejectedException(
                    i, voidReason, VoidMessageFor(i, verdict.VoidIndex, voidReason), verdict.VoidIndex);
            }
        }

        IReadOnlyList<IReadOnlyList<GeoPoint>> foundRings = [found.Polygon, .. found.AdditionalPolygons];
        for (var i = 0; i < rings.Count; i++)
        {
            if (!geometry.Intersects([rings[i]], foundRings, DriftToleranceMeters))
            {
                throw new SiteBoundaryGeometryRejectedException(
                    i, SiteBoundaryGeometryRejectedException.DriftedAway, MessageFor(i, SiteBoundaryGeometryRejectedException.DriftedAway));
            }
        }

        if (geometry.UnionArea(rings, voids) > MaxAreaGrowthFactor * found.AreaSquareMeters)
        {
            throw new SiteBoundaryGeometryRejectedException(
                0, SiteBoundaryGeometryRejectedException.TooLarge, MessageFor(0, SiteBoundaryGeometryRejectedException.TooLarge));
        }
    }

    private static string VoidReasonFor(RingValidationResult result) => result switch
    {
        RingValidationResult.VoidOutsidePart => SiteBoundaryGeometryRejectedException.VoidOutsidePart,
        RingValidationResult.VoidsTouch => SiteBoundaryGeometryRejectedException.VoidsTouch,
        RingValidationResult.SelfCrossing => SiteBoundaryGeometryRejectedException.SelfCrossing,
        RingValidationResult.DuplicateCorner => SiteBoundaryGeometryRejectedException.DuplicateCorner,
        _ => SiteBoundaryGeometryRejectedException.Degenerate,
    };

    private static string VoidMessageFor(int ringIndex, int voidIndex, string reason) => reason switch
    {
        SiteBoundaryGeometryRejectedException.VoidOutsidePart =>
            $"Void {voidIndex + 1} of ring {ringIndex + 1} must lie inside the ring, clear of its edge.",
        SiteBoundaryGeometryRejectedException.VoidsTouch =>
            $"Void {voidIndex + 1} of ring {ringIndex + 1} touches another void.",
        SiteBoundaryGeometryRejectedException.SelfCrossing => $"Void {voidIndex + 1} of ring {ringIndex + 1} crosses itself.",
        SiteBoundaryGeometryRejectedException.DuplicateCorner => $"Void {voidIndex + 1} of ring {ringIndex + 1} has two corners in the same spot.",
        _ => $"Void {voidIndex + 1} of ring {ringIndex + 1} is too small or flat.",
    };

    private static string MessageFor(int ringIndex, string reason) => reason switch
    {
        SiteBoundaryGeometryRejectedException.SelfCrossing => $"Ring {ringIndex + 1} crosses itself.",
        SiteBoundaryGeometryRejectedException.Degenerate => $"Ring {ringIndex + 1} is too small or flat to be an outline.",
        SiteBoundaryGeometryRejectedException.DuplicateCorner => $"Ring {ringIndex + 1} has two corners in the same spot.",
        SiteBoundaryGeometryRejectedException.DriftedAway => $"Ring {ringIndex + 1} is too far from the site Lucy found.",
        _ => "The outline is far larger than the site Lucy found.",
    };

    /// <summary>The API carries closed rings, so a ring is stored closed whether or not the client repeated the first corner.</summary>
    private static IReadOnlyList<GeoPoint> Closed(IReadOnlyList<GeoPoint> ring) =>
        ring.Count > 1 && ring[0] == ring[^1] ? ring : [.. ring, ring[0]];

    private static FoundSiteBoundarySnapshot SnapshotOf(ActiveSiteBoundary found) => new(
        found.Polygon, found.AdditionalPolygons, found.CorePolygon, found.AreaSquareMeters, found.Confidence,
        found.ConfidenceLevel, found.Source, found.SourceDetail, found.Members);
}

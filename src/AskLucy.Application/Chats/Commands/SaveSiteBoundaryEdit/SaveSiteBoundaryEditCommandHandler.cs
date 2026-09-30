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

        // The client never adds or removes rings (spec Assumptions): a different count means it is
        // editing something other than the outline in force.
        var ringCount = 1 + effective.AdditionalPolygons.Count;
        if (request.Rings.Count != ringCount)
        {
            throw new DomainRuleViolationException(
                $"The outline has {ringCount} ring(s), but {request.Rings.Count} were sent.");
        }

        var rings = request.Rings.Select(Closed).ToList();
        EnsureAcceptable(rings, found);

        var area = geometry.UnionArea(rings);
        var correction = effective.CorrectionId is { } correctionId
            ? await correctionRepository.GetByIdAsync(correctionId, userId, cancellationToken)
            : null;

        if (correction is null)
        {
            correction = SiteBoundaryCorrection.Create(
                userId, found.SiteName, found.CentroidLatitude, found.CentroidLongitude,
                SnapshotOf(found), rings, area, found.Members, userId);
            correctionRepository.Add(correction);
            chat.LinkSiteBoundaryCorrection(correction.Id, userId);
        }
        else
        {
            correction.ReplaceRings(rings, area, userId);
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
    private void EnsureAcceptable(List<IReadOnlyList<GeoPoint>> rings, ActiveSiteBoundary found)
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

        IReadOnlyList<IReadOnlyList<GeoPoint>> foundRings = [found.Polygon, .. found.AdditionalPolygons];
        for (var i = 0; i < rings.Count; i++)
        {
            if (!geometry.Intersects([rings[i]], foundRings, DriftToleranceMeters))
            {
                throw new SiteBoundaryGeometryRejectedException(
                    i, SiteBoundaryGeometryRejectedException.DriftedAway, MessageFor(i, SiteBoundaryGeometryRejectedException.DriftedAway));
            }
        }

        if (geometry.UnionArea(rings) > MaxAreaGrowthFactor * found.AreaSquareMeters)
        {
            throw new SiteBoundaryGeometryRejectedException(
                0, SiteBoundaryGeometryRejectedException.TooLarge, MessageFor(0, SiteBoundaryGeometryRejectedException.TooLarge));
        }
    }

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

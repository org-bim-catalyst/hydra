using AskLucy.Application.Abstractions;
using AskLucy.Application.Chats.Authorization;
using AskLucy.Application.Chats.Commands.SaveSiteBoundaryEdit;
using AskLucy.Application.SiteBoundaries;
using AskLucy.Domain.SiteBoundaries;
using MediatR;

namespace AskLucy.Application.Chats.Commands.CombineSiteBoundaryShape;

/// <summary>
/// specs/079 - does the geometry of adding a circle to the outline or cutting one out. Only the chat's
/// owner may ask (a denial is audited), and it needs a chat that has an outline. The answer is checked
/// before it is returned, so the editor is never handed rings that could not be saved: a cut from the
/// middle would leave a hole, which an outline cannot have, and a cut that takes everything leaves no outline.
/// </summary>
public sealed class CombineSiteBoundaryShapeCommandHandler(
    IUserChatRepository chatRepository,
    ISiteRingGeometry geometry,
    ChatOwnershipAuditor ownershipAuditor,
    ICurrentUserAccessor currentUser) : IRequestHandler<CombineSiteBoundaryShapeCommand, CombineSiteBoundaryShapeResult>
{
    /// <summary>Corners in the circle drawn for the shape.</summary>
    private const int CircleSegments = 72;

    public async Task<CombineSiteBoundaryShapeResult> Handle(CombineSiteBoundaryShapeCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var chat = await ownershipAuditor.EnsureOwnedByAsync(
            await chatRepository.GetByIdAsync(request.ChatId, cancellationToken), userId, "combine-site-boundary-shape", cancellationToken);
        _ = chat.ActiveBoundary ?? throw new KeyNotFoundException("Chat has no outlined site.");

        for (var i = 0; i < request.Rings.Count; i++)
        {
            var reason = geometry.Validate(request.Rings[i]) switch
            {
                RingValidationResult.SelfCrossing => SiteBoundaryGeometryRejectedException.SelfCrossing,
                RingValidationResult.Degenerate => SiteBoundaryGeometryRejectedException.Degenerate,
                RingValidationResult.DuplicateCorner => SiteBoundaryGeometryRejectedException.DuplicateCorner,
                _ => null,
            };
            if (reason is not null)
            {
                throw new SiteBoundaryGeometryRejectedException(i, reason, $"Ring {i + 1} is not a valid outline ({reason}).");
            }
        }

        var circle = GeometryMath.CirclePolygon(request.Centre, request.RadiusMeters, CircleSegments);
        var combined = geometry.Combine(request.Rings, circle, request.Operation);

        switch (combined.Failure)
        {
            case CombineFailure.HoleNotSupported:
                throw new SiteBoundaryGeometryRejectedException(
                    0, SiteBoundaryGeometryRejectedException.HoleNotSupported,
                    "That cut would leave a hole in the middle of the outline, which an outline can't have. Start the circle at the edge so it cuts a bite out instead.");
            case CombineFailure.NothingLeft:
                throw new SiteBoundaryGeometryRejectedException(
                    0, SiteBoundaryGeometryRejectedException.NothingLeft,
                    "That would take away the whole outline. Use a smaller circle.");
        }

        if (combined.Rings.Count > SaveSiteBoundaryEditCommandValidator.MaxRings)
        {
            throw new SiteBoundaryGeometryRejectedException(
                0, SiteBoundaryGeometryRejectedException.TooManyRings,
                $"That would make {combined.Rings.Count} separate rings; an outline can have at most {SaveSiteBoundaryEditCommandValidator.MaxRings}.");
        }

        return new CombineSiteBoundaryShapeResult(combined.Rings);
    }
}

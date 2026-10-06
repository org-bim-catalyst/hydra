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

        // specs/081: each void is a valid ring lying inside its own ring and clear of the others.
        for (var i = 0; i < request.Voids.Count; i++)
        {
            var verdict = geometry.ValidateVoids(request.Rings[i], request.Voids[i]);
            if (verdict.Result != RingValidationResult.Ok)
            {
                var reason = verdict.Result switch
                {
                    RingValidationResult.VoidOutsidePart => SiteBoundaryGeometryRejectedException.VoidOutsidePart,
                    RingValidationResult.VoidsTouch => SiteBoundaryGeometryRejectedException.VoidsTouch,
                    RingValidationResult.SelfCrossing => SiteBoundaryGeometryRejectedException.SelfCrossing,
                    RingValidationResult.DuplicateCorner => SiteBoundaryGeometryRejectedException.DuplicateCorner,
                    _ => SiteBoundaryGeometryRejectedException.Degenerate,
                };
                throw new SiteBoundaryGeometryRejectedException(
                    i, reason, $"Void {verdict.VoidIndex + 1} of ring {i + 1} is not valid ({reason}).", verdict.VoidIndex);
            }
        }

        // A drawn polygon is used as it is; the circle is built round its centre.
        var shape = request.Shape ?? GeometryMath.CirclePolygon(request.Centre!, request.RadiusMeters, CircleSegments);
        if (request.Shape is not null)
        {
            var shapeReason = geometry.Validate(shape) switch
            {
                RingValidationResult.SelfCrossing => SiteBoundaryGeometryRejectedException.SelfCrossing,
                RingValidationResult.Degenerate => SiteBoundaryGeometryRejectedException.Degenerate,
                RingValidationResult.DuplicateCorner => SiteBoundaryGeometryRejectedException.DuplicateCorner,
                _ => null,
            };
            if (shapeReason is not null)
            {
                throw new SiteBoundaryGeometryRejectedException(0, shapeReason, $"The shape you drew is not valid ({shapeReason}).");
            }
        }

        var combined = geometry.Combine(request.Rings, request.Voids, shape, request.Operation);

        switch (combined.Failure)
        {
            case CombineFailure.HoleNotSupported:
                throw new SiteBoundaryGeometryRejectedException(
                    0, SiteBoundaryGeometryRejectedException.HoleNotSupported,
                    "That cut would leave a hole in the middle of the outline.");
            case CombineFailure.NothingLeft:
                throw new SiteBoundaryGeometryRejectedException(
                    0, SiteBoundaryGeometryRejectedException.NothingLeft,
                    "That would take away the whole outline. Use a smaller shape.");
            case CombineFailure.NothingChanged:
                throw new SiteBoundaryGeometryRejectedException(
                    0, SiteBoundaryGeometryRejectedException.NothingChanged,
                    "That area is already outside the site.");
        }

        if (combined.Rings.Count > SaveSiteBoundaryEditCommandValidator.MaxRings)
        {
            throw new SiteBoundaryGeometryRejectedException(
                0, SiteBoundaryGeometryRejectedException.TooManyRings,
                $"That would make {combined.Rings.Count} separate rings; an outline can have at most {SaveSiteBoundaryEditCommandValidator.MaxRings}.");
        }

        return new CombineSiteBoundaryShapeResult(combined.Rings) { Voids = combined.Voids };
    }
}

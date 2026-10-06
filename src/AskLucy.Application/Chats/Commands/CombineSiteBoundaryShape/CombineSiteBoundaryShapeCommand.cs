using AskLucy.Application.SiteBoundaries;
using AskLucy.Domain.SiteBoundaries;
using MediatR;

namespace AskLucy.Application.Chats.Commands.CombineSiteBoundaryShape;

/// <summary>
/// specs/079 - add a circle to the outline being edited, or cut one out of it. A pure calculation: nothing
/// is saved. The editor sends the rings as they stand (unsaved changes included) and gets the rings that
/// result, which it shows and later saves with the ordinary Done.
/// </summary>
public sealed record CombineSiteBoundaryShapeCommand(
    Guid ChatId,
    IReadOnlyList<IReadOnlyList<GeoPoint>> Rings,
    CombineOperation Operation,
    GeoPoint? Centre,
    double RadiusMeters) : IRequest<CombineSiteBoundaryShapeResult>
{
    /// <summary>specs/081 - each ring's voids by ring index (<c>Voids[i]</c> belong to <c>Rings[i]</c>); empty for none.</summary>
    public IReadOnlyList<IReadOnlyList<IReadOnlyList<GeoPoint>>> Voids { get; init; } = [];

    /// <summary>
    /// specs/081 - a drawn polygon (rectangle, square or free shape) to add or cut, instead of the circle given by
    /// <see cref="Centre"/> and <see cref="RadiusMeters"/>. Exactly one of the two is sent.
    /// </summary>
    public IReadOnlyList<GeoPoint>? Shape { get; init; }
}

/// <summary>The outline's rings after the shape was added or cut; open rings, the ring holding the site first.</summary>
public sealed record CombineSiteBoundaryShapeResult(IReadOnlyList<IReadOnlyList<GeoPoint>> Rings)
{
    /// <summary>specs/081 - each ring's voids by ring index; open rings; empty for none.</summary>
    public IReadOnlyList<IReadOnlyList<IReadOnlyList<GeoPoint>>> Voids { get; init; } = [];
}

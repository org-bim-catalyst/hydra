using AskLucy.Application.Ai.Commands.SendChatMessage;

namespace AskLucy.Application.SiteBoundaries;

/// <summary>
/// specs/042-site-boundary-resolution — resolves a site's boundary polygon around an
/// already-confirmed location (<see cref="ConfirmedLocationData"/>, from
/// <c>ILocationResolutionService</c>). Never throws — every failure path maps to a typed
/// <see cref="BoundaryResolutionOutcome"/> (constitution §VIII).
/// </summary>
public interface IBoundaryResolutionService
{
    Task<BoundaryResolutionOutcome> ResolveAsync(
        ConfirmedLocationData confirmedLocation, Guid userChatId, CancellationToken cancellationToken = default);
}

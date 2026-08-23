using AskLucy.Application.Ai.Commands.SendChatMessage;

namespace AskLucy.Application.Locations;

/// <summary>
/// specs/037-location-query-resolution data-model.md — the full result of one
/// <see cref="ILocationResolutionService.ResolveAsync"/> call: the outcome type, an optional
/// <see cref="ConfirmedLocationData"/> (non-null only for <see cref="LocationResolutionOutcomeType.Confirmed"/>),
/// and an optional deterministic confirmation/explanation sentence for every non-NoIntent outcome.
/// </summary>
public enum LocationResolutionOutcomeType
{
    NoIntent,
    Confirmed,
    Ambiguous,
    NotFound,
    Unavailable,
}

public sealed record LocationResolutionOutcome(
    LocationResolutionOutcomeType Type,
    ConfirmedLocationData? ConfirmedLocation,
    string? ConfirmationText);

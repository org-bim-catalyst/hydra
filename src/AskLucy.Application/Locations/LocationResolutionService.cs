using System.Text.Json;
using System.Text.Json.Serialization;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai;
using AskLucy.Application.Ai.Commands.SendChatMessage;
using AskLucy.Domain.Ai;
using AskLucy.Domain.Chats;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AskLucy.Application.Locations;

internal static partial class LocationResolutionServiceLog
{
    [LoggerMessage(Level = LogLevel.Information,
        Message = "Location resolution for chat {UserChatId}: {OutcomeType} (query: {Query})")]
    public static partial void Resolved(ILogger logger, Guid userChatId, LocationResolutionOutcomeType outcomeType, string? query);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Location resolution for chat {UserChatId}: Confirmed {LocationName} ({Latitude}, {Longitude}) confidence={Confidence} source=nominatim")]
    public static partial void Confirmed(ILogger logger, Guid userChatId, string locationName, double latitude, double longitude, double confidence);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Location intent classification failed for chat {UserChatId}")]
    public static partial void ClassificationFailed(ILogger logger, Guid userChatId, Exception exception);

    /// <summary>
    /// Geocoding failing is NOT classification failing, but both used to log the message
    /// above — so a console line saying "intent classification failed" could equally mean
    /// the classifier threw or that Google returned REQUEST_DENIED, two causes with
    /// completely different fixes. Split out so the log names which half broke.
    /// </summary>
    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Geocoding failed for chat {UserChatId} (query: {Query}); intent classification had already succeeded")]
    public static partial void GeocodingFailed(ILogger logger, Guid userChatId, string query, Exception exception);

    /// <summary>
    /// The classifier answering with something that is not bare JSON (a markdown fence, a
    /// preamble) is a routine model behaviour and a completely different problem from the
    /// provider being unreachable, yet both arrive as an exception. Logging a bounded
    /// prefix of what actually came back is the only way to tell them apart.
    /// </summary>
    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Location intent classifier returned unparseable content for chat {UserChatId} using {ProviderKey}/{ModelKey}: {ContentPrefix}")]
    public static partial void ClassifierContentUnparseable(
        ILogger logger, Guid userChatId, string providerKey, string modelKey, string contentPrefix, Exception exception);
}

/// <summary>
/// specs/037-location-query-resolution — classifies intent, geocodes when necessary, and
/// returns a <see cref="LocationResolutionOutcome"/> that
/// <c>SendChatMessageCommandHandler</c> appends to the chat stream. Never throws into the
/// stream — every failure path maps to <see cref="LocationResolutionOutcomeType.Unavailable"/>
/// (constitution §2.VIII).
/// </summary>
public sealed class LocationResolutionService(
    AiCapabilityProviderResolver capabilityProviderResolver,
    IAIProviderRepository aiProviderRepository,
    IAIModelRepository aiModelRepository,
    IAIProviderResolver aiProviderResolver,
    IGeocodingProvider geocodingProvider,
    IOptions<LocationResolutionOptions> options,
    ILogger<LocationResolutionService> logger) : ILocationResolutionService
{
    private readonly LocationResolutionOptions _options = options.Value;

    /// <summary>
    /// v1 — versioned per constitution §9 ("prompt engineering… versioned artifacts… reviewed
    /// like code"). A future revision becomes LocationIntentClassificationPromptV2, never a
    /// silent replacement of this constant.
    /// </summary>
    private const string LocationIntentClassificationPromptV1 =
        "You are a location-intent classifier for a 3-D map viewer. Analyse the user message " +
        "and return ONLY a single JSON object — no markdown, no commentary — using exactly this " +
        "schema: {\"intent\":\"none\"|\"new_query\"|\"back_reference\",\"placeQueries\":[...]}.\n\n" +
        "Rules:\n" +
        "- intent=\"none\": the user merely mentions a place name in passing (comparison, historical " +
        "recollection, analysis — e.g. \"I read that Al Safa Park was renovated\" or \"compare the " +
        "parking ratio to Zabeel Park\"). placeQueries must be [].\n" +
        "- intent=\"new_query\": the user explicitly wants to view, navigate to, or be shown a real-" +
        "world place (\"show me X\", \"where is X\", \"take me to X\", \"center on X\", \"let's look " +
        "at X\"). placeQueries contains the place name(s) exactly as written. When two or more " +
        "distinct places are named with no single navigational target, list ALL of them — the caller " +
        "will treat that as ambiguous.\n" +
        "- intent=\"back_reference\": the user refers to an already-established location without " +
        "re-stating its name (\"zoom in on it\", \"center on that place\", \"go there\"). " +
        "placeQueries must be [].\n\n" +
        "Never guess at intent — when the message does not unambiguously request navigation, return " +
        "intent=\"none\".";

    public async Task<LocationResolutionOutcome> ResolveAsync(
        string? userId,
        Guid userChatId,
        string latestUserMessage,
        ActiveSiteLocation? activeLocation,
        CancellationToken cancellationToken = default)
    {
        LocationIntentPayload? payload;
        try
        {
            var resolved = await capabilityProviderResolver.ResolveAsync(AiCapability.LocationIntent, cancellationToken);
            var provider = await aiProviderRepository.GetByIdAsync(resolved.ProviderId, cancellationToken)
                ?? throw new KeyNotFoundException("Default AI provider not found.");
            var model = await aiModelRepository.GetByIdAsync(resolved.ModelId, cancellationToken)
                ?? throw new KeyNotFoundException("Default AI model not found.");
            var aiProvider = aiProviderResolver.Resolve(provider.ProviderKey);

            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, LocationIntentClassificationPromptV1),
                new(ChatRole.User, $"User message:\n{latestUserMessage}"),
            };

            var completion = await aiProvider.ChatAsync(messages, model.ModelKey, parameters: null, cancellationToken);

            // Deserialize separately from the provider call so a model that answers with a
            // markdown fence or a preamble is reported as exactly that, naming the provider
            // and model that did it. Note this is the PLATFORM default pair (resolved with
            // preference: null above), not whatever the user picked in Settings — so this can
            // fail on a provider the user is not consciously using, while their chat replies
            // keep working normally.
            try
            {
                payload = JsonSerializer.Deserialize<LocationIntentPayload>(completion.Content);
            }
            catch (JsonException ex)
            {
                LocationResolutionServiceLog.ClassifierContentUnparseable(
                    logger, userChatId, provider.ProviderKey, model.ModelKey, Truncate(completion.Content), ex);
                return Unavailable();
            }

            if (payload is null || payload.Intent is not ("none" or "new_query" or "back_reference"))
            {
                LocationResolutionServiceLog.ClassificationFailed(logger, userChatId,
                    new InvalidOperationException($"Unrecognized intent value: '{payload?.Intent}'."));
                return Unavailable();
            }
        }
        catch (JsonException ex)
        {
            LocationResolutionServiceLog.ClassificationFailed(logger, userChatId, ex);
            return Unavailable();
        }
        catch (Exception ex)
        {
            LocationResolutionServiceLog.ClassificationFailed(logger, userChatId, ex);
            return Unavailable();
        }

        return payload.Intent switch
        {
            "none" => new LocationResolutionOutcome(LocationResolutionOutcomeType.NoIntent, null, null),
            "back_reference" => ResolveBackReference(userChatId, activeLocation),
            _ => await ResolveNewQueryAsync(userChatId, payload, cancellationToken),
        };
    }

    private LocationResolutionOutcome ResolveBackReference(Guid userChatId, ActiveSiteLocation? activeLocation)
    {
        if (activeLocation is null)
        {
            LocationResolutionServiceLog.Resolved(logger, userChatId, LocationResolutionOutcomeType.Unavailable, "back_reference(no-active)");
            return new LocationResolutionOutcome(LocationResolutionOutcomeType.Unavailable, null,
                LocationConfirmationTemplates.BackReferenceNoActive);
        }

        var data = new ConfirmedLocationData(
            activeLocation.Latitude, activeLocation.Longitude,
            activeLocation.LocationName, activeLocation.Confidence);
        LocationResolutionServiceLog.Confirmed(logger, userChatId,
            activeLocation.LocationName, activeLocation.Latitude, activeLocation.Longitude, activeLocation.Confidence);
        return new LocationResolutionOutcome(LocationResolutionOutcomeType.Confirmed, data,
            LocationConfirmationTemplates.Confirmed(activeLocation.LocationName));
    }

    private async Task<LocationResolutionOutcome> ResolveNewQueryAsync(
        Guid userChatId, LocationIntentPayload payload, CancellationToken cancellationToken)
    {
        if (payload.PlaceQueries.Count >= 2)
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                var joined = string.Join(", ", payload.PlaceQueries);
                LocationResolutionServiceLog.Resolved(logger, userChatId, LocationResolutionOutcomeType.Ambiguous, joined);
            }
            return new LocationResolutionOutcome(LocationResolutionOutcomeType.Ambiguous, null,
                LocationConfirmationTemplates.Ambiguous);
        }

        if (payload.PlaceQueries.Count == 0)
        {
            LocationResolutionServiceLog.Resolved(logger, userChatId, LocationResolutionOutcomeType.NotFound, null);
            return new LocationResolutionOutcome(LocationResolutionOutcomeType.NotFound, null,
                LocationConfirmationTemplates.NotFound);
        }

        var query = payload.PlaceQueries[0];
        IReadOnlyList<GeocodingCandidate> candidates;
        try
        {
            candidates = await geocodingProvider.SearchAsync(query, cancellationToken);
        }
        catch (GeocodingProviderUnavailableException ex)
        {
            LocationResolutionServiceLog.GeocodingFailed(logger, userChatId, query, ex);
            return Unavailable();
        }

        var filtered = candidates
            .Where(c => c.Importance >= _options.MinimumImportanceFloor)
            .OrderByDescending(c => c.Importance)
            .ToList();

        if (filtered.Count == 0)
        {
            LocationResolutionServiceLog.Resolved(logger, userChatId, LocationResolutionOutcomeType.NotFound, query);
            return new LocationResolutionOutcome(LocationResolutionOutcomeType.NotFound, null,
                LocationConfirmationTemplates.NotFound);
        }

        GeocodingCandidate winner;
        if (filtered.Count == 1)
        {
            winner = filtered[0];
        }
        else if (filtered[0].Importance - filtered[1].Importance >= _options.CandidateDominanceMargin)
        {
            winner = filtered[0];
        }
        else
        {
            LocationResolutionServiceLog.Resolved(logger, userChatId, LocationResolutionOutcomeType.Ambiguous, query);
            return new LocationResolutionOutcome(LocationResolutionOutcomeType.Ambiguous, null,
                LocationConfirmationTemplates.Ambiguous);
        }

        // WGS-84 range validation (FR-007)
        if (winner.Latitude is < -90 or > 90 || winner.Longitude is < -180 or > 180)
        {
            LocationResolutionServiceLog.Resolved(logger, userChatId, LocationResolutionOutcomeType.NotFound, query);
            return new LocationResolutionOutcome(LocationResolutionOutcomeType.NotFound, null,
                LocationConfirmationTemplates.NotFound);
        }

        var data = new ConfirmedLocationData(winner.Latitude, winner.Longitude, query, winner.Importance,
            LocationType: winner.LocationType, Viewport: winner.Viewport);
        LocationResolutionServiceLog.Confirmed(logger, userChatId,
            query, winner.Latitude, winner.Longitude, winner.Importance);
        return new LocationResolutionOutcome(LocationResolutionOutcomeType.Confirmed, data,
            LocationConfirmationTemplates.Confirmed(winner.LocationName));
    }

    private static LocationResolutionOutcome Unavailable() =>
        new(LocationResolutionOutcomeType.Unavailable, null, LocationConfirmationTemplates.Unavailable);

    /// <summary>Bounded so a runaway model reply cannot flood the log.</summary>
    private static string Truncate(string? content) =>
        string.IsNullOrEmpty(content)
            ? "<empty>"
            : content.Length <= 300 ? content : content[..300] + "…";

    private sealed record LocationIntentPayload(
        [property: JsonPropertyName("intent")] string Intent,
        [property: JsonPropertyName("placeQueries")] IReadOnlyList<string> PlaceQueries);
}

using System.Text.Json;
using AskLucy.Application.Agents.Tools;
using AskLucy.Application.Locations;
using AskLucy.Domain.Agents;

namespace AskLucy.Application.Conversations.Capabilities;

/// <summary>
/// Resolves a named place to confirmed coordinates (specs/045 FR-013).
///
/// <para>
/// Wraps the existing <see cref="ILocationResolutionService"/> rather than reaching for a
/// geocoder directly: that service owns the confidence model, the ambiguity rules and the
/// WGS-84 validation established in specs/035 and 037, all of which took real production
/// failures to get right. This capability contributes the conversational surface, nothing more.
/// </para>
///
/// <para>
/// <b>Available always, offerable never.</b> The two predicates diverge sharply here, and this is
/// the clearest example of why they had to be separated: finding a place can run at any moment,
/// but a user names the place they want — proposing "find a place" out of nowhere, after an
/// answer about setback regulations, is noise. It is reached by asking, or as step 1 of the
/// <c>locate_a_place</c> flow.
/// </para>
/// </summary>
public sealed class ResolveLocationCapability(ILocationResolutionService locationResolutionService) : IConversationCapability
{
    public const string CapabilityKey = "resolve_location";

    public string Name => CapabilityKey;

    public string Description =>
        "Resolves a named real-world place to confirmed coordinates and a canonical place name.";

    public string WhenToUse =>
        "Use when the user asks to see, find, locate or navigate to a named real-world place — " +
        "\"show me X\", \"where is X\", \"take me to X\", \"centre on X\" — or names a site, park, " +
        "building or address they want on the map.";

    public string ArgumentHint => "the place name, as the user wrote it";

    public string UsageGuidance =>
        "Report the resolved place by the name the geocoder returned, not the user's spelling, so " +
        "they can see which place was matched. When the outcome is ambiguous or not found, say so " +
        "plainly and do not guess a candidate — moving the viewer to the wrong site is worse than " +
        "not moving it. Never re-run this for a place already confirmed in this turn.";

    public string Label => "Find a place";

    public string OfferDescription => "Look up a named place and confirm where it is.";

    public string AcknowledgementTemplate => "OK, let me find it first.";

    public AgentToolRiskLevel RiskLevel => AgentToolRiskLevel.Low;

    public IReadOnlyList<AgentToolPermission> RequiredPermissions => [AgentToolPermission.ExternalNetwork];

    public string InputSchemaJson =>
        """{"type":"object","required":["query"],"properties":{"query":{"type":"string","minLength":1}}}""";

    public string OutputSchemaJson =>
        """{"type":"object","properties":{"outcome":{"type":"string"},"locationName":{"type":"string"},"latitude":{"type":"number"},"longitude":{"type":"number"},"confidence":{"type":"number"}}}""";

    public CapabilityDuration ExpectedDuration => CapabilityDuration.Noticeable;

    public bool IsAvailable(TurnContext context) => true;

    /// <summary>
    /// Never offered. See the type remarks — this is the canonical case for the
    /// available/offerable split (FR-025b).
    /// </summary>
    public bool IsOfferable(TurnContext context, TurnOutcome justCompleted) => false;

    public async Task<AgentToolResult> ExecuteAsync(
        AgentToolExecutionContext context, JsonDocument input, CancellationToken cancellationToken = default)
    {
        if (!input.RootElement.TryGetProperty("query", out var queryElement) ||
            queryElement.GetString() is not { Length: > 0 } query)
        {
            return AgentToolResult.Failure("A non-empty place name is required.");
        }

        try
        {
            var outcome = await locationResolutionService.ResolveAsync(
                context.UserId, context.UserChatId ?? Guid.Empty, query, activeLocation: null, cancellationToken);

            if (outcome.ConfirmedLocation is not { } location)
            {
                // Not an exception: ambiguous and not-found are ordinary, expected results with
                // their own user-facing wording. Returning them as failures would conflate "the
                // place does not exist" with "the lookup broke".
                return AgentToolResult.Failure(outcome.ConfirmationText ?? "The place could not be confidently resolved.");
            }

            return AgentToolResult.Success(JsonSerializer.SerializeToDocument(new
            {
                outcome = outcome.Type.ToString(),
                locationName = location.LocationName,
                latitude = location.Latitude,
                longitude = location.Longitude,
                confidence = location.Confidence,
            }));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // The service documents a never-throws contract, but the turn's integrity must not
            // depend on another type keeping its promise (constitution §2.VIII — isolation, not
            // suppression: the reason travels back to the caller and into the turn record).
            return AgentToolResult.Failure($"The place lookup failed: {ex.Message}");
        }
    }
}

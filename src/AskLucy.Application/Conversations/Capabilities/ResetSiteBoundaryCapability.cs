using System.Text.Json;
using System.Text.Json.Nodes;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.Tools;
using AskLucy.Application.Ai.Commands.SendChatMessage;
using AskLucy.Application.Chats.Commands.ResetSiteBoundary;
using AskLucy.Application.SiteBoundaries;
using AskLucy.Domain.Agents;
using AskLucy.Domain.Common;
using MediatR;

namespace AskLucy.Application.Conversations.Capabilities;

/// <summary>
/// specs/079 (US5) contracts/edit-and-reset-capabilities.md - puts back the outline Lucy found, in place of the
/// user's hand-edited one. The same command as the REST reset, so the ownership check, the revision check and
/// the chat line are the same. Returns the found outline in <see cref="SiteBoundaryPayload"/>'s shape, which
/// the existing <c>siteBoundary</c> event redraws with the animated border. Reached from the "Reset" row of
/// <see cref="Runtime.SiteBoundaryResetOffer"/>, or from the user asking to undo their edits.
/// </summary>
public sealed class ResetSiteBoundaryCapability(
    IUserChatRepository userChatRepository,
    EffectiveSiteBoundary effectiveSiteBoundary,
    ISender sender) : IConversationCapability
{
    public const string CapabilityKey = "reset_site_boundary";

    public string Name => CapabilityKey;

    public string Description => "Puts back the outline Lucy found, in place of the user's hand-edited one.";

    public string WhenToUse =>
        "Use when the user wants their hand-edited outline undone, reset or replaced with the one Lucy found.";

    public string ArgumentHint => "none";

    public string UsageGuidance =>
        "Say in one sentence that the outline is back to the one you found, and give its area. " +
        "When reset is false, say the outline was never edited, so there was nothing to put back.";

    public string Label => "Reset the outline";

    public string OfferDescription => "Discard your edits and show the outline I found.";

    public string AcknowledgementTemplate => "Putting back the outline I found.";

    public AgentToolRiskLevel RiskLevel => AgentToolRiskLevel.Low;

    public IReadOnlyList<AgentToolPermission> RequiredPermissions => [];

    public string InputSchemaJson => """{"type":"object","properties":{}}""";

    public string OutputSchemaJson =>
        """{"type":"object","properties":{"siteName":{"type":"string"},"areaSquareMeters":{"type":"number"},"reset":{"type":"boolean"},"resetFromHandEdit":{"type":"boolean"}}}""";

    public CapabilityDuration ExpectedDuration => CapabilityDuration.Brief;

    public SubAgentArea Area => SubAgentArea.Location;

    public bool IsAvailable(TurnContext context) => context.ActiveBoundary is { IsHandEdited: true };

    /// <summary>Never offered through the generic offer: <see cref="Runtime.SiteBoundaryResetOffer"/> builds its own row.</summary>
    public bool IsOfferable(TurnContext context, TurnOutcome justCompleted) => false;

    public async Task<AgentToolResult> ExecuteAsync(
        AgentToolExecutionContext context, JsonDocument input, CancellationToken cancellationToken = default)
    {
        if (context.UserChatId is not { } chatId)
        {
            return AgentToolResult.Failure("There's no outlined site to reset.");
        }

        var chat = await userChatRepository.GetByIdAsync(chatId, cancellationToken);
        var effective = await effectiveSiteBoundary.ResolveAsync(chat, cancellationToken);
        if (effective is null)
        {
            return AgentToolResult.Failure("There's no outlined site to reset.");
        }

        if (!effective.IsHandEdited)
        {
            return AgentToolResult.Success(JsonSerializer.SerializeToDocument(new
            {
                note = "This outline was never edited, so there was nothing to put back.",
                reset = false,
                siteName = effective.SiteName,
                areaSquareMeters = effective.AreaSquareMeters,
            }));
        }

        try
        {
            // Ownership is checked, and a denial audited, by the handler (constitution section 8).
            await sender.Send(new ResetSiteBoundaryCommand(chatId, effective.Revision.ToString()), cancellationToken);
        }
        catch (KeyNotFoundException)
        {
            return AgentToolResult.Failure("Only the chat's owner can reset its outline.");
        }
        catch (ConcurrencyConflictException)
        {
            return AgentToolResult.Failure("The outline changed while I was working. Ask again to reset the current one.");
        }

        var found = (await userChatRepository.GetByIdAsync(chatId, cancellationToken))?.ActiveBoundary
            ?? throw new InvalidOperationException("The chat lost its outline during the reset.");
        var confirmed = new ConfirmedSiteBoundaryData(
            found.SiteName, found.CentroidLatitude, found.CentroidLongitude, found.Polygon, found.AreaSquareMeters,
            found.Confidence, found.ConfidenceLevel, found.Source, found.SourceDetail, AlternativeCandidateNames: [])
        {
            CorePolygon = found.CorePolygon,
            AdditionalPolygons = found.AdditionalPolygons,
            Members = found.Members,
        };

        // A plain sentence first: the narrating model reads the first field before anything else.
        var output = new JsonObject
        {
            ["note"] = "The outline is back to the one Lucy found; the user's edits were discarded.",
            ["reset"] = true,
            ["resetFromHandEdit"] = true,
        };
        foreach (var (key, value) in JsonSerializer.SerializeToNode(SiteBoundaryPayload.Write(confirmed))!.AsObject().ToList())
        {
            output[key] = value?.DeepClone();
        }

        return AgentToolResult.Success(JsonSerializer.SerializeToDocument(output));
    }
}

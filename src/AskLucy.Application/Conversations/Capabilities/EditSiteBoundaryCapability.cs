using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.Tools;
using AskLucy.Application.Chats.Authorization;
using AskLucy.Application.SiteBoundaries;
using AskLucy.Domain.Agents;

namespace AskLucy.Application.Conversations.Capabilities;

/// <summary>
/// specs/079 contracts/edit-and-reset-capabilities.md - opens the map's outline editor for the
/// chat's site. Performs no geometry: the browser owns edit mode, and this only tells it which
/// revision to start from (research D7). Reached from the "Edit the outline" row of
/// <see cref="Runtime.SiteBoundaryEditOffer"/>, or from the user asking to adjust the outline.
/// </summary>
public sealed class EditSiteBoundaryCapability(
    IUserChatRepository userChatRepository,
    EffectiveSiteBoundary effectiveSiteBoundary,
    ChatOwnershipAuditor ownershipAuditor) : IConversationCapability
{
    public const string CapabilityKey = "edit_site_boundary";

    public string Name => CapabilityKey;

    public string Description => "Opens the map's outline editor so the user can move, add or delete the outline's corners.";

    public string WhenToUse =>
        "Use when the user wants to adjust, fix, correct or redraw the outlined site's shape by hand.";

    public string ArgumentHint => "none";

    public string UsageGuidance =>
        "Say in one or two sentences that the editor is open and how to use it, from howToEdit. Don't give the area.";

    public string Label => "Edit the outline";

    public string OfferDescription => "Move, add or delete the outline's corners on the map.";

    public string AcknowledgementTemplate => "Opening the outline editor.";

    public AgentToolRiskLevel RiskLevel => AgentToolRiskLevel.Low;

    public IReadOnlyList<AgentToolPermission> RequiredPermissions => [];

    public string InputSchemaJson => """{"type":"object","properties":{}}""";

    public string OutputSchemaJson =>
        """{"type":"object","properties":{"siteName":{"type":"string"},"areaSquareMeters":{"type":"number"},"isHandEdited":{"type":"boolean"},"chatId":{"type":"string"},"revision":{"type":"string"},"openEditor":{"type":"boolean"},"howToEdit":{"type":"string"}}}""";

    public CapabilityDuration ExpectedDuration => CapabilityDuration.Brief;

    public SubAgentArea Area => SubAgentArea.Location;

    public bool IsAvailable(TurnContext context) => context.ActiveBoundary is not null;

    /// <summary>Never offered through the generic offer: <see cref="Runtime.SiteBoundaryEditOffer"/> builds its own row.</summary>
    public bool IsOfferable(TurnContext context, TurnOutcome justCompleted) => false;

    public async Task<AgentToolResult> ExecuteAsync(
        AgentToolExecutionContext context, JsonDocument input, CancellationToken cancellationToken = default)
    {
        if (context.UserChatId is not { } chatId)
        {
            return AgentToolResult.Failure("There's no outlined site to edit.");
        }

        var chat = await userChatRepository.GetByIdAsync(chatId, cancellationToken);
        try
        {
            // A denial is audited (constitution section 8) and reported to the user, not swallowed.
            await ownershipAuditor.EnsureOwnedByAsync(chat, context.UserId, "edit-site-boundary", cancellationToken);
        }
        catch (KeyNotFoundException)
        {
            return AgentToolResult.Failure("Only the chat's owner can edit its outline.");
        }

        var outline = await effectiveSiteBoundary.ResolveAsync(chat, cancellationToken);
        if (outline is null)
        {
            return AgentToolResult.Failure("There's no outlined site to edit.");
        }

        return AgentToolResult.Success(JsonSerializer.SerializeToDocument(new
        {
            siteName = outline.SiteName,
            areaSquareMeters = outline.AreaSquareMeters,
            voidCount = outline.Voids.Sum(ringVoids => ringVoids.Count),
            isHandEdited = outline.IsHandEdited,
            chatId = chatId.ToString(),
            revision = outline.Revision.ToString(),
            openEditor = true,
            howToEdit = "Drag a corner to move it, drag an edge's middle handle to add one, right-click or long-press a corner to delete it. Press Done when finished.",
        }));
    }
}

using AskLucy.Application.Chats;
using AskLucy.Application.Chats.Commands.AppendMessage;
using AskLucy.Domain.Chats;
using MediatR;

namespace AskLucy.Application.SiteAnalysis.Routing;

/// <summary>
/// Wires <see cref="SiteAnalysisChatTurnRouter"/> into the existing chat pipeline
/// (contracts/chat-to-agent-routing.md) as a <c>MediatR</c> pipeline behavior scoped to
/// <see cref="AppendMessageCommand"/> only \u2014 not a change to <c>AppendMessageCommandHandler</c>
/// itself (constitution §3: cross-cutting behavior belongs in a pipeline behavior, not duplicated
/// per handler). Only reacts to user-authored messages; assistant messages (including the ones
/// this feature's own runtime posts) pass through untouched, so no recursive re-triggering occurs.
/// </summary>
public sealed class SiteAnalysisChatTurnBehavior(SiteAnalysisChatTurnRouter router)
    : IPipelineBehavior<AppendMessageCommand, MessageDto>
{
    public async Task<MessageDto> Handle(
        AppendMessageCommand request, RequestHandlerDelegate<MessageDto> next, CancellationToken cancellationToken)
    {
        var result = await next();

        if (request.Role == MessageRole.User)
        {
            await router.HandleUserMessageAsync(request.ChatId, request.Content, cancellationToken);
        }

        return result;
    }
}

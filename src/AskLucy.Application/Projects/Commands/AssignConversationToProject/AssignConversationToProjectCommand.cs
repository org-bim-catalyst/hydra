using MediatR;

namespace AskLucy.Application.Projects.Commands.AssignConversationToProject;

/// <summary>contracts/projects-api.md — `PUT /api/v1/chats/{chatId}/project` (spec.md FR-002a). Null <see cref="ProjectId"/> removes the conversation from its Project (back to general scope).</summary>
public sealed record AssignConversationToProjectCommand(Guid ChatId, Guid? ProjectId) : IRequest;

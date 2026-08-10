using MediatR;

namespace AskLucy.Application.Prompts.Commands.SetFavorite;

public sealed record SetFavoriteCommand(Guid PromptId, bool IsFavorite) : IRequest;

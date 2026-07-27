using MediatR;

namespace AskLucy.Application.Ai.Commands.GenerateImage;

public sealed record GenerateImageCommand(string Prompt) : IRequest<Uri>;

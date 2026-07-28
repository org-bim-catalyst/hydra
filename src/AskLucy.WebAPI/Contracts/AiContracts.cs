using AskLucy.Application.Ai;

namespace AskLucy.WebAPI.Contracts;

public sealed record ChatRequest(Guid ChatId, IReadOnlyList<ChatMessageDto> Messages);

public sealed record TranslateRequest(Guid ChatId, string Text, string TargetLanguage);

public sealed record GenerateImageRequest(Guid ChatId, string Prompt);

public sealed record GenerateImageResponse(string Url);

public sealed record TranscriptionResponse(string Text);

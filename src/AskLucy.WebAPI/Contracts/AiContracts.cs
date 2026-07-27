using AskLucy.Application.Ai;

namespace AskLucy.WebAPI.Contracts;

public sealed record ChatRequest(IReadOnlyList<ChatMessageDto> Messages);

public sealed record TranslateRequest(string Text, string TargetLanguage);

public sealed record GenerateImageRequest(string Prompt);

public sealed record GenerateImageResponse(string Url);

public sealed record TranscriptionResponse(string Text);

namespace AskLucy.Application.Chats;

public sealed record AttachmentDto(Guid Id, string FileName, string ContentType, string AccessLocation);

public sealed record CitationDto(Guid Id, string SourceLabel, string? SourceReference);

public sealed record MessageDto(
    Guid Id,
    string Role,
    string Kind,
    string Content,
    string? SourceText,
    DateTime CreatedAtUtc,
    string? Provider,
    string? Model,
    string? GenerationParametersJson,
    int? InputTokenCount,
    int? OutputTokenCount,
    IReadOnlyList<AttachmentDto> Attachments,
    IReadOnlyList<CitationDto> Citations);

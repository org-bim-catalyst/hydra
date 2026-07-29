namespace AskLucy.Application.Chats.Queries.ExportUserChat;

/// <summary>
/// The structured, portable export schema (FR-025, research.md Topic 7) — a JSON document
/// with the conversation's title/dates and its full ordered message history. Attachments/
/// citations are included by reference (filename, type, existing access location) per the
/// export clarification — never embedded file content.
/// </summary>
public sealed record ConversationExportDto(
    Guid Id,
    string Title,
    DateTime CreatedAtUtc,
    DateTime? ModifiedAtUtc,
    IReadOnlyList<ExportedMessageDto> Messages);

public sealed record ExportedMessageDto(
    string Role,
    string Kind,
    string Content,
    string? SourceText,
    DateTime CreatedAtUtc,
    string? Provider,
    string? Model,
    int? InputTokenCount,
    int? OutputTokenCount,
    IReadOnlyList<ExportedAttachmentDto> Attachments,
    IReadOnlyList<ExportedCitationDto> Citations);

public sealed record ExportedAttachmentDto(string FileName, string ContentType, string AccessLocation);

public sealed record ExportedCitationDto(string SourceLabel, string? SourceReference);

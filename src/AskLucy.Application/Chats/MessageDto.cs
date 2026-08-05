namespace AskLucy.Application.Chats;

public sealed record AttachmentDto(Guid Id, string FileName, string ContentType, string AccessLocation);

/// <summary>The trailing RAG-specific fields (research.md Decision 9) are null for a plain, non-RAG citation.</summary>
public sealed record CitationDto(
    Guid Id, string SourceLabel, string? SourceReference,
    Guid? DocumentChunkId = null, Guid? KnowledgeBaseId = null, Guid? DocumentId = null,
    Guid? DocumentVersionId = null, int? PageNumber = null, string? Section = null);

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
    // The four fields below were added for specs/005-multi-provider-ai-engine (FR-020/FR-021) —
    // see AppendMessageCommandHandler.ToDto for how they're populated.
    int? CachedTokenCount,
    int? ReasoningTokenCount,
    int? LatencyMs,
    decimal? EstimatedCostUsd,
    IReadOnlyList<AttachmentDto> Attachments,
    IReadOnlyList<CitationDto> Citations);

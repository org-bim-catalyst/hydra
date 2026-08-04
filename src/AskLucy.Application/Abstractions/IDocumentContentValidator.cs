namespace AskLucy.Application.Abstractions;

/// <summary>The document types this feature supports (spec.md Objective, CLAUDE.md RAG file types).</summary>
public enum KnowledgeBaseDocumentType
{
    Pdf,
    Word,
    Excel,
    PowerPoint,
    Markdown,
    Csv,
    Text,
}

public sealed record DocumentValidationResult(bool IsValid, KnowledgeBaseDocumentType? DetectedType, string? ResolvedContentType, string? FailureReason)
{
    public static DocumentValidationResult Valid(KnowledgeBaseDocumentType type, string contentType) => new(true, type, contentType, null);

    public static DocumentValidationResult Invalid(string reason) => new(false, null, null, reason);
}

/// <summary>
/// Validates an uploaded document by its actual byte content, not by file extension or the
/// client-supplied MIME header alone (constitution §8; specs/014-knowledge-base-management
/// research.md Decision 8).
/// </summary>
public interface IDocumentContentValidator
{
    Task<DocumentValidationResult> ValidateAsync(Stream content, string fileName, CancellationToken cancellationToken = default);
}

using AskLucy.Domain.Documents;

namespace AskLucy.Application.Abstractions;

public sealed record DocumentFileValidationResult(bool IsValid, DocumentFileType? DetectedType, string? ResolvedContentType, string? FailureReason)
{
    public static DocumentFileValidationResult Valid(DocumentFileType type, string contentType) => new(true, type, contentType, null);

    public static DocumentFileValidationResult Invalid(string reason) => new(false, null, null, reason);
}

/// <summary>
/// Validates an uploaded document by its actual byte content, never by file extension or the
/// client-supplied MIME header alone (FR-010, FR-049, constitution §8). Covers the full
/// <see cref="DocumentFileType"/> set — a separate abstraction from
/// <c>IDocumentContentValidator</c> (specs/014-knowledge-base-management), which is scoped to the
/// narrower RAG-ingestible <c>KnowledgeBaseDocumentType</c> set (research.md Decision 11).
/// </summary>
public interface IDocumentFileValidator
{
    Task<DocumentFileValidationResult> ValidateAsync(Stream content, string fileName, CancellationToken cancellationToken = default);
}

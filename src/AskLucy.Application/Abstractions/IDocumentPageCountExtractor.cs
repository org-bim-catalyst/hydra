namespace AskLucy.Application.Abstractions;

/// <summary>
/// Best-effort page-count extraction for PDF/Word/PowerPoint documents (specs/014-knowledge-
/// base-management research.md Decision 5). Returns <c>null</c> — never throws — when the
/// type has no meaningful page count (Excel/Markdown/CSV/Text, spec.md Assumptions) or when
/// extraction fails on a malformed/unexpected-structure file; a missing derived statistic is
/// not the outcome the caller (an upload) asked for, so the upload itself is never blocked
/// by an extraction failure.
/// </summary>
public interface IDocumentPageCountExtractor
{
    Task<int?> ExtractPageCountAsync(Stream content, KnowledgeBaseDocumentType documentType, CancellationToken cancellationToken = default);
}

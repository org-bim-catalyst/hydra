using System.Security.Cryptography;
using System.Text;
using AskLucy.Application.Abstractions;
using AskLucy.Domain.Documents;
using AskLucy.Domain.KnowledgeBases;
using AskLucy.Domain.Retrieval;

namespace AskLucy.Application.Retrieval.Indexing;

/// <summary>
/// Indexes one <see cref="KnowledgeBaseDocument"/> end to end (research.md Decision 2): creates
/// the underlying <see cref="Document"/>/<see cref="DocumentVersion"/> via the Document
/// Intelligence Pipeline's existing text extractors when no link exists yet, chunks, embeds in
/// batches (FR-050), and writes to the knowledge base's resolved <see cref="IVectorStore"/>
/// (ADR-0007 — <see cref="IVectorStoreResolver"/> picks the implementation per
/// <see cref="KnowledgeBase.VectorStoreProvider"/>), skipping unchanged content by content hash
/// (FR-005).
///
/// <para>Commits incrementally (chunks persisted, then each embedding batch persisted before its
/// vector-store write) rather than one single <see cref="IUnitOfWork.SaveChangesAsync"/> for the
/// whole run — a deliberate deviation from constitution §3's "one business transaction, one
/// SaveChanges" rule for *synchronous request/response* work, matching the same progressive-
/// persistence precedent specs/015's <c>DocumentProcessingPipeline</c> already established for
/// long-running background jobs (each stage's state is durable before the next stage begins, so a
/// crash mid-run resumes rather than redoing completed work).</para>
/// </summary>
public sealed class IndexingOrchestrator(
    IKnowledgeBaseDocumentRepository knowledgeBaseDocumentRepository,
    IKnowledgeBaseRepository knowledgeBaseRepository,
    IDocumentRepository documentRepository,
    IFileStorage fileStorage,
    IEnumerable<IDocumentTextExtractor> textExtractors,
    IDocumentChunkRepository documentChunkRepository,
    IChunkingService chunkingService,
    IEmbeddingProviderRepository embeddingProviderRepository,
    IEmbeddingServiceResolver embeddingServiceResolver,
    IEmbeddingRepository embeddingRepository,
    IVectorStoreResolver vectorStoreResolver,
    IUnitOfWork unitOfWork) : IIndexingOrchestrator
{
    private const string Actor = "system:rag-index";
    private const int EmbeddingBatchSize = 16;

    public async Task<IndexingOutcome> IndexKnowledgeBaseDocumentAsync(
        Guid knowledgeBaseDocumentId, bool forceFullReindex, CancellationToken cancellationToken = default)
    {
        var knowledgeBaseDocument = await knowledgeBaseDocumentRepository.GetByIdAsync(knowledgeBaseDocumentId, cancellationToken)
            ?? throw new KeyNotFoundException("Knowledge base document not found.");

        var knowledgeBase = await knowledgeBaseRepository.GetByIdAsync(knowledgeBaseDocument.KnowledgeBaseId, cancellationToken)
            ?? throw new KeyNotFoundException("Knowledge base not found.");

        var (document, version) = knowledgeBaseDocument.DocumentId is { } existingDocumentId
            ? await LoadExistingDocumentAsync(existingDocumentId, cancellationToken)
            : await CreateDocumentFromKnowledgeBaseDocumentAsync(knowledgeBaseDocument, knowledgeBase, cancellationToken);

        if (knowledgeBaseDocument.DocumentId is null)
        {
            knowledgeBaseDocument.LinkToDocument(document.Id, Actor);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(version.ExtractedText))
        {
            return IndexingOutcome.Failed;
        }

        var provider = await ResolveEmbeddingProviderAsync(knowledgeBase, cancellationToken);
        var embeddingService = embeddingServiceResolver.Resolve(provider.Vendor);
        var vectorStore = vectorStoreResolver.Resolve(knowledgeBase.VectorStoreProvider);

        var strategy = chunkingService.Resolve(knowledgeBase.ChunkingStrategy);
        var chunkCandidates = await strategy.ChunkAsync(version.ExtractedText, version.ExtractedStructureJson, language: null, cancellationToken);

        var chunksNeedingEmbedding = new List<DocumentChunk>();

        foreach (var candidate in chunkCandidates)
        {
            var contentHash = ComputeSha256(candidate.Content);
            var existingChunk = await documentChunkRepository.FindByContentHashAsync(knowledgeBaseDocument.Id, contentHash, cancellationToken);

            if (existingChunk is not null && !forceFullReindex)
            {
                continue; // FR-005 — unchanged content, skip re-chunking/re-embedding
            }

            var chunk = DocumentChunk.Create(
                knowledgeBase.Id, knowledgeBaseDocument.Id, document.Id, version.Id, knowledgeBase.ChunkingStrategy,
                candidate.Content, contentHash, candidate.TokenCount, candidate.CharacterCount, candidate.Language,
                candidate.PageNumber, candidate.Section, candidate.Heading, candidate.Position, Actor);

            documentChunkRepository.Add(chunk);
            chunksNeedingEmbedding.Add(chunk);
        }

        if (chunksNeedingEmbedding.Count == 0)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return IndexingOutcome.Completed;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        foreach (var batch in chunksNeedingEmbedding.Chunk(EmbeddingBatchSize))
        {
            var texts = batch.Select(c => c.Content).ToList();
            var embeddingResults = await embeddingService.EmbedBatchAsync(texts, cancellationToken); // FR-050 batch generation

            var newEmbeddings = new List<(Guid ChunkId, Embedding Embedding, float[] Vector)>();
            for (var i = 0; i < batch.Length; i++)
            {
                await embeddingRepository.MarkExistingSupersededAsync(batch[i].Id, Actor, cancellationToken); // FR-008
                var embedding = Embedding.Create(batch[i].Id, provider.Id, embeddingResults[i].Vector, Actor);
                embeddingRepository.Add(embedding);
                newEmbeddings.Add((batch[i].Id, embedding, embeddingResults[i].Vector));
            }

            await unitOfWork.SaveChangesAsync(cancellationToken); // rows must exist before the raw-SQL vector UPDATE below

            foreach (var (chunkId, embedding, vector) in newEmbeddings)
            {
                await vectorStore.UpsertAsync(chunkId, embedding.Id, knowledgeBase.Id, vector, cancellationToken);
            }
        }

        return IndexingOutcome.Completed;
    }

    private async Task<(Document Document, DocumentVersion Version)> LoadExistingDocumentAsync(Guid documentId, CancellationToken cancellationToken)
    {
        var document = await documentRepository.GetByIdAsync(documentId, cancellationToken)
            ?? throw new KeyNotFoundException("Linked document not found.");
        var version = await documentRepository.GetVersionByIdAsync(document.CurrentVersionId, cancellationToken)
            ?? throw new KeyNotFoundException("Current document version not found.");

        return (document, version);
    }

    /// <summary>
    /// research.md Decision 2 — reuses the Document Intelligence Pipeline's existing
    /// <see cref="IDocumentTextExtractor"/> implementations (never re-implements OCR/parsing,
    /// constitution §18) to create a <see cref="Document"/>/<see cref="DocumentVersion"/> from a
    /// knowledge-base document's already-stored file.
    /// </summary>
    private async Task<(Document Document, DocumentVersion Version)> CreateDocumentFromKnowledgeBaseDocumentAsync(
        KnowledgeBaseDocument knowledgeBaseDocument, KnowledgeBase knowledgeBase, CancellationToken cancellationToken)
    {
        var fileType = InferFileType(knowledgeBaseDocument.FileName);
        var extractor = textExtractors.FirstOrDefault(e => e.CanHandle(fileType))
            ?? throw new InvalidOperationException($"No text extractor is registered for file type '{fileType}'.");

        DocumentTextExtractionResult extraction;
        string checksumHash;
        await using (var contentStream = await fileStorage.OpenReadAsync(knowledgeBaseDocument.StoredFileName, cancellationToken))
        {
            extraction = await extractor.ExtractAsync(contentStream, fileType, cancellationToken);
        }

        await using (var hashStream = await fileStorage.OpenReadAsync(knowledgeBaseDocument.StoredFileName, cancellationToken))
        {
            checksumHash = await ComputeSha256Async(hashStream, cancellationToken);
        }

        var checksum = DocumentChecksum.Create(checksumHash, Actor);
        documentRepository.AddChecksum(checksum);

        var documentId = Guid.CreateVersion7();
        var version = DocumentVersion.Create(
            documentId, versionMajor: 1, versionMinor: 0, knowledgeBaseDocument.StoredFileName, knowledgeBaseDocument.FileName,
            knowledgeBaseDocument.SizeBytes, checksum.Id, Actor);
        version.ApplyExtractedText(extraction.PlainText, extraction.StructureJson, extraction.PageCount, Actor);

        var document = Document.Create(
            documentId, knowledgeBase.OwnerId, knowledgeBaseDocument.FileName, fileType, knowledgeBaseDocument.SizeBytes, version.Id, Actor);
        document.SetProcessingStatus(DocumentProcessingStatus.Completed, Actor);

        documentRepository.Add(document);
        documentRepository.AddVersion(version);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return (document, version);
    }

    private async Task<EmbeddingProvider> ResolveEmbeddingProviderAsync(KnowledgeBase knowledgeBase, CancellationToken cancellationToken)
    {
        var provider = knowledgeBase.EmbeddingProviderId is { } providerId
            ? await embeddingProviderRepository.GetByIdAsync(providerId, cancellationToken)
            : await embeddingProviderRepository.GetDefaultAsync(EmbeddingHostingType.Cloud, cancellationToken);

        return provider ?? throw new InvalidOperationException("No embedding provider could be resolved for this knowledge base.");
    }

    /// <summary>A minimal extension-based mapping — the Documents module's own upload flow already validates content by magic bytes (constitution §8); this indexing path trusts an already-accepted, already-stored file.</summary>
    private static DocumentFileType InferFileType(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        ".pdf" => DocumentFileType.Pdf,
        ".doc" or ".docx" => DocumentFileType.Word,
        ".xls" or ".xlsx" => DocumentFileType.Excel,
        ".ppt" or ".pptx" => DocumentFileType.PowerPoint,
        ".md" or ".markdown" => DocumentFileType.Markdown,
        ".csv" => DocumentFileType.Csv,
        ".txt" => DocumentFileType.Text,
        _ => DocumentFileType.Text,
    };

    private static string ComputeSha256(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private static async Task<string> ComputeSha256Async(Stream stream, CancellationToken cancellationToken)
    {
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexStringLower(hash);
    }
}

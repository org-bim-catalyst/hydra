# ADR-0007: Pinecone as a second, per-knowledge-base vector store alongside SQL Server

**Status**: Accepted
**Date**: 2026-08-05
**Deciders**: Engineering (SPEC-016 RAG & Semantic Search Engine, Foundational implementation)

## Context

`specs/016-rag-semantic-search`'s Foundational implementation stores embeddings in SQL Server's
native `vector(n)` column and searches them with a brute-force `VECTOR_DISTANCE` scan, deliberately
without a `CREATE VECTOR INDEX` (research.md Decision 3). That decision was verified directly
against the real hosted SQL Server 2025 (non-Azure, RTM-CU3, Standard Edition) Test and Production
databases, which the platform owner upgraded from SQL Server 2022 specifically to get native vector
support: creating a vector index there requires enabling the `PREVIEW_FEATURES` scoped
configuration, and the index it produces is the pre-Azure/Fabric format (`sys.vector_indexes.
index_version = NULL`, not the "3"/latest format) — Microsoft's own documentation states the
DML-compatible latest format is available "only in Azure SQL Database and SQL database in Microsoft
Fabric currently." The pre-Azure/Fabric index format makes the indexed table **permanently
read-only for all DML** (confirmed: `INSERT` fails with error 42231 once the index exists), and the
documented `ALLOW_STALE_VECTOR_INDEX` workaround is not recognized on this SQL Server build either.
This is a direct, confirmed conflict with FR-010/FR-011/US5's requirement to keep knowledge bases
continuously, incrementally indexable — a vector index cannot coexist with ongoing writes on this
real, non-Azure deployment target.

Constitution §5 requires an ADR before introducing any vector database beyond SQL Server. This ADR
records that decision.

## Decision

Make the platform genuinely vector-store-agnostic now, at the per-knowledge-base level, rather than
waiting for a future SQL Server release or migrating the whole platform to Azure SQL/Fabric:

- Add Pinecone as a second `IVectorStore` implementation (`PineconeVectorStore`), integrated via its
  plain REST data-plane API rather than the official gRPC/Protobuf-based `Pinecone.Client` SDK —
  every other external provider in this codebase (OpenAI, Anthropic, Gemini, OpenRouter,
  ElevenLabs) is a JSON/HTTP `IHttpClientFactory` integration, and pulling in `Grpc.Net.Client`/
  `Google.Protobuf` for one provider would be an unnecessary new dependency class (CLAUDE.md Core
  Design Principles).
- Add `KnowledgeBase.VectorStoreProvider` (`SqlServer` | `Pinecone`), selected **per knowledge
  base**, resolved at runtime via a new `IVectorStoreResolver` (mirroring the existing
  `IEmbeddingServiceResolver` pattern within this same feature area).
- Pinecone becomes the default for **newly created** knowledge bases going forward. Existing
  knowledge bases are backfilled to `SqlServer` by the migration (their vectors are already there —
  defaulting them to an empty Pinecone index would silently break their search, which this
  project's "no silent failures" rule forbids).
- `KnowledgeBase.RequiresDataResidency` is extended to also force `VectorStoreProvider = SqlServer`
  — Pinecone is a third-party US-hosted SaaS, the same rationale that already forces
  `EmbeddingHostingType.Local` for these knowledge bases.
- `SqlServerVectorStore` is kept, unmodified, as the other available choice — not deprecated. A
  knowledge base can select it at any time (e.g. if a future SQL Server release resolves the
  read-only-index limitation, or for data-residency reasons).
- A knowledge-base search spanning both providers is partitioned by `VectorStoreProvider`, queried
  against each store separately, and the results merged and re-ranked by distance before truncating
  to the requested `topK` (`SemanticSearchQueryHandler`) — silently querying only one provider's
  knowledge bases would drop the other provider's results with no error, which this project's "no
  silent failures" rule (CLAUDE.md Error Handling) forbids.

## Alternatives considered

- **Stay SQL-Server-only, periodically drop/rebuild the vector index outside a maintenance
  window** — rejected: the read-only-after-index-creation finding means there is no maintenance
  window that doesn't also block writes for its entire duration, incompatible with "continuous
  incremental indexing."
- **Migrate the whole platform to Azure SQL Database/Fabric** to get the DML-compatible "latest"
  vector index format — rejected/deferred: a much larger infrastructure/hosting migration than this
  feature's scope, and still single-vendor lock-in rather than genuine provider choice.
- **Qdrant (self-hosted) instead of Pinecone** — deferred: adds an operational (self-hosting)
  burden a managed SaaS choice avoids; worth revisiting if data-residency requirements later expand
  beyond what forcing `RequiresDataResidency` knowledge bases onto SQL Server already covers.
- **A single platform-wide vector-store setting instead of per-knowledge-base** — rejected: would
  force an all-or-nothing choice and could not respect `RequiresDataResidency` at the granularity
  the platform already offers for embedding-provider selection.

## Consequences

- Pinecone becomes a new external, paid dependency on the RAG critical path for any knowledge base
  configured to use it, with its own outage/cost surface distinct from the platform's SQL Server
  dependency.
- Data residency is covered by extending the existing `RequiresDataResidency` guard rather than
  introducing a new mechanism — one invariant, enforced in one place
  (`KnowledgeBase.UpdateRetrievalSettings`).
- A knowledge-base search spanning knowledge bases on both providers requires the partition-and-
  merge logic in `SemanticSearchQueryHandler` — new, correctness-sensitive code, covered by a
  dedicated test proving results from both providers survive the merge.
- `IVectorStore` gained a `Provider` member and an additional `knowledgeBaseId` parameter on
  `UpsertAsync` (Pinecone needs it as vector metadata for its KB-scoped query filter) — both
  additive changes; `SqlServerVectorStore`'s existing behavior is unaffected (it ignores the new
  parameter, already scoping by `KnowledgeBaseId` at query time via its `DocumentChunks` join).
- If SQL Server later ships a DML-compatible vector index on a non-Azure edition, or the platform
  moves to Azure SQL/Fabric, reverting a knowledge base to `SqlServer` (or changing the new-KB
  default back) is a configuration/data change, not a rewrite — this was the whole point of keeping
  `IVectorStore` as the only surface Domain/Application code touches (constitution §5, spec.md
  FR-015).

See `specs/016-rag-semantic-search/research.md` Decision 3 for the full SQL Server 2025 vector-index
finding this ADR builds on.

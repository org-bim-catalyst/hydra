using MediatR;

namespace AskLucy.Application.KnowledgeBases.Queries.ExportKnowledgeBase;

/// <summary>Structured, portable export of a knowledge base's metadata (FR-033) — name, description, category, tags, folder structure, statistics, notes. Never document contents.</summary>
public sealed record ExportKnowledgeBaseQuery(Guid Id) : IRequest<KnowledgeBaseExportDto>;

namespace AskLucy.Application.KnowledgeBases;

/// <summary>
/// Which slice of a user's knowledge bases to list (FR-023/FR-027) — `view` query parameter
/// (contracts/knowledge-bases-api.md). <see cref="Active"/> means "not archived, not
/// soft-deleted" — i.e. includes both `Status: Draft` and `Status: Active`, so a
/// just-created Draft knowledge base appears immediately (spec.md User Story 1, Acceptance
/// Scenario 1). <see cref="Deleted"/> bypasses the soft-delete filter entirely, regardless of
/// prior `Status`.
/// </summary>
public enum KnowledgeBaseListView
{
    Active,
    Archived,
    Deleted,
}

namespace AskLucy.Application.Documents;

/// <summary>Mirrors <c>KnowledgeBaseListView</c> (specs/014). <c>Active</c> means "not archived, not soft-deleted."</summary>
public enum DocumentListView
{
    Active,
    Archived,
    Deleted,
}

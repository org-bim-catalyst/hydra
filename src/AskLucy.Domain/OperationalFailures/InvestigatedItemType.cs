namespace AskLucy.Domain.OperationalFailures;

/// <summary>The kinds of user item an administrator can open from an incident (FR-016a).</summary>
public enum InvestigatedItemType
{
    Chat,
    WorkflowRun,
    Document,
}

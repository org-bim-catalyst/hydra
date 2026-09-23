namespace AskLucy.Domain.CustomModels;

/// <summary>specs/072 FR-022. <see cref="Queued"/>, <see cref="Listing"/> and <see cref="Transferring"/> are in progress; the other three are terminal.</summary>
public enum CustomModelDeploymentState
{
    Queued,
    Listing,
    Transferring,
    Completed,
    Failed,
    Cancelled,
}

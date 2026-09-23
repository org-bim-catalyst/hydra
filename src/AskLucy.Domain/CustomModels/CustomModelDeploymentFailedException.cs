namespace AskLucy.Domain.CustomModels;

/// <summary>
/// A deployment rule the job must turn into a <see cref="CustomModel.Fail"/> with this
/// <see cref="Kind"/> (for example the size cap in <see cref="CustomModel.BeginTransfer"/>). The
/// message is shown to the admin, so it never contains a secret or the deployment root.
/// </summary>
public sealed class CustomModelDeploymentFailedException(CustomModelFailureKind kind, string message) : Exception(message)
{
    public CustomModelFailureKind Kind { get; } = kind;
}

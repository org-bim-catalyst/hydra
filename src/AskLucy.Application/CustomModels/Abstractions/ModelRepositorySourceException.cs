namespace AskLucy.Application.CustomModels.Abstractions;

public enum ModelRepositorySourceFailureKind
{
    NotFound,
    Unavailable,
    GatedOrPrivate,
    IntegrityMismatch,
    DownloadStalled,
}

/// <summary>specs/072. A model-repository failure whose message is safe to show an admin. The original exception, if any, is the inner exception.</summary>
public sealed class ModelRepositorySourceException(ModelRepositorySourceFailureKind kind, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public ModelRepositorySourceFailureKind Kind { get; } = kind;
}

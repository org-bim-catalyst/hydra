namespace AskLucy.Application.CustomModels.Abstractions;

public enum DeploymentTargetFailureKind
{
    AuthRejected,
    TlsNotAccepted,
    CertificateInvalid,
    ConnectionLost,
    WriteRejected,
    SizeMismatch,
}

/// <summary>
/// specs/072 research D2. A deployment-target failure whose message is safe to show an admin and to
/// persist: it never contains the password, host or root path. The FTP library's exception, if any,
/// is the <see cref="Exception.InnerException"/>, which is never interpolated into a message.
/// </summary>
public sealed class DeploymentTargetException(DeploymentTargetFailureKind kind, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public DeploymentTargetFailureKind Kind { get; } = kind;
}

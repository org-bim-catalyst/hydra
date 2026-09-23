namespace AskLucy.Domain.CustomModels;

/// <summary>specs/072 FR-017/FR-018. Why a deployment failed. The admin sees a safe reason alongside it; neither ever carries the deployment target's password, host or root path.</summary>
public enum CustomModelFailureKind
{
    SourceNotFound,
    SourceUnavailable,
    SourceGatedOrPrivate,
    SizeLimitExceeded,
    UnsafeRepositoryPath,
    ReservedFileName,
    IntegrityMismatch,
    DownloadStalled,
    DiskSpaceExhausted,
    TargetNotConfigured,
    TargetAuthRejected,
    TargetTlsNotAccepted,
    TargetCertificateInvalid,
    TargetConnectionLost,
    TargetWriteRejected,
    TargetSizeMismatch,
    InterruptedByRestart,
    Unexpected,
}

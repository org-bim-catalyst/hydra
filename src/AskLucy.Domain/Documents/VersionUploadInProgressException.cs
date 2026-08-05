namespace AskLucy.Domain.Documents;

/// <summary>
/// Thrown by <c>RestoreDocumentVersion</c> when a replace-version upload (<c>ReplaceDocument</c>)
/// is still in progress for the same document — spec.md Edge Cases: "What happens when a user
/// tries to restore a version while a new version upload is simultaneously in progress?" Mapped
/// to <c>409 Conflict</c> with <c>reason: "VersionUploadInProgress"</c>
/// (contracts/document-versions-folders-api.md), the same pattern as
/// <see cref="ProcessingNotInFailedStateException"/>. Restoring while an upload targeting this
/// document hasn't finished would race the in-flight <c>ReplaceDocument</c> finalize against a
/// concurrent `CurrentVersionId` change, risking a corrupted-looking version history —
/// deterministic rejection instead, per the Edge Case.
/// </summary>
public sealed class VersionUploadInProgressException() : Exception("A replacement upload is already in progress for this document. Try again once it finishes.");

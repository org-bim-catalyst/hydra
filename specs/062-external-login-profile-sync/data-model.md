# Phase 1 Data Model: External Login Profile Sync

No new entities and no database migration. This feature populates existing fields from a new
data source (OAuth claims) and adds two Application-layer contracts to carry that data through
the existing pipeline.

## Existing Entities (unchanged schema)

### `ApplicationUser` (`src/AskLucy.Persistence/Identity/ApplicationUser.cs`)

| Field | Type | Change |
|---|---|---|
| `FirstName` | `string?` | None — now also written from provider claims (sync, on sign-in/link) in addition to manual edit |
| `LastName` | `string?` | None — same as above |
| `AvatarFileName` | `string?` | None — now also written from a provider-sourced picture (async, on sign-in/link) in addition to manual upload |

**Overwrite rule** (FR-006, Assumptions): a provider-supplied value always wins on the sign-in/link
event that supplies it, whether the previous value came from a manual edit or an earlier sync. A
field is left unchanged only when the provider's claim for that field is absent on that specific
event (FR-004).

## New Application Contracts

### `ProcessExternalLoginCallbackCommand` (extended)

```csharp
public sealed record ProcessExternalLoginCallbackCommand(
    string Provider,
    string ProviderKey,
    string? Email,
    bool EmailVerified,
    string? LinkToUserId,
    string? FirstName,     // NEW — from GivenName claim, when supplied
    string? LastName,      // NEW — from Surname claim, when supplied
    string? PictureUrl     // NEW — from the provider-specific picture claim, when supplied
) : IRequest<string?>;
```

Populated in `ExternalAuth.HandleTicketReceivedAsync` (`src/AskLucy.Web/Auth/ExternalAuth.cs`)
alongside the existing `NameIdentifier`/`Email`/`email_verified` extraction — this remains the only
place a `ClaimsPrincipal` is read; downstream code only ever sees plain strings.

### `IExternalProfilePictureSyncJob` (new, `Application.Abstractions`)

```csharp
public interface IExternalProfilePictureSyncJob
{
    /// <summary>
    /// Fetches <paramref name="pictureUrl"/> and stores it as the user's avatar, mirroring
    /// UploadAvatarCommandHandler's storage path. Runs off the sign-in/link request path
    /// (Hangfire background job) — a failure here MUST be logged (FR-008) and MUST NOT surface
    /// to the user, per the spec's explicit edge case, and MUST leave the previously stored
    /// avatar (if any) untouched on failure (FR-004). Downloaded content MUST pass
    /// IImageContentValidator's magic-byte check before being persisted; a failed check is
    /// treated as any other fetch failure (constitution §8).
    /// </summary>
    Task SyncAsync(string userId, string pictureUrl, CancellationToken cancellationToken = default);
}
```

Implemented by `ExternalProfilePictureSyncJob` in `AskLucy.Infrastructure`, composing:
- `IRemoteFileDownloader` (existing) — host-allow-listed, size-capped fetch (research.md Decisions 5–7)
- `IImageContentValidator` (new, research.md Decision 9) — magic-byte validation of the downloaded bytes before persisting; also retrofitted into `UploadAvatarCommandHandler`
- `IFileStorage` (existing) — `SaveAsync` to persist the downloaded bytes exactly as `UploadAvatarCommandHandler` does
- `IUserProfileRepository` (existing) — `SetAvatarFileNameAsync` to point the user record at the new file

No new interface is needed for the HTTP fetch itself (research.md Decision 5) or for size/host
validation (both live inside the job implementation, since they are wholly Infrastructure-side
policy with no Application-layer caller needing to vary them).

## Flow Summary

```text
OAuth ticket (Google/Facebook)
  → ExternalAuth.HandleTicketReceivedAsync              [Web]   extracts NameIdentifier, Email,
                                                                  email_verified, GivenName,
                                                                  Surname, picture-claim
  → ProcessExternalLoginCallbackCommand                 [Application]  carries all of the above
  → ProcessExternalLoginCallbackCommandHandler           [Application]
        1. identityService.ResolveExternalLoginAsync(...)         → resolves/creates/links user
        2. profiles.GetByIdAsync + merge-on-null + UpdateAsync    → name sync (synchronous)
        3. backgroundJobClient.Enqueue<IExternalProfilePictureSyncJob>  → picture sync (async)
        4. codeStore.Issue(...)                                    → unchanged, returns as before
  → ExternalProfilePictureSyncJob.SyncAsync(...)         [Infrastructure, background]
        1. host allow-list check
        2. IRemoteFileDownloader.DownloadAsync (size-capped)
        3. IFileStorage.SaveAsync
        4. IUserProfileRepository.SetAvatarFileNameAsync
        (any failure at 1–3 is logged and swallowed here — sign-in already completed)
```

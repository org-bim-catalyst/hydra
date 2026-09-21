# Phase 0 Research: External Login Profile Sync

All Technical Context unknowns were resolvable from the existing codebase and the feature's own
input/clarifications — no `[NEEDS CLARIFICATION]` markers remain.

## Decision 1: Claim mapping per provider

**Decision**: Extend the existing Google/Facebook handler registration in `Program.cs`:
- **Google**: `given_name`/`family_name` are already exposed via `ClaimTypes.GivenName`/`ClaimTypes.Surname` by the framework's default Google handler claim actions — no change needed for name mapping, but `options.Scope.Add("profile")` MUST be added explicitly — the handler's default scope list does not guarantee `profile` is requested, and without it the userinfo response will not contain `given_name`/`family_name`/`picture` regardless of claim mapping (FR-001). Add `options.ClaimActions.MapJsonKey("urn:google:picture", "picture")` for the picture URL (not mapped by default).
- **Facebook**: name fields are not returned by the default `/me` field set. Add `options.Fields.Add("first_name")`, `options.Fields.Add("last_name")`, `options.Fields.Add("picture")`, then `options.ClaimActions.MapJsonKey(ClaimTypes.GivenName, "first_name")`, `MapJsonKey(ClaimTypes.Surname, "last_name")`, and `MapCustomJson("urn:facebook:picture", user => user.GetProperty("picture").GetProperty("data").GetProperty("url").GetString())` for the nested picture path.

**Rationale**: Matches the documented ASP.NET Core OAuth handler behavior for these providers exactly (confirmed against the feature's own input); requires no additional Graph/People API round trip — the claims arrive on the same ticket already produced by the existing authorization-code exchange.

**Alternatives considered**: Calling Google's People API / Facebook Graph API separately after token exchange — rejected: adds a second outbound call and new provider-scoped access-token handling for data the OAuth ticket already carries for free.

## Decision 2: Where profile sync is orchestrated

**Decision**: `ProcessExternalLoginCallbackCommandHandler` (Application layer) is the single orchestration point for both flows — full sign-in and "link an additional provider" — because `IIdentityService.ResolveExternalLoginAsync` already handles both cases (`linkToUserId` null vs. set) and returns the resolved `UserId` either way.

**Rationale**: One code path satisfies FR-002, FR-003, and FR-003a without special-casing the link flow; keeps `IIdentityService` focused on identity resolution only (SRP).

**Alternatives considered**: Adding the sync logic inside `IdentityService.ResolveExternalLoginAsync` itself — rejected: would mix identity-resolution concerns (Persistence/`ApplicationUser`) with profile-repository, file-storage, and background-job concerns that already have their own Application-owned interfaces; also would require `IIdentityService`'s public contract to grow purely to serve this one caller.

## Decision 3: Name/last-name sync semantics (synchronous, merge-on-null)

**Decision**: In the handler, after resolving the user id, fetch the current profile via `IUserProfileRepository.GetByIdAsync`, compute `effectiveFirstName = claimFirstName ?? currentProfile.FirstName` (same for last name), then call the existing `IUserProfileRepository.UpdateAsync(userId, effectiveFirstName, effectiveLastName)` synchronously, before the sign-in response is produced.

**Rationale**: `UpdateAsync` already unconditionally overwrites both fields (used today by `UpdateMyProfileCommand`, where a null legitimately means "user cleared this field"); merging in the handler preserves FR-004 ("leave unchanged when the provider doesn't supply a value") without changing `UpdateAsync`'s existing contract or adding a new partial-update method.

**Alternatives considered**: Adding a `UpdateIfProvidedAsync` variant to `IUserProfileRepository` — rejected (YAGNI): the merge is a two-line read-then-write in the handler, and `UpdateMyProfileCommand`'s existing "always overwrite" semantics is exactly right for its own manual-edit use case, so the interface itself doesn't need to change.

## Decision 4: Picture sync is asynchronous via a background job

**Decision**: Add `IExternalProfilePictureSyncJob` to `Application.Abstractions`, enqueued from the handler via `IBackgroundJobClient.Enqueue<IExternalProfilePictureSyncJob>(j => j.SyncAsync(userId, pictureUrl, CancellationToken.None))`, implemented in `Infrastructure` as `ExternalProfilePictureSyncJob`.

**Rationale**: Directly mirrors the already-established `IPasswordEmailJob`/`PasswordEmailJob` and `IPasswordResetIssuanceJob` pattern (dispatch via Hangfire, off the request path) used elsewhere in this exact `Authentication` feature area — Convention Over Configuration (§7). Satisfies the clarified requirement that a slow/failing picture fetch never adds latency to, or can fail, sign-in (FR-003b, SC-004). Hangfire's built-in automatic retry covers transient fetch failures without new retry logic.

**Alternatives considered**: Synchronous fetch-and-store inline in the handler — rejected per clarification (adds latency and couples an external HTTP call's failure mode to sign-in success).

## Decision 5: Picture download implementation reuses `IRemoteFileDownloader`

**Decision**: `ExternalProfilePictureSyncJob` depends on the existing generic `IRemoteFileDownloader` (already implemented by `RemoteFileDownloader` for SiteAnalysis imagery downloads) rather than a new fetch abstraction, registered against its own named `HttpClient` (`"ExternalProfilePictureDownload"`) in `Infrastructure/DependencyInjection.cs`, following the exact precedent set by `"SiteAnalysisImageDownload"`/`"Geocoding"`/`"Overpass"`.

**Rationale**: `IRemoteFileDownloader.DownloadAsync(Uri) -> DownloadedFile(Stream, ContentType?)` is already exactly the shape needed; a dedicated named client lets this integration's timeout and (see Decision 6) host restriction diverge independently from every other outbound integration, matching this codebase's stated convention for "why a new named client" (see `RemoteFileDownloader`'s own doc comment).

**Alternatives considered**: A new `IExternalProfilePictureFetcher` interface — rejected (YAGNI/DRY): would duplicate `IRemoteFileDownloader`'s exact shape for no behavioral difference.

## Decision 6: Outbound fetch is host-restricted (SSRF hardening)

**Decision**: Before downloading, `ExternalProfilePictureSyncJob` validates the picture URL's host against a small allow-list per provider (Google: `*.googleusercontent.com`; Facebook: `platform-lookaside.fbsbx.com`, `*.fbcdn.net`). A URL that fails the check is treated the same as any other fetch failure (FR-008: logged, skipped, previous picture retained).

**Rationale**: The picture URL is a claim value originating from a third party (the OAuth provider's own response), not the end user directly, but it is still external input driving a server-side outbound HTTP request — constitution §8's OWASP baseline applies. Restricting to each provider's known image-CDN domains closes the SSRF class of risk (a compromised/malformed claim pointing the server at an internal address or arbitrary host) at negligible cost, since legitimate provider responses never point anywhere else.

**Alternatives considered**: No host restriction, trusting the provider's signed OAuth response entirely — rejected: still leaves a server-side-fetch surface directly reachable via provider-side claim content, which the constitution's "least privilege / secure defaults" principle (§8) counsels against when the mitigation is this cheap.

## Decision 7: Reuse the existing 5MB avatar size limit

**Decision**: `ExternalProfilePictureSyncJob` enforces the same 5MB cap already applied to manually uploaded avatars (`UsersController.UploadAvatar`'s `[RequestSizeLimit(5 * 1024 * 1024)]`) by using `HttpCompletionOption.ResponseHeadersRead` and aborting if `Content-Length` (when present) exceeds the limit, or if the streamed byte count exceeds it during copy.

**Rationale**: Consistency with the spec's edge case ("existing image validation/sanitization applied to manually uploaded avatars applies equally to provider-sourced pictures"); avoids introducing a second, undocumented size policy.

**Alternatives considered**: No explicit cap (trusting `IFileStorage`/disk space) — rejected: provider CDNs are trusted for content but an unbounded download is still an unnecessary resource-exhaustion surface once a background job triggers per sign-in.

## Decision 8: No new database schema

**Decision**: No migration is needed — `ApplicationUser.FirstName`, `LastName`, and `AvatarFileName` already exist and are exactly what this feature populates.

**Rationale**: Confirmed by reading `ApplicationUser.cs`; these columns were added for manual profile editing/avatar upload (FR-025, prior work) and are schema-compatible with provider-sourced values.

## Decision 9: Shared magic-byte image validation

**Decision**: Add `IImageContentValidator` to `Application.Abstractions`
(`bool IsValidImage(Stream content, out string? detectedContentType)`), implemented in
`Infrastructure` via magic-byte signature checks (JPEG/PNG/GIF/WebP). Called by both the new
`ExternalProfilePictureSyncJob` and the existing `UploadAvatarCommandHandler`, replacing that
handler's current lack of content validation.

**Rationale**: Constitution §8 requires content-based (magic-byte) file validation, not
extension/MIME-header trust, for every persisted upload — non-negotiable. The provider-sourced
picture is a *less* trusted input than a direct user upload (URL claim from a third-party
ticket), so it cannot ship with weaker validation than manual upload already has. Fixing both
call sites with one shared check avoids introducing a second, divergent validation
implementation (DRY, §2.III).

**Alternatives considered**: Validating only in the new job and leaving `UploadAvatarCommandHandler`
as-is — rejected: leaves a known non-negotiable constitution violation in place when the fix is
a small, shared addition, and would mean two different avatar-writing paths enforce different
security postures.

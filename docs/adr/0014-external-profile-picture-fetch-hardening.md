# ADR 0014: Host-Restricting and Content-Validating Provider-Sourced Profile Pictures

**Status:** Accepted

**Date:** 2026-09-21

**Feature:** [specs/062-external-login-profile-sync](../../specs/062-external-login-profile-sync/spec.md)

## Context

Google and Facebook sign-in now supply a `picture` claim on the OAuth ticket. Syncing it means
the server fetches a URL whose value originates from a third party's response, not from the
signed-in user directly, and writes the result to disk as the user's avatar via the same
`IFileStorage` path manual avatar upload already uses.

Two gaps had to be closed before that fetch could exist at all:

* An outbound HTTP request driven by external claim content is a server-side request forgery
  (SSRF) surface — constitution §8's OWASP baseline applies even though the value comes from an
  OAuth provider's ticket rather than a raw user-supplied field.
* `UploadAvatarCommandHandler` (the existing manual-upload path) had no content-based file
  validation at all — extension/MIME-header trust only — which is a standing constitution §8
  violation independent of this feature, but one this feature would otherwise inherit and extend
  to a second, less-trusted input source without fixing.

## Decision

1. **Host allow-list.** `ExternalProfilePictureSyncJob` validates the picture URL's host against
   a small per-provider allow-list (Google: `*.googleusercontent.com`; Facebook:
   `platform-lookaside.fbsbx.com`, `*.fbcdn.net`) before calling `IRemoteFileDownloader`. A URL
   that fails the check is treated identically to any other fetch failure: logged, skipped, and
   the previously stored avatar (if any) is left untouched (FR-008).
2. **Shared magic-byte validation.** A new `IImageContentValidator.IsValidImage(Stream, out
   string? detectedContentType)` (JPEG/PNG/GIF/WebP signature check) is called by both
   `ExternalProfilePictureSyncJob` and `UploadAvatarCommandHandler`, retrofitting the
   previously-missing check onto the manual-upload path instead of leaving two avatar-writing
   call sites with different security postures.
3. **Reused 5MB cap.** The job enforces the same limit already applied to manual uploads
   (`UsersController.UploadAvatar`'s `[RequestSizeLimit]`), via `Content-Length` and a
   streamed-byte-count abort, rather than introducing a second size policy.

See [research.md Decisions 6, 7, 9](../../specs/062-external-login-profile-sync/research.md) for
the full rationale and alternatives considered for each.

## Consequences

**A compromised or malformed `picture` claim cannot turn the sync job into an internal-network
probe or arbitrary-host fetcher** — only each provider's own known image-CDN hosts are reachable,
which is where legitimate responses always point anyway.

**Manual avatar upload gained the content validation it was missing**, closing a pre-existing
constitution §8 gap as a side effect of this feature rather than deferring it — one shared
implementation, not two divergent ones.

**A failed host or content check never surfaces to the user and never fails sign-in.** Both
checks live entirely inside the async `ExternalProfilePictureSyncJob` (Hangfire), consistent with
[ARCHITECTURE.md §18](../ARCHITECTURE.md#18-background-processing)'s existing pattern of keeping
this feature's background jobs off the request path.

## Alternatives Considered

**Trust the OAuth provider's signed response entirely; no host restriction.** Rejected: the
ticket's authenticity does not make every claim value inside it a safe server-side fetch target;
restricting to each provider's documented image-CDN domains closes the SSRF class at negligible
cost since legitimate responses never point anywhere else.

**Validate content only in the new job, leave `UploadAvatarCommandHandler` unfixed.** Rejected:
would leave a known constitution violation in place when the fix is a small, shared addition, and
would mean the two avatar-writing paths enforce different security postures for no reason.

## Related

* [specs/062-external-login-profile-sync/research.md](../../specs/062-external-login-profile-sync/research.md) — Decisions 5–7, 9
* [specs/062-external-login-profile-sync/data-model.md](../../specs/062-external-login-profile-sync/data-model.md)
* [docs/ARCHITECTURE.md §18 Background Processing](../ARCHITECTURE.md)

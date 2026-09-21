# Quickstart: Validating External Login Profile Sync

## Prerequisites

- Google and Facebook OAuth app credentials configured (`Authentication:Google:ClientId`/`ClientSecret`,
  `Authentication:Facebook:AppId`/`AppSecret`) via user-secrets or environment variables — see
  `Program.cs`'s existing conditional registration.
- The API and frontend running locally (`dotnet run` for `AskLucy.Web`, `npm run dev` under
  `ClientApp`), with Hangfire's storage reachable (existing dev DB connection string) since the
  picture sync runs as a Hangfire job.
- A Google account and/or Facebook account with a name and profile photo set, usable for test
  sign-in.

## Scenario 1 — Brand-new account (User Story 1)

1. Sign out / use an incognito session.
2. Sign in via "Continue with Google" (or Facebook) using an account with a name and photo, and
   with no existing Ask Lucy account for that email.
3. Once redirected into the app, call `GET /api/v1/users/me`.
4. **Expected**: `firstName`/`lastName` are populated immediately (visible in the same response
   that follows sign-in). The avatar URL becomes non-null within a few seconds (poll or refresh)
   once the background job completes — the sign-in itself must not have waited on it.

## Scenario 2 — Existing account self-heals (User Story 2)

1. Using an account created before this feature (or with `FirstName`/`LastName`/`AvatarFileName`
   manually cleared in the dev DB) that already has a linked Google/Facebook login, sign in again
   via that provider.
2. **Expected**: `GET /api/v1/users/me` now shows the name and (shortly after) the avatar,
   with no manual data entry or admin action performed.

## Scenario 3 — Provider-side change propagates (User Story 3)

1. Change the display name or profile photo on the linked Google/Facebook account.
2. Sign in to Ask Lucy again with that account.
3. **Expected**: the updated name is visible immediately; the updated picture appears shortly
   after, replacing the previous one (even if the previous one was manually uploaded within Ask
   Lucy).

## Scenario 4 — Picture fetch failure doesn't block sign-in

1. Temporarily block outbound access to the provider's image CDN host (e.g., firewall rule, or
   point the claim's host allow-list check to reject it in a test build).
2. Sign in via the affected provider.
3. **Expected**: sign-in still succeeds without added delay; `firstName`/`lastName` still sync;
   the avatar is left unchanged (or stays null for a new account); a warning-level log entry
   appears for the failed picture sync, tagged with the user id and provider.

## Scenario 5 — Linking a second provider (Clarification: link flow parity)

1. While already signed in (e.g., via email/password or a first social provider), link an
   additional Google or Facebook account from Settings that has a different name/photo.
2. **Expected**: the profile's name/picture refresh from the newly linked provider's claims,
   identically to Scenario 1/2 — the linking action itself triggers the sync, not just a
   subsequent full sign-in via that provider.

## Automated coverage (see tasks.md for the full breakdown)

- `AskLucy.Application.Tests/Authentication/ProcessExternalLoginCallbackCommandHandlerTests.cs` —
  asserts the handler merges claim values with the existing profile (FR-004), always calls
  `UpdateAsync` synchronously, and enqueues `IExternalProfilePictureSyncJob` only when a picture
  URL is present, for both the sign-in and link (`LinkToUserId` set) paths.
- `AskLucy.Infrastructure.Tests/Identity/ExternalProfilePictureSyncJobTests.cs` — asserts the host
  allow-list rejects non-provider hosts, the size cap aborts an oversize download, and a fetch
  failure logs without throwing.
- `AskLucy.Web.Tests/Auth/ExternalLoginTests.cs` — asserts the Google/Facebook handler
  configuration maps the new claims as specified in `contracts/external-login-claim-contract.md`.

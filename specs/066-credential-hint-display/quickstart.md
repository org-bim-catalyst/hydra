# Quickstart: Credential Hint Display

## Prerequisites

- Backend running with a migrated dev DB (`AskLucy.Web` + SQL Server).
- Logged in as an administrator with `admin.ai-providers.manage`/`.view`.
- At least one provider with no credential, and at least one with a real (or dev-seeded) credential.

## Scenario 1 — New credential shows a hint (User Story 1 & 2)

1. Open **Admin → AI Providers**.
2. Pick a provider with no credential configured. Confirm its row shows the "no key set" indicator next to its name.
3. Open "Set credential", enter a key of at least 8 characters (e.g. `sk-test-1234567890ABCDEFghij`), confirm.
4. Confirm the table refreshes and now shows a hint next to that provider's name in the form `sk-t...ghij` (first 4 + `...` + last 4 of what you typed).

## Scenario 2 — Replacing updates the hint (FR-005)

1. On the same provider, open "Replace credential", enter a different key (e.g. `zzzz-different-key-WXYZ`).
2. Confirm the table's hint updates to match the new key's first/last 4 characters, not the old one.

## Scenario 3 — Clearing removes the hint (FR-006)

1. Use "Clear credential" on that provider.
2. Confirm the row reverts to the "no key set" indicator and the hint is gone.

## Scenario 4 — Short key falls back to full mask (FR-008, edge case)

1. Set a credential shorter than 8 characters (e.g. `abc123` — dev/test only, real vendor keys are always longer).
2. Confirm the table shows `****`, not a partial or full reveal of the short value.

## Scenario 5 — Pre-existing credentials get backfilled (FR-009)

1. Using a DB with a provider credential set *before* this feature was deployed (or simulate by directly nulling that row's `CredentialHint` column while leaving `CredentialCiphertext` populated).
2. Restart the backend.
3. Reload the AI Providers table — confirm the hint now appears without re-entering the credential.

## Scenario 6 — No full key ever reaches the client (SC-003)

1. Open browser DevTools → Network tab.
2. Reload the AI Providers page and inspect the `GET /api/v1/admin/ai/providers` response body.
3. Confirm `credentialHint` values are short (`xxxx...xxxx` or `****`) and no field anywhere in the response contains a full key.

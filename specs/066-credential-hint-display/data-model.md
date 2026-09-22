# Phase 1 Data Model: Credential Hint Display

## Entity: `AIProvider` (existing — extended)

| Field | Type | Change | Notes |
|---|---|---|---|
| `CredentialCiphertext` | `string?` | unchanged | Data-Protection-encrypted API key. Still never selected into any DTO/read projection. |
| `CredentialHint` | `string?` | **NEW** | First 4 + `...` + last 4 characters of the plaintext key at the time it was last set, or `****` if the key was too short to split safely (FR-008). `null` when no credential is configured. Safe to return to the client — the inverse of `CredentialCiphertext`'s never-return rule. |
| `CredentialLastRotatedAtUtc` | `DateTime?` | unchanged | Already updated by `SetCredential`; `CredentialHint` changes in lockstep (same call). |

### State transitions

- **Set/Replace credential** (`SetCredential(ciphertext, hint, actor)`): both `CredentialCiphertext` and `CredentialHint` are written together, atomically, in the same call — they can never observably disagree (no state where the ciphertext is new but the hint is stale, or vice versa).
- **Clear credential** (`ClearCredential(actor)`): both `CredentialCiphertext` and `CredentialHint` are set to `null` together.
- **Backfill** (one-time, startup): for rows where `CredentialCiphertext IS NOT NULL AND CredentialHint IS NULL` (i.e., configured before this feature shipped), decrypt once and populate `CredentialHint` only — `CredentialCiphertext` and `CredentialLastRotatedAtUtc` are untouched, since no actual credential change occurred.

### Validation rules

- `CredentialHint` is derived, never accepted as direct input from any controller/DTO — there is no `SetHint` mutator; it only ever changes as a side effect of `SetCredential`/`ClearCredential`/the backfill step.
- Length invariant: `CredentialHint` is either exactly `4 + 3 + 4 = 11` characters (`xxxx...xxxx`) or exactly `4` characters (`****`) — enforced by `CredentialHintFormatter`, not by a DB check constraint (consistent with how `CredentialLastRotatedAtUtc` etc. are validated in the Domain layer, not the database).

## DTO: `AdminAiProviderDto` (existing — extended)

Adds `string? CredentialHint` alongside the existing `bool HasCredential` — `HasCredential` remains the authoritative "is a credential configured" signal (derived from `CredentialCiphertext is not null`, unchanged); `CredentialHint` is purely presentational and always consistent with it (`HasCredential == false` implies `CredentialHint == null`, and vice versa).

## Frontend type: `AdminAiProvider` (existing — extended)

Adds `credentialHint: string | null`, mirroring the DTO field 1:1 (same pattern as every other field in this interface).

## New pure helper: `CredentialHintFormatter` (Application layer)

```text
static string Format(string plaintextApiKey)
  → plaintextApiKey.Length >= 8 ? $"{plaintextApiKey[..4]}...{plaintextApiKey[^4..]}" : "****"
```

No state, no dependencies, no I/O — a pure function, directly unit-testable without mocks.

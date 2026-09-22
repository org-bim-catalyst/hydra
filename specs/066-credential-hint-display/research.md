# Phase 0 Research: Credential Hint Display

## Decision 1: Where the hint is computed

**Decision**: Compute the hint from the plaintext API key inside `SetAiProviderCredentialCommandHandler`, immediately before `credentialProtector.Protect(request.ApiKey)` is called — the only place in the codebase that already legitimately holds the plaintext value.

**Rationale**: `AIProvider.SetCredential` currently receives an already-encrypted `ciphertext` and documents "the plaintext key is encrypted by the caller... Domain never sees it." Keeping that invariant, the hint is computed by that same caller and passed into `SetCredential` alongside the ciphertext, so Domain stores two derived-but-opaque strings without ever handling plaintext itself.

**Alternatives considered**:
- Compute inside `AIProvider.SetCredential` from the ciphertext — rejected, impossible without decrypting (defeats the point of a cheap, always-available hint) and would put crypto-adjacent concerns in Domain.
- Compute lazily on read (decrypt-on-read) — rejected explicitly by the spec (Assumptions: "not recomputed on every read") and by the user's own stated goal of returning only a shortened value, never re-touching the full key outside the write path.

## Decision 2: Hint format & short-key fallback

**Decision**: A pure static formatter, `CredentialHintFormatter.Format(string plaintext)`:
- If `plaintext.Length >= 8`: return `plaintext[..4] + "..." + plaintext[^4..]`.
- If `plaintext.Length < 8`: return a fixed fully-masked placeholder (`"****"`) — never a partial reveal.

**Rationale**: 8 is the minimum length at which the first 4 and last 4 characters are non-overlapping (`[0,4)` and `[len-4,len)` touch exactly at length 8, never overlap). This matches the vendor pattern shown in the user's reference screenshots (OpenAI: `sk-...33IA`; Anthropic: `sk-ant-api03-C5f...ygAA`) while guaranteeing FR-008 (no partial/full exposure of short keys).

**Alternatives considered**: A configurable threshold — rejected as unnecessary complexity (YAGNI); no real provider issues API keys under 20 characters, so the `<8` branch is a defensive fallback, not an expected path.

## Decision 3: Persistence shape

**Decision**: New nullable `string? CredentialHint` column on the existing `AIProviders` table, `HasMaxLength(20)` (11 chars needed for `xxxx...xxxx`, generous headroom), mapped like `CredentialLastRotatedAtUtc` (a plain, always-projectable column — explicitly *not* excluded from read projections, unlike `CredentialCiphertext`).

**Rationale**: Same table, same aggregate — one logical migration per constitution §5. Keeping it a separate column from `CredentialCiphertext` (rather than, say, encoding it into the ciphertext blob) keeps the "safe to return" / "never return" distinction enforced structurally: one column is excluded from DTOs by convention, the other is designed to be included.

**Alternatives considered**: Store the hint only in memory / recompute per request — rejected (spec Assumptions explicitly rules this out; would require decrypting on every table load, a strictly worse security posture than today).

## Decision 4: Backfilling pre-existing credentials

**Decision**: An idempotent startup step (mirroring the existing `DevAiProviderSeeder` / `DevBaselineSeeder` pattern, but running unconditionally — not dev-only, since real production credentials predate this feature) that: queries `AIProvider` rows where `CredentialCiphertext IS NOT NULL AND CredentialHint IS NULL`, calls `IAiCredentialProtector.Unprotect` per row inside a try/catch, formats and saves the hint, and logs (not throws) on a per-row decrypt failure so one bad/rotated-key-ring row cannot block application startup.

**Rationale**: `Unprotect` is already invoked today for live provider calls and health checks — this introduces no new decryption capability or exposure, just a one-time pass to populate the new column. Scoping the query to `CredentialHint IS NULL` makes repeated runs (e.g., app restarts before a successful backfill completes, or multi-instance deploys) safe and cheap (no-op once complete).

**Alternatives considered**:
- Require administrators to re-enter every existing key — rejected; explicitly called out in spec.md as unacceptable UX (FR-009, SC-004).
- A one-off manual migration script run by hand at deploy time — rejected in favor of a self-healing startup step, consistent with this codebase's existing `DevBaselineSeeder` self-healing convention (see memory: dev DB self-heals via seeders) and because this repo's deploys are manual/solo (no separate migration-runner step to hook into).

## Decision 5: Where the hint is displayed ("next to the provider name" vs. "last column")

**Decision**: Render the hint as secondary text directly beneath/next to the provider's `displayName` inside the existing **Provider** column of `AdminAiProvidersPage.tsx` (not the rightmost **Actions** column, which holds only the per-row kebab-menu button and has no room for text).

**Rationale**: The user's request contains two spatial cues that don't both point at the same cell in today's table (`Expand | Provider | Enabled | Credential | Health | Last confirmed | Actions`) — "next to the provider name" (Provider column, position 2) and "(last column)" (literally Actions, position 7, which is structurally unsuitable for a text hint). "Next to the provider name" is the more specific, literal, and actionable instruction, and satisfies both of the user's stated reasons (confirm a key exists; recognize which key it is) exactly where an administrator's eye already lands first. Treated as a reasonable default per spec.md's Assumptions, not a [NEEDS CLARIFICATION] — low-impact, trivially adjustable in review if wrong.

**Alternatives considered**: Repurpose the existing **Credential** column (currently a "Configured"/"Not configured" chip) to show the hint instead of/alongside the chip — a defensible alternative, but rejected in favor of leaving that column's existing boolean-status chip intact (it already serves User Story 1's "at a glance" need) and adding the hint where the user explicitly asked for it, next to the name.

# Data Model: Cookie Consent & Privacy Management

**Feature**: [spec.md](./spec.md) | **Research**: [research.md](./research.md)

One new Domain entity, append-only (research.md Topic 2). No changes to `ApplicationUser`
or any other existing aggregate.

## CookieConsentRecord (new entity, `src/AskLucy.Domain/Consent/CookieConsentRecord.cs`)

Inherits `BaseEntity` (`Id: Guid` v7, `CreatedAtUtc/CreatedBy`, `ModifiedAtUtc/ModifiedBy`,
`DeletedAtUtc/DeletedBy`, `RowVersion`), per constitution §5 — even though normal
application flow never updates or hard-deletes a row (append-only), inheriting `BaseEntity`
keeps this entity consistent with the GDPR-erasure hard-delete path every other
user-owned entity supports, and gets `CreatedAtUtc`/`CreatedBy` populated for free by the
existing `AuditSaveChangesInterceptor` — no separate "recorded at" field is needed.

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` (v7) | Surrogate key |
| `UserId` | `string` | FK to `ApplicationUser.Id` (string, per the same documented structural exception `UserChat.UserId` already relies on) |
| `PolicyVersion` | `string` (≤50) | The cookie/privacy policy version this decision was made under (spec.md FR-005); compared against `ICookiePolicyProvider`'s current version to compute `RequiresReconsent` (research.md Topic 3) |
| `FunctionalAccepted` | `bool` | Functional/Preferences category (spec.md FR-003) |
| `AnalyticsAccepted` | `bool` | Analytics category (spec.md FR-003) |
| `MarketingAccepted` | `bool` | Marketing category (spec.md FR-003) |
| `CreatedAtUtc` | `DateTime` | Existing `BaseEntity` audit field; doubles as "when this decision was recorded" (spec.md FR-005/FR-012/FR-016) — populated by `AuditSaveChangesInterceptor`, never set manually |

Deliberately **not** persisted: `EssentialAccepted`. Essential is a domain invariant
(always granted, never toggleable — spec.md FR-004), not a per-decision variable; the API
response layer returns a fixed `Essential = true` constant rather than storing a column
that can only ever hold one value (research.md Topic 4's YAGNI reasoning applied to the
same column-vs-constant tradeoff).

**Validation rules** (Domain, enforced in the `Create` factory):
- `UserId` required, non-blank.
- `PolicyVersion` required, non-blank.
- No mutator methods exist — the entity is immutable after creation (append-only,
  research.md Topic 2). Changing a preference means inserting a **new**
  `CookieConsentRecord`, never editing an existing one.

**Lifecycle**:

```text
(no record for user) ──first explicit decision (banner)──> CookieConsentRecord #1 (current)
CookieConsentRecord #N (current) ──user changes preferences (Settings)──> CookieConsentRecord #N+1 (current)
CookieConsentRecord #N (current) ──policy version bumps (server config change)──> still #N,
    but RequiresReconsent becomes true (computed, not stored) until a new record is created
    under the new version ──> CookieConsentRecord #N+1 (current, new version)
```

"Current" state for a user is always **the row with the latest `CreatedAtUtc` for that
`UserId`** — never mutated in place. History (spec.md FR-016 — "what was this user's
consent state on date X") is answered by querying all rows for the user ordered by
`CreatedAtUtc` and taking the latest row with `CreatedAtUtc <= X`.

## CookieConsentStatusDto (Application DTO, not persisted)

Returned by `GetMyCookieConsentQuery`; composed from the latest `CookieConsentRecord` (if
any) plus the current policy version from `ICookiePolicyProvider`.

| Field | Type | Notes |
|---|---|---|
| `HasConsented` | `bool` | `false` if the user has no `CookieConsentRecord` at all (first login) |
| `RequiresReconsent` | `bool` | `true` if `HasConsented` is `false`, OR the latest record's `PolicyVersion` != the current policy version (spec.md FR-007) — this single flag is what `ConsentGate` uses to decide whether to show the blocking banner |
| `PolicyVersion` | `string?` | The version the user's latest decision was recorded under; `null` if `HasConsented` is `false` |
| `CurrentPolicyVersion` | `string` | Always populated, from `ICookiePolicyProvider` |
| `Essential` | `bool` | Always `true` (constant, research.md Topic 4) |
| `Functional` / `Analytics` / `Marketing` | `bool` | From the latest record; `false` (safe default) if `HasConsented` is `false` |
| `LastUpdatedAtUtc` | `DateTime?` | Latest record's `CreatedAtUtc`; `null` if `HasConsented` is `false` — displayed in the Cookie Preferences panel (spec.md FR-012) |

## CookiePolicyDto (Application DTO, not persisted)

Returned by `GetCookiePolicyQuery` (the anonymous endpoint backing the Privacy Page,
research.md Topic 8).

| Field | Type | Notes |
|---|---|---|
| `Version` | `string` | From `CookiePolicyOptions.CurrentVersion` |
| `EffectiveAtUtc` | `DateTime` | From `CookiePolicyOptions.EffectiveAtUtc` |

## CookiePolicyOptions (Infrastructure configuration, not a Domain/DB entity)

Bound via `IOptions<CookiePolicyOptions>`, validated at startup (constitution §4).

| Field | Type | Notes |
|---|---|---|
| `CurrentVersion` | `string` | Bumped by whoever owns compliance content when the policy changes (research.md Topic 3) |
| `EffectiveAtUtc` | `DateTime` | Displayed on the Privacy Page |

## Entity Relationship Summary

```text
ApplicationUser 1───* CookieConsentRecord   (append-only; "current" = MAX(CreatedAtUtc) per UserId)
```

No changes to `ApplicationUser` or any other existing aggregate are required.

## Index

`(UserId, CreatedAtUtc DESC)` on `CookieConsentRecords` — every query in this feature
either looks up "the latest record for this user" or "all records for this user ordered
by time," both covered by this single composite index (constitution §5: "every column
used in a WHERE, JOIN, or ORDER BY... MUST be covered by an index").

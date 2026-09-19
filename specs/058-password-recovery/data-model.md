# Phase 1 Data Model: Password Recovery & Password Management

**Feature**: 058-password-recovery | **Date**: 2026-09-18

One new entity. No changes to `ApplicationUser`; no changes to `RefreshToken` beyond a new query
on its repository.

---

## PasswordResetToken (new)

Domain entity in `AskLucy.Domain/Authentication/PasswordResetToken.cs`, modelled directly on the
existing `RefreshToken` in the same namespace: private setters, static factory, behaviour methods,
no `BaseEntity`/soft-delete (consumed and superseded rows are audit-relevant and are kept).

| Field | Type | Notes |
|-------|------|-------|
| `Id` | `Guid` | `Guid.CreateVersion7()`, sequential for index locality |
| `UserId` | `string` | FK to `AspNetUsers.Id`, cascade delete, indexed |
| `TokenHash` | `string(64)` | SHA-256 hex of the plaintext token, via `ITokenService.Hash`. Unique index. The plaintext is never persisted |
| `EmailAtIssue` | `string` | The account's email when issued; a mismatch at redemption rejects the token (FR-006, email-change invalidation) |
| `CreatedAtUtc` | `DateTime` | UTC |
| `ExpiresAtUtc` | `DateTime` | `CreatedAtUtc + 1 hour` |
| `ConsumedAtUtc` | `DateTime?` | Set on successful redemption; non-null means spent |
| `SupersededAtUtc` | `DateTime?` | Set when a newer token is issued for the same account |
| `RequestedFromIp` | `string?` | Client origin, for the audit trail (FR-015) |

### Derived state

```text
IsRedeemable => ConsumedAtUtc is null
             && SupersededAtUtc is null
             && DateTime.UtcNow < ExpiresAtUtc
```

### State transitions

```text
                 issued
                   │
                   ▼
              ┌─────────┐   redeem (valid)    ┌──────────┐
              │ Pending │ ──────────────────► │ Consumed │  (terminal)
              └─────────┘                     └──────────┘
                │      │
    new token   │      │  1 hour elapses
    issued for  │      │
    same user   ▼      ▼
         ┌────────────┐  ┌─────────┐
         │ Superseded │  │ Expired │   (both terminal; both reject redemption)
         └────────────┘  └─────────┘
```

A password change through any path (reset, change-while-signed-in) supersedes every Pending token
for that account. `Expired` is derived from the clock, not stored, so no background job is needed
to move rows into it.

### Validation rules

- `TokenHash` must be exactly 64 hex characters; enforced in the factory, not only by the column.
- Redemption requires `IsRedeemable` **and** `EmailAtIssue` equal to the account's current
  email, compared case-insensitively.
- At most one Pending token per user is an invariant maintained by the issue path (it supersedes
  first, then inserts, inside one transaction), not by a database constraint — a filtered unique
  index would make a legitimate concurrent re-request fail with a constraint error rather than
  the second request simply winning.

### Retention

Consumed, superseded and expired rows are kept for 90 days for the audit trail, then removed by the
existing scheduled cleanup infrastructure. Retention is a task in `tasks.md`, not a migration.

---

## Entities reused unchanged

- **ApplicationUser** — `PasswordHash` (may be null for external-only accounts, which US4 fills),
  `EmailConfirmed` (gates reset eligibility), `LockoutEnd`, `TwoFactorEnabled`.
- **RefreshToken** — the session record revoked in bulk by FR-009/FR-010. Its repository gains
  `ListActiveByUserAsync(string userId, CancellationToken)`; the entity itself is untouched.

## Migration

One EF Core migration, `AddPasswordResetTokens`: creates `PasswordResetTokens` with the columns
above, a unique index on `TokenHash`, and a non-unique index on `(UserId, CreatedAtUtc DESC)` to
serve both the supersede-on-issue write and the per-email throttle read. No data backfill. The
migration file must be saved without a BOM and with the repo's line-ending convention, per the
CI gotchas already recorded for this repository.

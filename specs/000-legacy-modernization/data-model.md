# Phase 1 Data Model: Legacy Application Modernization & Technology Stack Migration

Scope: only the entities needed to satisfy `spec.md`'s Key Entities section and Functional Requirements. No Conversation/Message/KnowledgeBase/Agent/Payment aggregates are introduced — those are future scope (SPEC-002+).

## ApplicationUser (existing, migrated in place)

The ASP.NET Identity user, extended with the application's existing custom fields.

| Field | Type | Notes |
|---|---|---|
| `Id` | `string` (Identity default) | Unchanged — already an effectively-GUID key today; no migration needed. |
| `Email`, `UserName`, `PasswordHash`, `SecurityStamp`, etc. | Identity-standard | Unchanged. Never serialized in any API response (FR-019). |
| `FirstName` | `string?` | Preserved as-is (FR-014). |
| `LastName` | `string?` | Preserved as-is (FR-014). |
| `BirthDate` | `DateOnly` | Preserved as-is (FR-014). |
| `AvatarFileName` | `string?` | **New**, replaces `ProfilePicture` (`byte[]?`). Stores the on-disk file name under the server-side avatars directory (FR-025). |
| `TwoFactorEnabled`, external-login rows (`AspNetUserLogins`) | Identity-standard | Unchanged (FR-010, FR-011). |

**Migration note**: existing `ProfilePicture` BLOBs are written to disk as files and `AvatarFileName` is populated in the same migration/data-fix pass that drops the `ProfilePicture` column; zero users lose their picture (SC-009).

**Validation rules**: unchanged from today (Identity's own password/email rules). No new validation introduced.

**Domain events**: none new. (`UserRegistered`, `EmailVerified` etc. from `docs/ENTITY_MODEL.md` are available conventions but not required to satisfy any FR in this migration; only introduced if a handler actually needs to react to them.)

## UserChat (existing, migrated onto standard conventions)

| Field | Type (before → after) | Notes |
|---|---|---|
| `Id` | `int` identity → `Guid` (`uniqueidentifier`, sequential) | See `research.md` Topic 5 for the migration approach. |
| `Title` | `string` (required) | Unchanged (FR-008). |
| `SessionId` | `string?` | Unchanged. |
| `UserId` | `string` (FK → `ApplicationUser.Id`) | Unchanged relationship; cascade delete preserved. |
| `CreationDateTime` → `CreatedAtUtc` | `DateTime?` (local) → `DateTime` (UTC, non-null) | Converted to UTC and made non-nullable per convention; existing rows backfilled by converting their existing local-time value with the server's known time zone at write time. |
| `LastAccessDateTime` → `ModifiedAtUtc` | `DateTime?` (local) → `DateTime?` (UTC) | Same conversion. |
| `CreatedBy` | `string` (new) | Backfilled to `UserId` for existing rows (the creator is always the owner today). |
| `ModifiedBy` | `string?` (new) | Null for existing rows (no prior modification actor recorded). |
| `DeletedAtUtc` / `DeletedBy` | `DateTime?` / `string?` (new) | Null for all existing rows; populated only when a user deletes a chat (FR-033, soft delete via global query filter). |
| `RowVersion` | `byte[]` (new, concurrency token) | Initialized on first save after migration. |

**Lifecycle**: Create (FR-008) → Rename (FR-033, updates `Title`/`ModifiedAtUtc`/`ModifiedBy`) → Delete (FR-033, sets `DeletedAtUtc`/`DeletedBy`, excluded from queries by the global soft-delete filter; not physically removed). No "restore" capability is introduced — none exists today and none was requested.

**Validation rules**: `Title` required, matching today's behavior (no new length/format constraint introduced beyond what FluentValidation needs to prevent an empty string).

## RefreshToken (new)

Purely an authentication-infrastructure entity — not a user-facing capability.

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | Surrogate key. |
| `UserId` | `string` (FK → `ApplicationUser.Id`) | Owner. |
| `TokenHash` | `string` | The refresh token is never stored in plaintext (constitution §8/§23). |
| `TokenFamilyId` | `Guid` | Groups a rotation chain; reuse of a revoked token revokes the whole family (`docs/SECURITY.md` §8). |
| `ExpiresAtUtc` | `DateTime` | 14 days from issuance (`research.md` Topic 1). |
| `RevokedAtUtc` | `DateTime?` | Set on rotation, logout, or reuse-detection. |
| `CreatedAtUtc` | `DateTime` | Standard audit field. |

**State transitions**: `Active` → `Rotated` (a new row created, this one's `RevokedAtUtc` set) → `Revoked` (logout, reuse detected, or family revoked). No soft-delete needed — revoked tokens are audit-relevant and kept, not deleted.

## Role / UserRole (existing, unchanged data)

The existing `AspNetRoles`/`AspNetUserRoles` rows ("Super User", "Administrator") are unchanged. No new roles are introduced. What changes is enforcement: every Control Panel/user-management action now requires a policy-based `[Authorize]` check against these existing role assignments (FR-017), where today only the Razor view checks `User.IsInRole(...)`.

## Explicitly out of scope for this data model

Per `spec.md` § Key Entities: `Conversations`, `Messages`, `MessageAttachments`, `KnowledgeBases`, `Documents`, `Embeddings`, `Agents`, `MCPServers`, `PromptTemplates`, `UserSubscriptions`. None of these tables, and none of the fields that would only make sense in support of them, are created by this migration.

## Relationships

```text
ApplicationUser
 ├──── UserChats (1:N, cascade delete, soft-deleted not hard-deleted)
 └──── RefreshTokens (1:N, revoked not deleted)
```

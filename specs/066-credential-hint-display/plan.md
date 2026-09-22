# Implementation Plan: Credential Hint Display

**Branch**: `066-credential-hint-display` | **Date**: 2026-09-22 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/066-credential-hint-display/spec.md`

## Summary

Compute a vendor-style, non-reversible hint (`first4...last4`, or a fully masked placeholder for short keys) from an AI provider's API key at the moment it is set, persist it alongside — not instead of — the existing encrypted credential, expose it through the admin provider-list contract, and render it next to the provider's display name in the AI Providers admin table. Pre-existing credentials are backfilled once via an idempotent startup pass that reuses the same decryption capability already used for live provider calls.

## Technical Context

**Language/Version**: C# / .NET 10 (backend), TypeScript 5 / React 19 (frontend, Vite)

**Primary Dependencies**: ASP.NET Core, EF Core, MediatR, `Microsoft.AspNetCore.DataProtection` (via existing `IAiCredentialProtector`); MUI, TanStack Query (frontend) — no new packages.

**Storage**: SQL Server — one new nullable column (`CredentialHint`) on the existing `AIProviders` table.

**Testing**: xUnit (Application/Domain unit tests, Web.Tests integration tests), Vitest + React Testing Library (frontend).

**Target Platform**: Existing ASP.NET Core Web API + React SPA (unchanged).

**Project Type**: Web application (existing Clean Architecture solution: Domain / Application / Infrastructure / Persistence / Web).

**Performance Goals**: N/A beyond existing provider-list endpoint's current characteristics — the hint is a stored column read alongside existing fields, no new per-request computation or decryption.

**Constraints**: The hint MUST be derived from the plaintext key only at write time (set/replace) or during the one-time backfill; it MUST NEVER be recomputed by decrypting `CredentialCiphertext` on a normal read path (data-model.md `CredentialCiphertext` invariant: read projections never touch it).

**Scale/Scope**: 4 built-in providers today, unbounded but small (admin-configured rows only) — no scale concerns.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **§8 Security — "Secrets never live in... client bundles."** The full API key remains server-only; only a deliberately shortened, one-way-derived hint (never long enough to reconstruct the key, matching OpenAI's/Anthropic's own console UX) is returned to the client. This is a conscious, user-directed trade-off (explicit request, with the full-key alternative explicitly rejected) rather than an oversight — documented here per the constitution's requirement to justify any security-relevant deviation, not to silently allow it. **PASS with documented rationale.**
- **§5 Database Principles — Migrations.** One additive, nullable, reversible column (`CredentialHint`) on an existing table — a single logical change, one migration, trivial `Down` (drop column). **PASS.**
- **§1 Clean Architecture / Dependency Rule.** Hint computation is a pure string transform that only ever sees plaintext already present in the Application-layer command handler (same place `IAiCredentialProtector.Protect` is already called) — no new Domain→Infrastructure dependency, no Domain layer awareness of formatting. **PASS.**
- **§8 Security — Audit logs / least privilege.** No new audit-relevant action is introduced (hint changes are a side effect of the already-logged `SetCredential`/`ClearCredential` actions). **PASS.**

No violations requiring Complexity Tracking.

## Project Structure

### Documentation (this feature)

```text
specs/066-credential-hint-display/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md         # Phase 1 output
├── quickstart.md         # Phase 1 output
├── contracts/
│   └── admin-ai-providers-hint.md
└── tasks.md              # Phase 2 output (/speckit-tasks)
```

### Source Code (repository root)

```text
src/AskLucy.Domain/Ai/
└── AIProvider.cs                          # + CredentialHint property, SetCredential/ClearCredential signature change

src/AskLucy.Application/Ai/
├── AdminAiProviderDto.cs                  # + CredentialHint field
├── CredentialHintFormatter.cs             # NEW — pure static first4...last4 / masked formatter
└── Commands/
    ├── SetAiProviderCredential/SetAiProviderCredentialCommandHandler.cs   # computes hint before Protect()
    └── ClearAiProviderCredential/ClearAiProviderCredentialCommandHandler.cs

src/AskLucy.Persistence/
├── Configurations/AIProviderConfiguration.cs      # + CredentialHint column mapping
└── Migrations/{timestamp}_AddAiProviderCredentialHint.cs                   # NEW

src/AskLucy.Web/
└── (no controller/route changes — existing GET providers / PUT|DELETE credential endpoints unchanged in shape, only response payload gains a field)

src/AskLucy.Web/ClientApp/src/features/admin/
├── api/adminAiProvidersApi.ts             # + credentialHint field on AdminAiProvider
├── pages/AdminAiProvidersPage.tsx         # render hint next to provider display name
└── pages/AdminAiProvidersPage.test.tsx    # (if present) or table-level test coverage
```

**Structure Decision**: Existing single-solution Clean Architecture layout (Domain → Application → Persistence/Infrastructure → Web/ClientApp) — no new projects. This feature is a narrow vertical slice through the already-established AI Provider admin path (same one spec 005 and spec 065 both used).

## Complexity Tracking

*No violations — table intentionally omitted.*

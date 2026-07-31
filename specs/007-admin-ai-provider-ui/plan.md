# Implementation Plan: Admin AI Provider Configuration UI

**Branch**: `007-admin-ai-provider-ui` | **Date**: 2026-07-31 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/007-admin-ai-provider-ui/spec.md`

## Summary

Administrators currently have no way to enable an AI provider or configure its credential
— the backend capability was fully delivered under `005-multi-provider-ai-engine`
(`AdminAiProvidersController`: `GET/PATCH /api/v1/admin/ai/providers`, `PUT/DELETE
/api/v1/admin/ai/providers/{id}/credential`), but no page in the product calls it. This
plan adds exactly that missing page: a new `AdminAiProvidersPage` under the existing admin
section, following the established `AdminUsersPage`/`UserActionMenu` list-plus-row-actions
pattern, with every state-changing action (enable, disable, set credential, clear
credential) gated behind an explicit confirmation dialog per the spec's clarified FR-010.
This is a **frontend-only** change — no backend code is added or modified.

## Technical Context

**Language/Version**: TypeScript 5 / React 19 (existing `AskLucy.Web/ClientApp` SPA). No backend language/runtime involved — no backend changes.

**Primary Dependencies**: MUI (Material UI) v9 (`Table`, `Dialog`, `Chip`, `TextField`), TanStack Query v5 (`useQuery`/`useMutation`), React Router — all already in use by the sibling `AdminUsersPage`/`AdminDashboardPage`.

**Storage**: N/A — no new persistence. Reuses the `AIProvider` data already stored and exposed by the existing `AdminAiProvidersController` from spec 005.

**Testing**: Vitest + React Testing Library, matching `UserActionMenu.test.tsx`'s confirm-dialog test pattern, plus an automated a11y check (`*.a11y.test.tsx`, matching `AdminDashboardPage.a11y.test.tsx`).

**Target Platform**: Web (existing admin-only area of the product's single-page app).

**Project Type**: Web application — this is an additive feature slice inside the existing `features/admin` folder of `AskLucy.Web/ClientApp`; no new project is created.

**Performance Goals**: No new performance budget — matches SC-005 (state changes reflected with no perceptible delay beyond normal navigation), consistent with the existing admin pages' behavior.

**Constraints**: Admin-only, enforced both client-side (`AdminRoute`) and server-side (already-existing `[Authorize(Policy = "AdministratorOrSuperUser")]` on `AdminAiProvidersController`); a provider credential's value MUST never be requested from, or rendered by, the client — already structurally guaranteed by the existing `AdminAiProviderDto` response shape, which has no credential-value field at all.

**Scale/Scope**: A small, bounded, administrator-curated list (today: 4 seeded providers) — not a paginated/high-volume list; no virtualization or server-side paging needed.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Gate | Assessment |
|---|---|
| I. Clean Architecture & Dependency Rule | PASS — no backend change; frontend calls the existing versioned REST API only, same as every other admin page. |
| II–IV. SOLID / Simplicity / Composition | PASS — reuses `AdminUsersPage`'s table shape and `UserActionMenu`'s confirm-dialog composition rather than introducing a new UI pattern (no new abstraction invented for a single feature). |
| V. Dependency Inversion & Testability | PASS — frontend component depends on a thin `adminAiProvidersApi.ts` module (mirrors `adminApi.ts`), swappable/mockable in tests exactly like the existing admin API modules. |
| VI. Separation of Concerns | PASS — all business rules (credential-required-to-enable, clear-credential-auto-disables) already live server-side in Domain (`AIProvider.Enable/ClearCredential`); the new UI only orchestrates confirmation + calls, never re-implements the rule. |
| VII. Convention Over Configuration | PASS — reuses the established admin list/detail/confirm-dialog convention; no new navigational or state-management pattern introduced. |
| VIII. No Silent Failures (NON-NEGOTIABLE) | PASS (design requirement) — every mutation (enable/disable/set/clear) MUST surface success/error via visible UI feedback (Snackbar/inline), not just `onSuccess`/console; enforced in Phase 1 design and verified in tests. |
| §7 UI Principles | PASS — MUI theme only, WCAG AA via an a11y test file, responsive `Table`/`Dialog`, no hardcoded colors. Dialog state uses local `useState`, following `UserActionMenu.tsx`'s existing precedent rather than §7's literal "dialogs... live in Zustand stores" wording — an accepted pre-existing pattern in this codebase, not a new deviation introduced here; flagged for awareness, not blocking. |
| §6 API Standards | PASS — zero new endpoints; consumes the existing versioned, rate-limited (`admin-endpoints`), Problem-Details-compliant contract from spec 005 as-is. |
| §8 Security | PASS — admin-only enforced at both layers already; credential value is structurally never in any response the client receives. |
| §10 Testing | Design requirement — component test (confirm-dialog gating, per FR-010) and a11y test required in the same change, per this constitution's "tests in the same PR" rule. |

No violations. **Complexity Tracking is not needed** — this feature introduces no new architectural layer, dependency, or pattern.

**Post-Phase 1 re-check**: data-model.md, contracts/admin-ai-providers.md, and
quickstart.md confirm no backend endpoint, entity, or contract changes were introduced
during design — every gate above still holds unchanged.

## Project Structure

### Documentation (this feature)

```text
specs/007-admin-ai-provider-ui/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md         # Phase 1 output
├── quickstart.md         # Phase 1 output
├── contracts/            # Phase 1 output
│   └── admin-ai-providers.md
└── tasks.md              # Phase 2 output (/speckit-tasks — not created by this command)
```

### Source Code (repository root)

This feature is entirely frontend-additive within the existing single web application
(`AskLucy.Web` backend + `AskLucy.Web/ClientApp` React SPA). No backend project is touched.

```text
src/AskLucy.Web/ClientApp/src/
├── features/admin/
│   ├── api/
│   │   └── adminAiProvidersApi.ts          # NEW — thin fetch wrappers over the existing AdminAiProvidersController endpoints (mirrors adminApi.ts)
│   ├── components/
│   │   ├── AiProviderActionsMenu.tsx       # NEW — per-row enable/disable/set-credential/clear-credential actions, each confirm-gated (mirrors UserActionMenu.tsx)
│   │   └── AiProviderActionsMenu.test.tsx  # NEW
│   └── pages/
│       ├── AdminAiProvidersPage.tsx        # NEW — list page (mirrors AdminUsersPage.tsx)
│       ├── AdminAiProvidersPage.a11y.test.tsx  # NEW
│       └── AdminDashboardPage.tsx          # MODIFIED — add a "Manage AI providers" nav button (mirrors the existing "Manage users" button)
└── routes/
    └── router.tsx                          # MODIFIED — add the `/admin/ai-providers` route, wrapped in ProtectedRoute + AdminRoute like /admin/users
```

**Structure Decision**: Additive feature slice inside the existing `features/admin` folder,
following the same file layout `001-admin-dashboard` already established (`api/` +
`components/` + `pages/` per feature). No new top-level directory, no backend project
touched — the backend capability this UI calls was delivered complete under
`005-multi-provider-ai-engine`.

## Complexity Tracking

*No violations — table intentionally omitted.*

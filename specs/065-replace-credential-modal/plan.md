# Implementation Plan: Replace Credential Modal Polish

**Branch**: `065-replace-credential-modal` | **Date**: 2026-09-22 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/065-replace-credential-modal/spec.md`

**Note**: This template is filled in by the `/speckit-plan` command; its definition describes the execution workflow.

## Summary

Widen the shared "Set credential" / "Replace credential" dialog in `AiProviderActionsMenu.tsx`, add placeholder text so the (always-reset-to-empty) API key field never reads as pre-filled, and add a show/hide toggle inside the field that only appears once the administrator has typed a character. One component already serves all four providers, so the change is made once and applies everywhere by construction.

## Technical Context

**Language/Version**: TypeScript (strict), React 18/19, per repo `ClientApp`

**Primary Dependencies**: MUI (`Dialog`, `TextField`, `InputAdornment`, `IconButton`, `@mui/icons-material` `Visibility`/`VisibilityOff`)

**Storage**: N/A — no persisted state changes; the field's value only ever lives in component state until submit

**Testing**: Vitest + React Testing Library (`AiProviderActionsMenu.test.tsx`), `fireEvent`/`screen` per existing file's MUI-Popper jsdom workaround

**Target Platform**: Web (admin panel), desktop and mobile viewports

**Project Type**: Web application — this feature touches `src/AskLucy.Web/ClientApp` only

**Performance Goals**: N/A — presentational change, no measurable performance target

**Constraints**: Must not change the `setCredential`/`clearCredential` API contracts or any typed API payload; dialog must remain within MUI's responsive breakpoints on narrow viewports (FR-001, SC-004)

**Scale/Scope**: Single shared component (`AiProviderActionsMenu.tsx`), used identically by 4 providers today (Anthropic, Google Gemini, OpenAI, OpenRouter) and any added later (FR-009)

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **I. Clean Architecture & Dependency Rule** — N/A. No new dependency arrows; this is a leaf React component with no Domain/Application/Infrastructure involvement.
- **II–VII (SOLID, DRY/KISS/YAGNI, composition, DI, SOC, convention)** — Pass. The dialog is already a single shared component (satisfies DRY/FR-009 by construction); the toggle is local `useState`, no new abstraction layer needed for two booleans.
- **VIII. No Silent Failures (NON-NEGOTIABLE)** — N/A to this feature's own scope: this change touches only display/masking of an in-progress, unsubmitted value. No new async operation, request, or promise is introduced; existing error handling (`onError` → `Snackbar`) for the actual submit path is untouched.
- **TypeScript coding standards (§4)** — `strict` stays enabled, no `any` introduced.

No violations. No entries needed in Complexity Tracking.

Gate re-checked post-design (Phase 1): still passes — `data-model.md` confirms no new persisted entity, `quickstart.md` describes a pure UI verification with no backend interaction.

## Project Structure

### Documentation (this feature)

```text
specs/065-replace-credential-modal/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output (/speckit-plan command)
├── data-model.md        # Phase 1 output (/speckit-plan command)
├── quickstart.md        # Phase 1 output (/speckit-plan command)
└── tasks.md             # Phase 2 output (/speckit-tasks command - NOT created by /speckit-plan)
```

No `contracts/` directory — this feature has no external interface (no new/changed API endpoint, no new payload shape).

### Source Code (repository root)

```text
src/AskLucy.Web/ClientApp/src/features/admin/components/
├── AiProviderActionsMenu.tsx        # the credential dialog lives here — only file with behavior changes
└── AiProviderActionsMenu.test.tsx   # existing tests extended for placeholder/toggle/width behavior
```

**Structure Decision**: Existing web application structure (`src/AskLucy.Web/ClientApp`), no new directories. This is a scoped edit to one existing component and its co-located test file — the dialog is already shared by all four provider rows via the `provider` prop, so there is no per-provider file to touch.

## Complexity Tracking

*No entries — Constitution Check reported no violations.*

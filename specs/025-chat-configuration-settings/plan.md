# Implementation Plan: Chat Configuration in User Settings

**Branch**: `025-chat-configuration-settings` | **Date**: 2026-08-17 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/025-chat-configuration-settings/spec.md`

## Summary

Move advanced chat controls out of the Flumeria Studio workspace toolbar into User
Settings, without touching the two settings tabs that already hold most of that
configuration. "AI Providers" and "Voice" stay exactly as they are; a new **Chat
Configuration** hub tab links to both and additionally hosts a small, genuinely new
control — changing the AI model of the conversation the user currently has open, which
previously lived inline in the chat toolbar and has no equivalent home in the unchanged AI
Providers tab (that tab only sets the default for *new* conversations). A new, separate
**Chat History** tab hosts the relocated conversation list (search/filter/sort/pin/
favorite/archive/duplicate/export/delete), unrelated to Chat Configuration. The chat
toolbar loses the live provider/model switcher and the conversation-history panel, but
gains a direct "New chat" action so starting a conversation stays workspace-native. The
only backend change is one new minimal read endpoint (`GET /api/v1/chats/{id}`) to expose
the current conversation's provider/model, which is already persisted but was never
queryable; the existing write endpoint, and every other chat endpoint, is reused unchanged.

## Technical Context

**Language/Version**: C# 13 / .NET 10 (backend, existing solution — unchanged); TypeScript 5 / React 19 (frontend, existing Vite SPA — unchanged)

**Primary Dependencies**: ASP.NET Core, MediatR, FluentValidation, EF Core (backend — all existing, no new packages); React, MUI, TanStack Query, Zustand, React Router, React Hook Form (frontend — all existing, no new packages)

**Storage**: SQL Server via EF Core — reuses the existing `UserChats` table and its already-persisted `ProviderId`/`ModelId` columns (`src/AskLucy.Domain/Chats/UserChat.cs`); zero new tables, zero new migrations (data-model.md)

**Testing**: xUnit v3 + FluentAssertions + NSubstitute (backend, `tests/AskLucy.*.Tests`); Vitest + React Testing Library + MSW + jest-axe (frontend, `ClientApp/src/**/*.test.tsx`) — both stacks already in place, no new tooling

**Target Platform**: Existing ASP.NET Core Web API + React SPA (web browser); no hosting/deployment change

**Project Type**: Web application — existing multi-project Clean Architecture backend (`AskLucy.Domain` / `AskLucy.Application` / `AskLucy.Persistence` / `AskLucy.Web`) plus an existing separate SPA frontend (`src/AskLucy.Web/ClientApp`)

**Performance Goals**: No new performance-sensitive path; the new `GET /api/v1/chats/{id}` is a single indexed primary-key lookup, expected in line with the controller's other single-chat routes (no stated p95 budget beyond existing norms)

**Constraints**: Zero new backend tables/migrations; "AI Providers" and "Voice" tabs must remain behaviorally unmodified (research.md Decision 4); every relocated/newly-hosted control must keep its exact existing save/persist semantics (spec FR-014); no new routing structure for `/settings` or `/studio` beyond a `location.state`-driven initial tab (research.md Decision 4)

**Scale/Scope**: One new backend query + one new controller action; one new client-only session store; two new Settings tabs (Chat Configuration, Chat History); toolbar control removal (2 components) plus one added "New chat" toolbar action; two account-menu entries added to two existing, already-parallel menu components (`UserMenu.tsx`, `workspaceControls.tsx`)

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Gate (constitution §) | Status | Notes |
|---|---|---|
| Clean Architecture & Dependency Rule (§2.I, §3) | PASS | New `GetChatByIdQuery`/Handler lives in `Application`, depends only on `Domain` + the existing `IUserChatRepository` abstraction it already defines; new controller action in `Api` calls it via `ISender`, no direct repository/EF access from the controller. |
| SOLID (§2.II) | PASS | New handler has one reason to change (fetch-and-map a chat's own detail); reuses the existing `ChatOwnershipGuard` rather than re-implementing authorization. |
| Simplicity First — DRY/KISS/YAGNI (§2.III) | PASS | Explicitly rejected route-per-tab and a duplicate `/chats/history` endpoint (research.md Decisions 3–4) in favor of reusing existing patterns; no speculative generalization introduced. |
| Dependency Inversion & Testability (§2.V) | PASS | New handler depends on `IUserChatRepository` (already an interface); unit-testable with the same NSubstitute-faked repository pattern used by `UpdateChatModelSelectionCommandHandler`. |
| Separation of Concerns (§2.VI) | PASS | Ownership/ authorization logic stays in the Application-layer guard, not the controller; new React components contain no business rules beyond presentation/navigation. |
| Convention over Configuration (§2.VII) | PASS | New query follows the existing per-query folder convention (`Application/Chats/Queries/<Name>/`); new Settings tabs follow the existing tab-index convention rather than introducing routing. |
| No Silent Failures (§2.VIII) | PASS (design requirement carried into tasks) | New `GET` call and the reused `PATCH` call must surface errors via TanStack Query's error state in the current-conversation control, matching `AiProvidersTab`'s existing error-`Alert` pattern — no console-only failures. |
| CQRS rules (§3) | PASS | `GetChatByIdQuery` is read-only, returns only `Id/Title/ProviderId/ModelId` — no unrelated data. |
| REST conventions / API standards (§6) | PASS | `GET /api/v1/chats/{id}` is a standard resource-by-id route on the existing `/chats` resource; inherits the controller's existing `[Authorize]` and `[EnableRateLimiting("chat-endpoints")]`; errors via existing Problem Details middleware. |
| Database Principles (§5) | PASS (trivially) | No schema change, no migration — reads an already-indexed FK column. |
| UI Principles (§7) | PASS (design requirement carried into tasks) | New tabs must meet WCAG 2.1 AA via the existing `jest-axe` pattern already applied to `SettingsPage`; state split follows §7 (Zustand for the new session-scoped `activeConversationStore`, TanStack Query for the new `GET` fetch — mirroring `useAiPreferences`). |
| Testing Standards (§10) | PASS (design requirement carried into tasks) | New handler needs an xUnit unit test; relocated/new components need Vitest + a11y coverage, per existing sibling test files (`AiProvidersTab.test.tsx`, `SettingsPage.a11y.test.tsx`). |

No violations identified. Complexity Tracking table below is not needed.

## Project Structure

### Documentation (this feature)

```text
specs/025-chat-configuration-settings/
├── plan.md                          # This file
├── research.md                      # Phase 0 output
├── data-model.md                    # Phase 1 output
├── quickstart.md                    # Phase 1 output
├── contracts/
│   ├── chat-detail-api.md           # Phase 1 output — new GET /chats/{id}
│   └── settings-navigation.md       # Phase 1 output — Settings tab/menu UI contract
├── checklists/
│   └── requirements.md              # /speckit-specify + /speckit-clarify output
└── tasks.md                         # Phase 2 output (/speckit-tasks — not created here)
```

### Source Code (repository root)

This feature touches the existing Clean Architecture backend solution and the existing
separate React SPA frontend; no new projects are created.

```text
src/
├── AskLucy.Domain/
│   └── Chats/
│       └── UserChat.cs                          # unchanged — ProviderId/ModelId already exist
├── AskLucy.Application/
│   └── Chats/
│       ├── Commands/UpdateChatModelSelection/    # unchanged — reused as-is
│       └── Queries/
│           ├── SearchUserChats/                  # unchanged — reused by Chat History tab
│           └── GetChatById/                      # NEW — GetChatByIdQuery/Handler/ChatDetailDto
├── AskLucy.Persistence/
│   └── Configurations/UserChatConfiguration.cs   # unchanged
└── AskLucy.Web/
    ├── Controllers/v1/ChatsController.cs         # + one new GET action
    └── ClientApp/src/
        ├── components/
        │   └── UserMenu.tsx                      # + 2 menu entries
        ├── features/
        │   ├── chat/
        │   │   ├── activeConversationStore.ts     # NEW
        │   │   ├── workspaceControls.tsx          # + 2 menu entries (useAccountControl)
        │   │   ├── api/chatsApi.ts                # + getChatById() call
        │   │   ├── pages/ChatPage.tsx             # toolbar: remove 2 controls, add "New chat"
        │   │   └── components/
        │   │       ├── ProviderModelSelector.tsx   # relocated into Chat Configuration hub
        │   │       ├── ConversationSwitcher.tsx    # removed from workspace toolbar
        │   │       └── ChatSidebar.tsx              # ConversationList relocated into Chat History tab
        │   └── settings/
        │       └── pages/
        │           ├── SettingsPage.tsx            # + 2 tabs, location.state-seeded tab index
        │           ├── ChatConfigurationTab.tsx     # NEW — hub: current-conversation control + 2 entry-point links
        │           └── ChatHistoryTab.tsx           # NEW — hosts relocated ConversationList

tests/
├── AskLucy.Application.Tests/Chats/GetChatByIdQueryHandlerTests.cs   # NEW unit test
└── (ClientApp) src/features/settings/pages/
    ├── ChatConfigurationTab.test.tsx                       # NEW
    ├── ChatConfigurationTab.a11y.test.tsx                  # NEW
    ├── ChatHistoryTab.test.tsx                             # NEW
    └── ChatHistoryTab.a11y.test.tsx                        # NEW
```

**Structure Decision**: Existing Clean Architecture backend (`AskLucy.Domain` /
`AskLucy.Application` / `AskLucy.Persistence` / `AskLucy.Web`) and existing React SPA
(`src/AskLucy.Web/ClientApp`) are extended in place — no new top-level projects. The single
backend addition (`GetChatById` query) follows the existing per-query folder convention in
`AskLucy.Application/Chats/Queries/`. The frontend addition follows the existing
`src/features/<domain>` convention (`chat/` and `settings/`), reusing components
(`ProviderModelSelector`, `ConversationList`) by relocation rather than rewriting them.

## Post-Design Constitution Re-Check

Re-evaluated after Phase 1 (data-model.md, contracts/, quickstart.md): no new violations
introduced. The one backend addition (`GetChatByIdQuery`) stayed within the smallest
possible surface (id/title/providerId/modelId only, per research.md Decision 2's rejection
of extending the list-query DTO instead); the frontend design reuses three existing
components by relocation (`ProviderModelSelector`, `ConversationList`, the Settings
tab-index pattern) rather than introducing parallel implementations. All gates in the table
above remain PASS.

## Complexity Tracking

> Not applicable — Constitution Check has no unresolved violations.

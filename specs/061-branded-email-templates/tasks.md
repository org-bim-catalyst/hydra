---

description: "Task list for 061-branded-email-templates"
---

# Tasks: Branded Transactional Email Templates

**Input**: Design documents from `/specs/061-branded-email-templates/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/email-template-renderer.md](./contracts/email-template-renderer.md)

**Tests**: Included and **not optional** — constitution §10 mandates unit coverage for changed/new behavior, and this feature changes the observable output of five existing email-sending code paths plus both `IEmailSender` implementations.

**Organization**: Grouped by user story. US1 (P1) is the MVP — the shared branded renderer wired to all five emails. US2 (P1) layers dark-mode legibility onto the same renderer. US3 (P2) hardens the single-CTA/safety-note guarantees with dedicated tests across all five content variants.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: US1–US3, mapping to the user stories in [spec.md](./spec.md)

## Path Conventions

Backend Clean Architecture under `src/AskLucy.Application/` and `src/AskLucy.Infrastructure/`; backend tests under `tests/AskLucy.Application.Tests/` and `tests/AskLucy.Infrastructure.Tests/`. No frontend paths — this feature has no UI surface. Per [plan.md](./plan.md) § Project Structure.

---

## Phase 1: Setup

**Purpose**: Nothing to scaffold — this feature extends an existing vertical (`Application/Abstractions` + `Infrastructure/Email`) with no new project or dependency (plan.md Technical Context).

*(No tasks — proceed directly to Phase 2.)*

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The renderer contract, content model, and multipart-capable transport signature every user story builds on. **No user story can start until this phase is done.**

- [X] T001 Define `IEmailTemplateRenderer`, `AccountEmailContent`, and `EmailAction` in `src/AskLucy.Application/Abstractions/IEmailTemplateRenderer.cs` per [contracts/email-template-renderer.md](./contracts/email-template-renderer.md) and [data-model.md](./data-model.md) — `Render(AccountEmailContent) -> (string HtmlBody, string TextBody)`, with `AccountEmailContent` carrying `Subject`, `PreheaderText`, `Heading`, `Greeting`, `BodyParagraphs`, `PrimaryAction`, `SafetyNote`, `FooterNote`, and `EmailAction` carrying `Label`/`Url`
- [X] T002 Extend `IEmailSender.SendAsync` in `src/AskLucy.Application/Abstractions/IEmailSender.cs` to accept a `string textBody` parameter alongside `htmlBody` (contracts/email-template-renderer.md)
- [X] T003 [P] Update `SmtpEmailSender.SendAsync` in `src/AskLucy.Infrastructure/Email/SmtpEmailSender.cs` to send a multipart message — set both `BodyBuilder.HtmlBody` and `BodyBuilder.TextBody` from the new `textBody` parameter
- [X] T004 [P] Update `ConsoleEmailSender.SendAsync` in `src/AskLucy.Infrastructure/Email/ConsoleEmailSender.cs` to log both bodies (extend the `[LoggerMessage]` template with `{TextBody}`)

**Checkpoint**: `dotnet build` clean (T001–T004 only add the interface/model/DI-independent transport changes; DI registration of `BrandedAccountEmailTemplateRenderer` is deferred to T005a in Phase 3, since that type doesn't exist until T011).

---

## Phase 3: User Story 1 — Recognizable, on-brand email at every account touchpoint (Priority: P1) 🎯 MVP

**Goal**: All five account emails render through one shared, Flumeria-branded HTML shell instead of five independent inline-HTML strings.

**Independent test**: Trigger each of the five emails (quickstart.md steps 1–5) and confirm identical header wordmark, color treatment, typography, and footer across all five.

### Tests for User Story 1

- [X] T006 [P] [US1] Write `BrandedAccountEmailTemplateRendererTests` in `tests/AskLucy.Infrastructure.Tests/Email/BrandedAccountEmailTemplateRendererTests.cs` — asserts the Flumeria brand wordmark/header, consistent footer, and shared layout appear for every `AccountEmailContent` variant; asserts `HtmlBody` and `TextBody` both carry the heading, every body paragraph, and the safety note; asserts the primary action's label and URL appear in both bodies when `PrimaryAction` is set, and no action markup appears when it is null (password-changed notice)
- [X] T007 [P] [US1] Update `RegisterCommandHandlerTests` in `tests/AskLucy.Application.Tests/Authentication/RegisterCommandHandlerTests.cs` for the new `IEmailTemplateRenderer` + extended `IEmailSender.SendAsync` collaboration (fake/mock both, assert the content model passed to the renderer carries the confirmation link and encoded display name)
- [X] T008 [P] [US1] Update `RequestEmailChangeCommandHandlerTests` in `tests/AskLucy.Application.Tests/Authentication/RequestEmailChangeCommandHandlerTests.cs` for the same new collaboration
- [X] T009 [P] [US1] Write `AccountEmailJobTests` in `tests/AskLucy.Infrastructure.Tests/Email/AccountEmailJobTests.cs` covering `ResendConfirmationAsync`'s content-model construction and its existing skip conditions (no account, already confirmed)
- [X] T010 [P] [US1] Write `PasswordEmailJobTests` in `tests/AskLucy.Infrastructure.Tests/Email/PasswordEmailJobTests.cs` covering `SendResetLinkAsync` and `SendPasswordChangedNoticeAsync`'s content-model construction, including the existing unprotect-failure throw path

### Implementation for User Story 1

- [X] T011 [US1] Implement `BrandedAccountEmailTemplateRenderer` in `src/AskLucy.Infrastructure/Email/BrandedAccountEmailTemplateRenderer.cs` — single-column table-based HTML shell (research.md Topic 2) with a text-based Flumeria wordmark header (no logo image), body area (greeting, paragraphs, optional emphasized CTA table-cell button, safety note), shared footer with sender identity and support contact; derive `TextBody` from the same `AccountEmailContent` so the two never drift (depends on T001)
- [X] T005a [US1] Register `IEmailTemplateRenderer` → `BrandedAccountEmailTemplateRenderer` as scoped in `src/AskLucy.Infrastructure/DependencyInjection.cs`, alongside the existing `IEmailSender` registrations (depends on T011 — the concrete type must exist before it can be registered)
- [X] T012 [US1] Update `RegisterCommandHandler` in `src/AskLucy.Application/Authentication/Commands/Register/RegisterCommandHandler.cs` to build an `AccountEmailContent` (existing subject/copy/link, `WebUtility.HtmlEncode`d display name) and call `IEmailTemplateRenderer` then `IEmailSender.SendAsync(toEmail, subject, htmlBody, textBody, ct)` (depends on T001, T002)
- [X] T013 [US1] Update `RequestEmailChangeCommandHandler` in `src/AskLucy.Application/Authentication/Commands/ChangeEmail/RequestEmailChangeCommandHandler.cs` the same way (depends on T001, T002)
- [X] T014 [US1] Update `AccountEmailJob.ResendConfirmationAsync` in `src/AskLucy.Infrastructure/Email/AccountEmailJob.cs` to build `AccountEmailContent` and call the renderer + extended sender, preserving its existing skip/logging behavior unchanged (depends on T001, T002, T011)
- [X] T015 [US1] Update `PasswordEmailJob.SendResetLinkAsync` and `SendPasswordChangedNoticeAsync` in `src/AskLucy.Infrastructure/Email/PasswordEmailJob.cs` the same way, preserving the unprotect-failure throw and logging paths unchanged (depends on T001, T002, T011)

**Checkpoint**: All five account emails render through the shared branded shell; `dotnet build` and the full unit test suite are clean.

---

## Phase 4: User Story 2 — Legible and elegant in both light and dark mode (Priority: P1)

**Goal**: Every account email stays fully legible, with a visible call-to-action, in both light-mode and dark-mode email clients, without manual toggling.

**Independent test**: Save a rendered HTML body and open it in Gmail, Apple Mail, and Outlook with each client's dark mode toggled on and off (quickstart.md), confirming text contrast and CTA visibility in both.

### Tests for User Story 2

- [X] T016 [P] [US2] Extend `BrandedAccountEmailTemplateRendererTests` in `tests/AskLucy.Infrastructure.Tests/Email/BrandedAccountEmailTemplateRendererTests.cs` to assert the rendered `HtmlBody` contains `<meta name="color-scheme" content="light dark">`, a `<meta name="supported-color-schemes" content="light dark">` tag, and a `@media (prefers-color-scheme: dark)` block that overrides both background and text colors and the CTA's background color (research.md Topic 2/3)

### Implementation for User Story 2

- [X] T017 [US2] Add the dark-mode color tokens (research.md Topic 3: near-black background, off-white text, Flumeria green accent) and the `@media (prefers-color-scheme: dark)` override block to `BrandedAccountEmailTemplateRenderer` in `src/AskLucy.Infrastructure/Email/BrandedAccountEmailTemplateRenderer.cs`, inlining light-mode colors explicitly on every element per research.md Topic 2 so Gmail's partial dark-mode handling cannot invert unstyled content (depends on T011)

**Checkpoint**: All five emails remain legible and on-brand in both light and dark mode across the three required clients (SC-002).

---

## Phase 5: User Story 3 — Unmistakable, single call-to-action for security actions (Priority: P2)

**Goal**: Every actionable email presents exactly one visually emphasized action with descriptive link text, and every email carries a clear "didn't request this?" safety note.

**Independent test**: Open each of the four actionable emails and confirm exactly one emphasized CTA is present with descriptive (non-"click here") link text, and that the safety note is visible without scrolling; open the password-changed notice and confirm the safety note appears with no competing CTA.

### Tests for User Story 3

- [X] T018 [P] [US3] Extend `BrandedAccountEmailTemplateRendererTests` in `tests/AskLucy.Infrastructure.Tests/Email/BrandedAccountEmailTemplateRendererTests.cs` to assert exactly one visually emphasized CTA element renders per content variant with a `PrimaryAction`, that the CTA link text equals `EmailAction.Label` (never generic placeholder text), that no CTA element renders when `PrimaryAction` is null, and that `SafetyNote` renders on every variant including the no-action password-changed case
- [X] T019 [P] [US3] Write a no-tracking assertion covering all five real content variants in `tests/AskLucy.Infrastructure.Tests/Email/BrandedAccountEmailTemplateRendererTests.cs` — for each of the five emails' actual subject/copy/link (as now built by `RegisterCommandHandler`, `RequestEmailChangeCommandHandler`, `AccountEmailJob`, `PasswordEmailJob`), assert the rendered `HtmlBody` contains no `<img>` tag and no anchor `href` other than the exact URL supplied in the content model (FR-010)

### Implementation for User Story 3

- [X] T020 [US3] Review and, where needed, tighten `BrandedAccountEmailTemplateRenderer`'s CTA markup in `src/AskLucy.Infrastructure/Email/BrandedAccountEmailTemplateRenderer.cs` so the primary action is unambiguously the single most visually emphasized element (solid brand-color fill, no competing secondary links styled similarly) — informed by T018/T019 (depends on T011, T017)
- [X] T021 [P] [US3] Audit the five call sites' `EmailAction.Label` values (`RegisterCommandHandler`, `RequestEmailChangeCommandHandler`, `AccountEmailJob`, `PasswordEmailJob`) to confirm every label is descriptive on its own (e.g., "Confirm my email", "Reset my password") and update any that are not

**Checkpoint**: All user stories independently pass their acceptance scenarios; the feature is complete per spec.md.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Final verification spanning all three stories.

- [X] T022 [P] Update or add `SmtpEmailSenderTests`/`ConsoleEmailSenderTests` in `tests/AskLucy.Infrastructure.Tests/Email/` for the extended `SendAsync` signature (multipart body assertion for SMTP, dual-body log assertion for console)
- [X] T023 Run `dotnet format` and `dotnet build` across the solution to confirm zero warnings on all changed files (constitution §12)
- [ ] T024 Run the quickstart.md validation end to end — trigger all five emails, cross-check light/dark legibility in Gmail, Apple Mail, and Outlook, confirm no tracking element in any rendered output, and confirm the layout stays single-column with the call-to-action reachable without scrolling at both a narrow (~375px) and wide (~1024px+) preview width (SC-001 through SC-004, FR-009)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: None — no tasks.
- **Foundational (Phase 2)**: No dependencies beyond the existing codebase — BLOCKS all user stories.
- **User Stories (Phase 3+)**: All depend on Foundational phase completion.
  - US1 (Phase 3) must complete first — it creates `BrandedAccountEmailTemplateRenderer`, the file US2 and US3 both extend.
  - US2 (Phase 4) and US3 (Phase 5) both extend the same renderer file created in US1, so they run sequentially against that file (not in parallel with each other), though either order is valid once US1 is done.
- **Polish (Phase 6)**: Depends on all three user stories being complete.

### User Story Dependencies

- **User Story 1 (P1)**: Can start after Foundational (Phase 2). No dependency on other stories.
- **User Story 2 (P1)**: Can start after Foundational, but its implementation task (T017) edits the same file US1 creates (T011) — start after US1's T011 lands to avoid a merge conflict, even though US2 is independently testable once its layer is added.
- **User Story 3 (P2)**: Same constraint — its implementation tasks (T020) touch the file US1/US2 already edited.

### Within Each User Story

- Tests MUST be written and FAIL before implementation.
- Foundational abstractions before renderer implementation.
- Renderer implementation before call-site wiring.
- Story complete before moving to the next priority.

### Parallel Opportunities

- T003 and T004 (Phase 2) can run in parallel — different files.
- T006–T010 (US1 tests) can all run in parallel — different files, no shared dependency beyond already-completed Phase 2.
- T007, T008, T009, T010 remain parallel with each other even during implementation, since T012–T015 each touch a distinct call-site file.
- T018 and T019 (US3 tests) can run in parallel with each other, though both land in the same test file as T016 — coordinate via sequential edits or a single combined PR.
- T021 (US3 copy audit) can run in parallel with T020 (US3 CTA markup) — different concerns, though both may touch overlapping files; sequence if conflicts arise.

---

## Parallel Example: User Story 1

```bash
# Launch all US1 tests together:
Task: "Write BrandedAccountEmailTemplateRendererTests in tests/AskLucy.Infrastructure.Tests/Email/BrandedAccountEmailTemplateRendererTests.cs"
Task: "Update RegisterCommandHandlerTests in tests/AskLucy.Application.Tests/Authentication/RegisterCommandHandlerTests.cs"
Task: "Update RequestEmailChangeCommandHandlerTests in tests/AskLucy.Application.Tests/Authentication/RequestEmailChangeCommandHandlerTests.cs"
Task: "Write AccountEmailJobTests in tests/AskLucy.Infrastructure.Tests/Email/AccountEmailJobTests.cs"
Task: "Write PasswordEmailJobTests in tests/AskLucy.Infrastructure.Tests/Email/PasswordEmailJobTests.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 2: Foundational.
2. Complete Phase 3: User Story 1 — all five emails share one branded shell.
3. **STOP and VALIDATE**: Trigger all five emails (quickstart.md), confirm visual consistency.
4. Deploy/demo if ready — this alone is a major improvement over today's five unstyled emails, even before dark-mode/CTA hardening lands.

### Incremental Delivery

1. Foundational → renderer contract and multipart transport ready.
2. US1 → branded, consistent emails (MVP) → validate → deploy.
3. US2 → dark-mode legibility layered on → validate → deploy.
4. US3 → single-CTA/safety-note hardening → validate → deploy.
5. Polish → cross-client sign-off, format/build verification.

---

## Notes

- [P] tasks = different files (or, within Phase 2/3, clearly independent code paths), no dependency on an incomplete task.
- [Story] label maps task to specific user story for traceability.
- US2 and US3 are additive layers on the single renderer file US1 creates — they are independently *testable* (each has its own acceptance criteria and test assertions) but not independently *mergeable* without US1 landing first.
- Verify tests fail before implementing.
- Commit after each task or logical group.
- No database, endpoint, or frontend changes exist in this feature — every task is backend-only within `Application`/`Infrastructure`.

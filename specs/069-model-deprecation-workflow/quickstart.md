# Quickstart: Model Deprecation Workflow

Validation scenarios for [spec.md](spec.md). API shapes are in [contracts/](contracts/), entities in [data-model.md](data-model.md).

## Prerequisites

- Backend running locally with the dev test database migrated (`AddModelDeprecationWorkflow` applied).
- Two admin accounts (one Administrator, one Super User) and two ordinary users, created by the dev seeder or by hand. Do not reuse real credentials.
- An AI provider with at least three Available models: a retired candidate **A**, a similar model **B**, and a dissimilar model **C**. Use a fake vendor client that returns a controlled model list, so the sync can omit **A**.

## Automated checks

```bash
dotnet test tests/AskLucy.Domain.Tests --filter "FullyQualifiedName~ModelDeprecation|FullyQualifiedName~ReplacementModelSelector|FullyQualifiedName~AIModelStatus"
dotnet test tests/AskLucy.Application.Tests --filter "FullyQualifiedName~ModelDeprecation|FullyQualifiedName~ExecutableModelResolver"
dotnet test tests/AskLucy.Web.Tests --filter "FullyQualifiedName~ModelDeprecation"
cd src/AskLucy.Web/ClientApp && npx tsc -b --noEmit && npx vitest run
```

Run the full backend and frontend suites before pushing. `Web.Tests` needs `PERSISTENCE_TESTS_CONNECTION_STRING` set.

## Scenario S1: Deprecate with impact preview (US1, FR-001..FR-004)

1. Make **A** a provider default and the Chat capability's model. Give user 1 two conversations and an agent on **A**. Give user 2 a saved prompt that prefers **A**.
2. In Admin → AI Providers, run Sync with a vendor list that omits **A**.
3. **Expect**: the row for **A** shows both defaults, a proposed replacement (**B**), and counts of 2 conversations, 1 agent and 1 prompt across 2 users, with a permanence warning.
4. Select the row and confirm.
5. **Expect**: **A** shows as Deprecated (not Unavailable) and no longer appears in any picker. A deprecation record shows reason, time and confirming admin.

## Scenario S2: Defaults follow the replacement, or get flagged (US2)

1. After S1, **Expect**: the provider default and the Chat assignment both name **B**, and the record shows "awaiting validation".
2. Repeat with a model that has no eligible replacement. **Expect**: the defaults are cleared, an open flag shows on the provider's row naming the retired model and the model now serving, and a Chat message still works through the existing default rules.
3. Set a new default for the flagged target. **Expect**: the flag closes and the record shows who resolved it and which model they chose.
4. For `ImageGeneration`, pick a replacement without image output at override time. **Expect**: the review lists the lost capability, and on confirm the target is flagged, not reassigned.

## Scenario S3: Users keep working and are told once (US3)

1. Within a minute of S1 (the job is quick), **Expect**: user 1's conversations and agent draft now name **B**. User 2's prompt prefers **B**.
2. **Expect**: each of user 1 and user 2 has exactly one in-app notice listing their own items and the new model. A third user with no items has none.
3. Run an agent from its published version that still names **A**. **Expect**: it runs on **B** and the output shows **B**. A past message in user 1's chat still shows **A**.
4. Restart the backend mid-job (kill it during chunk processing), then start it again. **Expect**: the job finishes, with no duplicate notices.

## Scenario S4: Admins validate (US4, FR-011a/b, FR-020b)

1. **Expect**: the Administrator received the summary in-app, with a link to the record. The Super User received the `.super-user` type. The email half is recorded as Skipped until spec 067's email channel exists (research D8).
2. Open the record and change the replacement to **C**.
3. **Expect**: the defaults move to **C**. Items switched to **B** move to **C**, except one the owner changed by hand in between. Affected owners get a follow-up notice.

## Scenario S5: Status lock (US5, FR-021/022)

1. Call `PATCH /models/{A}` with `Available` and then `Unavailable`. **Expect**: `409` both times, and the status is unchanged.
2. Call `PATCH /models/{B}` with `Deprecated`. **Expect**: `409`.
3. **Expect**: the toggle on **A**'s row is disabled.

## Scenario S6: No replacement (FR-015)

1. Deprecate a model with no eligible replacement while a user has a conversation on it.
2. Send a message in that conversation. **Expect**: a clear message naming the retired model and how to pick another. No other provider answers.
3. **Expect**: a run of an affected agent fails with the same text, and an incident appears on the admin failure trail (spec 074).

## Performance check (SC-007)

Seed 5,000 conversations across 2,000 users on **A**, then confirm the deprecation. **Expect**: the apply request returns in under 10 s, and the job completes afterwards.

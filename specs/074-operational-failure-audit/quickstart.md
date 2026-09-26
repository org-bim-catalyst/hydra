# Quickstart: validating the Operational Failure Audit Trail

This is a validation run-book, not an implementation guide. The shapes and rules are in
[contracts/](contracts/) and [data-model.md](data-model.md).

## Prerequisites

- The migration `AddOperationalFailureAudit` is applied to the shared test DB, the dedicated
  Persistence test2 DB, and production. They are different catalogs, and each must be migrated by
  hand (data-model.md Migration notes).
- `PERSISTENCE_TESTS_CONNECTION_STRING` is set **per suite**. For Web.Tests it is the shared test DB
  (`ConnectionStrings:DefaultConnection`). For Persistence.Tests it is the dedicated test2 DB
  (`ConnectionStrings:PersistenceTests`), with `PERSISTENCE_TESTS_DEDICATED_DATABASE=1`. Both values
  are in `appsettings.Development.json`, and docs/TESTING.md §13 has the exact commands. Never set
  the DEDICATED flag with the shared test DB.
- Four accounts: a **Super User**; an **Administrator**; a **custom role** "Support lead" holding
  `admin.operational-failures.view`; and an ordinary **user**.

## 1. Automated suites

```bash
dotnet test tests/AskLucy.Domain.Tests         --filter "FullyQualifiedName~OperationalFailures"
dotnet test tests/AskLucy.Application.Tests    --filter "FullyQualifiedName~OperationalFailures|FullyQualifiedName~Authorization|FullyQualifiedName~DocumentProcessingFailureText|FullyQualifiedName~DeleteMyAccount"
dotnet test tests/AskLucy.Infrastructure.Tests --filter "FullyQualifiedName~OperationalFailures|FullyQualifiedName~ProviderHealthCheckHostedService"
dotnet test tests/AskLucy.Persistence.Tests    --filter "FullyQualifiedName~OperationalFailures"
dotnet test tests/AskLucy.Web.Tests            --filter "FullyQualifiedName~OperationalFailures|FullyQualifiedName~ProblemDetails|FullyQualifiedName~Roles|FullyQualifiedName~ContentPermissionGrantPaths"
cd src/AskLucy.Web/ClientApp && npx tsc -b --noEmit && npx vitest run
```

**Expected:** all pass. Run the **full** frontend suite, not only the touched files, because
ChatPage and the admin pages carry their own assertions (memory note). Then run the CI-equivalent
`dotnet format "Ask Lucy.sln" --no-restore --verify-no-changes --severity error`, filtering only
the whole-file `ENDOFLINE` noise, and the lone-CR scan.

## 2. Manual scenarios (localhost:7170 or production)

Each scenario lists its steps and the value to check. The admin page is
`/admin/operational-failures`.

| # | Steps | Expected |
|---|---|---|
| S1 (US1, SC-001) | 1. As the Super User, set Anthropic's API key to `sk-ant-invalid`. 2. As the ordinary **user**, send "hello" with Anthropic selected. | The user sees "This isn't available right now. Please try again later." with no word from the SC-001 list. Within about 5 s the admin page shows one row: **Critical · Chat · Anthropic / {model} · Credential rejected**, 1 occurrence, 1 user. |
| S2 (US1 scenario 5, SC-005) | Click the badge, then the S1 row, then "Replace the API key". | This takes 3 clicks or fewer and lands on `/admin/ai-providers?select=…` with Anthropic selected. |
| S3 (FR-010) | Copy the occurrence's correlation id. Search the server log for it. | The log has an `AI provider failure surfaced` line with the same `CorrelationId`. |
| S4 (US1 scenario 2) | Restore the key. Force a mid-stream failure (for example, drop the network adapter while a long reply streams). | The partial reply is kept and the calm notice appended. The occurrence kind is the classified kind or `DependencyUnreachable`, **not** `UnexpectedError`. The next turn's model context (Debug log) contains only the generic sentence. |
| S5 (US2, SC-003) | Run `OperationalFailureReplayTests.ElevenLabs_2026_09_22` (Web.Tests), which replays 7 failover and 7 recovery events in 70 s. | 1 incident: 7 occurrences, 7 recoveries, 1 user. The badge increases by exactly 1. |
| S6 (US3 scenario 5, SC-011) | Seed 40 documents and set the embedding key invalid. Upload or reindex them. | There are 40 incidents and the badge shows 1. One incident says "39 other incidents share this cause". Resolve all with a note resolves all 40 and clears the badge. |
| S7 (US1 scenarios 6–7, SC-009) | Open S1's chat link as (a) the Administrator and (b) the Super User. | (a) Metadata only, and the network response has `transcript: null`. (b) The transcript is shown with the failed turn highlighted, and one `UserContentAccessEvents` row is added naming the Super User, the owner, the chat and the incident. Neither view has a composer or any edit control. |
| S8 (US1b, SC-010) | As the Administrator, try to tick *View user content* on "Support lead", assign "Support lead" once a Super User has granted it, and send a crafted PUT that omits the key. | All three are refused (403 toast "Only a Super User…"). As the Super User, granting it, turning on the Administrator switch and revoking it all succeed, and each appears in the role audit trail. |
| S9 (US4 scenario 3) | Enqueue a test job that throws, with `[AutomaticRetry(Attempts = 2)]`. | Exactly 1 occurrence (kind `JobFailedAfterRetries`) after the third attempt. Its correlation id appears on all three attempts' log lines. |
| S10 (FR-006, SC-013) | Sign in with a wrong password 5× from one browser, sign in with an unknown email once, send a bad chat request body once, and open another user's chat URL once. | One Warning *Access · Sign-in refused* incident (5 occurrences, 1 account, 1 source); one unknown-email occurrence with no user; **no** record for the bad body; one *Access denied* occurrence; the chat URL still returns 404. The badge is unchanged. |
| S11 (FR-020, SC-004) | Stop SQL access for the writer (for example, revoke INSERT on the incidents table on the test DB) and repeat S1. | The user sees exactly the same message with no added delay. The server log has `OperationalFailureRecordingFailed` with S1's correlation id. Restore the grant afterwards. |
| S12 (US5, SC-008) | Seed incidents aged 91 d (acknowledged), 91 d (unacknowledged) and 181 d (unacknowledged). Trigger `OperationalFailureRetentionJob` from the jobs dashboard. | The 91 d acknowledged and 181 d incidents are removed, and the 91 d unacknowledged one remains. The log shows the counts. |
| S13 (FR-029a, SC-012) | Give a throwaway user one occurrence and one content-access event, then delete the account through Settings → Delete account. | The occurrence shows "erased user". Incident counts are unchanged. The access event remains with `IsOwnerErased = 1`. A search of the four tables for the user id returns 0 rows. |

## 3. Secret scan (SC-006)

After the suites run, search every stored `Reason` in the test DB for the redaction corpus patterns
(`Bearer `, `sk-`, `AIza`, `eyJ`, `Authorization`, `key=`, `Password=`). The query is kept with
the Persistence test `StoredReasons_ContainNoSecrets`. **Expected:** 0 rows.

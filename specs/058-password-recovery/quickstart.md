# Quickstart: Validating Password Recovery & Management

**Feature**: 058-password-recovery

How to run and prove this feature end to end. Endpoint shapes are in
[contracts/password-api.md](./contracts/password-api.md); token rules are in
[data-model.md](./data-model.md).

---

## Prerequisites

- .NET 10 SDK, Node 20+, SQL Server reachable via the `Development` connection string.
- `PERSISTENCE_TESTS_CONNECTION_STRING` set in the environment before running `AskLucy.Web.Tests`
  — without it almost every test in that project fails at host startup. The value is in
  `appsettings.Development.json`.
- Do **not** set `PERSISTENCE_TESTS_DEDICATED_DATABASE=1` against the shared dev database.
- Email in development goes to the development sender, which writes messages to disk rather than
  sending them; reset links are read from there, not from an inbox.
- **A Hangfire server must be running** — reset and notification emails are enqueued as background
  jobs, not sent inline, so with no worker processing the queue nothing is ever written to disk and
  the flow looks silently broken. `dotnet run` on the Web project starts one; confirm the job
  actually ran at `/hangfire` (administrator/operator sign-in required) before concluding the
  feature is at fault.

## Setup

```powershell
dotnet ef database update --project src/AskLucy.Persistence --startup-project src/AskLucy.Web
dotnet run --project src/AskLucy.Web          # API + SPA on https://localhost:7170
```

## Scenario 1 — Forgot password, happy path (US1 + US2)

1. Sign out. From `/login`, follow **Forgot password?** to `/forgot-password`.
2. Submit the address of a confirmed account. Expect the neutral confirmation panel.
3. Open the captured development email and follow the reset link to `/reset-password`.
4. Enter a new password twice and submit. Expect success and a prompt to sign in.
5. Sign in with the new password — succeeds. Sign in with the old one — fails.

**Also verifies**: SC-001. (SC-002 is asserted automatically by the latency test in T014, not by
this walkthrough.)

## Scenario 2 — No account enumeration (FR-003, SC-005)

Submit each of these on `/forgot-password` and confirm the response body, status and on-screen text
are byte-identical every time:

| Address | Expected |
|---------|----------|
| A confirmed account | 202, neutral panel, email sent |
| An address with no account | 202, neutral panel, no email |
| An account that never confirmed its email | 202, neutral panel, no email |
| A locked-out account | 202, neutral panel, no email |

## Scenario 3 — Link is single-use, expiring and supersedable (FR-005, FR-006, SC-006)

| Action | Expected |
|--------|----------|
| Reuse a link that already reset a password | Invalid-link message with a "request a new link" affordance |
| Request a second link, then open the first | First link rejected, second works |
| Age a token past one hour (update `ExpiresAtUtc` directly in the database) | Invalid-link message |
| Alter one character of the token in the URL | Invalid-link message — never a raw error page |
| Change the account's email, then open an outstanding link | Invalid-link message |

Every rejection must be the *same* message; the distinguishing cause appears only in the server log.

## Scenario 4 — Sessions and 2FA (FR-009, FR-010, FR-012, SC-007)

1. Sign in to the same account in two browsers.
2. Complete a reset. Both browsers are signed out on their next request.
3. Sign in again; if the account has 2FA enabled, the second factor is still demanded.
4. Now change the password from Settings in browser A. Browser A stays signed in; browser B is
   signed out on its next request.

## Scenario 5 — Change and set password (US3, US4, FR-013, FR-014)

- Settings → password section, correct current password + new password twice → success, plus a
  notification email.
- Wrong current password → inline error, no change.
- Mismatched confirmation or policy violation → inline error *before* any request is sent.
- Signed in as an account created via Google/Microsoft with no password → the section offers
  **Set password** and asks for no current password; afterwards email-and-password sign-in works.

## Automated checks

```powershell
dotnet test tests/AskLucy.Application.Tests
dotnet test tests/AskLucy.Web.Tests
cd src/AskLucy.Web/ClientApp
npx tsc -b --noEmit      # bare `tsc --noEmit` checks nothing in this repo
npm run lint
npm test                 # run the FULL suite — page-level tests assert on these components too
```

## Things that will bite

- Run the whole frontend suite, not just the files you touched: `SettingsPage`/`LoginPage` tests
  carry their own assertions about the password UI.
- The migration file must be written without a BOM, or CI's format check fails.
- Never log the plaintext token while debugging, including in a temporary `Console.WriteLine` —
  FR-016 is checked by review, and a leaked token in a shared-host log file is a real exposure.

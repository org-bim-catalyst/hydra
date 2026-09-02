# Quickstart: Validating Provider Failure Classification & Health Reporting

**Feature**: `043-provider-error-classification`

How to prove this feature works end to end. Scenario numbers map to the acceptance scenarios in [spec.md](./spec.md); field shapes are in [contracts/](./contracts/) and are not repeated here.

---

## Prerequisites

- .NET 10 SDK, Node 20+
- SQL Server reachable via the `DefaultConnection` string
- An account in `Administrator` or `Super User` (dev seeding provides one via `DevAdminSeeder`)
- A second, non-administrator account — required for scenario 7, the disclosure gate

```bash
dotnet restore
dotnet ef database update -p src/AskLucy.Persistence -s src/AskLucy.Web
npm --prefix src/AskLucy.Web/ClientApp ci
```

> The shared CI persistence database needs the new migration applied manually before the persistence suite passes against it.

---

## Automated validation

```bash
# Full backend suite — the classifier, handlers, middleware mapping, and boundary fallback
dotnet test

# Focused runs while iterating
dotnet test tests/AskLucy.Infrastructure.Tests --filter "FullyQualifiedName~Ai"
dotnet test tests/AskLucy.Infrastructure.Tests --filter "FullyQualifiedName~Boundaries"
dotnet test tests/AskLucy.Web.Tests --filter "FullyQualifiedName~ProblemDetails"

# Frontend — run the FULL suite, not only the touched files
npm --prefix src/AskLucy.Web/ClientApp test -- --run
npm --prefix src/AskLucy.Web/ClientApp exec tsc -b --noEmit

# Formatting / analyzers
dotnet format --verify-no-changes
npm --prefix src/AskLucy.Web/ClientApp run lint
```

Two repo-specific gotchas that have bitten this codebase before:

- **`tsc --noEmit` alone is a silent no-op** in `ClientApp` — its root tsconfig uses project references. Use `tsc -b --noEmit`, as above.
- **Page-level tests carry their own assertions.** Changing a component without running the full frontend suite misses failures in the page tests that render it.

### Expected coverage

| Area | Test project | Proves |
|---|---|---|
| Classifier, table-driven per vendor over `StubHttpMessageHandler` | `AskLucy.Infrastructure.Tests/Ai` | Every row of the classification tables (SC-001) |
| No credential / body / type name / stack trace in any message | `AskLucy.Infrastructure.Tests/Ai` + `AskLucy.Web.Tests` | SC-008 |
| Kind → status + `type` mapping; admin-gated `providerFailure` | `AskLucy.Web.Tests` | FR-015a, SC-002 |
| `CheckAiProviderHealth` handler; staleness horizon | `AskLucy.Application.Tests/Ai` | FR-019, FR-024 |
| Optional token limits accepted; supplied `0` still rejected | `AskLucy.Domain.Tests/Ai` | FR-029 |
| Sync apply adds null-limit rows instead of failing them | `AskLucy.Application.Tests/Ai` | SC-006 |
| Vision failure modes + 30s budget + cancellation ≠ failure | `AskLucy.Infrastructure.Tests/Boundaries` | SC-007, FR-032–FR-035 |
| Health cell states and a11y | `ClientApp` (Vitest + axe) | FR-017–FR-021 |

---

## Manual validation

```bash
dotnet run --project src/AskLucy.Web
# SPA: npm --prefix src/AskLucy.Web/ClientApp run dev
```

Sign in as an administrator and open **Admin → AI providers**.

### Scenario 1 — Credential rejected *(US1 #1)*

Set a Gemini credential to a syntactically valid but wrong key. Run **Sync from provider**.

**Expect**: a message naming a rejected credential and pointing at replacing the API key. **Not** "An unexpected error occurred."

### Scenario 2 — Quota exhausted *(US1 #2, FR-018)*

Easiest without burning real quota: point the provider's base URL at a local stub returning `429` with a `google.rpc.QuotaFailure` detail.

**Expect**: the message says the provider is configured correctly but temporarily unavailable on quota, and never suggests the credential is wrong. After the next health check, the row renders as *configured, temporarily limited* — visually distinct from a credential failure.

### Scenario 3 — Rate limited *(US1 #3)*

Stub returns `429` with `RESOURCE_EXHAUSTED` and no `QuotaFailure`, plus `Retry-After: 30`.

**Expect**: identified as rate limiting, with the 30-second wait conveyed. Confirm the response carries the `Retry-After` header.

### Scenario 4 — Billing disabled *(US1 #4)*

Stub returns `403` with `error.details[].reason = BILLING_DISABLED`.

**Expect**: names the billing restriction. **Regression guard**: it must not say "check the provider's API key" — that misdirection is the specific defect this scenario exists to catch.

### Scenario 5 — Credential unreadable *(US1 #6)*

The most likely real cause of the reported symptom. Reproduce by rotating the Data Protection key ring (delete or replace the keys directory) and restarting, leaving the stored ciphertext in place.

**Expect**: the sync says the stored credential could not be read and must be re-entered. Health shows the same. Previously this was the generic 500 *plus* a red Unhealthy chip on a provider whose credential read "Configured" — the exact pair of symptoms in the original report.

### Scenario 6 — Timeout *(US1 #5)*

Stub delays past the client timeout.

**Expect**: "temporarily unreachable, retry" — not the generic 500 that a `TaskCanceledException` produces today.

### Scenario 7 — Disclosure gate *(FR-015a)*

Sign in as the **non-administrator**. Trigger a provider failure through chat.

**Expect**: today's generic message. Inspect the response in devtools: **no `providerFailure` member**, and the generic `detail`. This is the check that the classification did not leak tenant-wide.

### Scenario 8 — Staleness *(US2 #3, FR-019)*

Stop the app (or set `ProviderHealthCheck:Interval` to something long), wait past `healthStaleAfterUtc`, reload.

**Expect**: the status presents as possibly out of date rather than as current fact. The original report showed a status two days old rendering as current.

### Scenario 9 — On-demand re-check *(US3)*

With a provider showing Unhealthy, fix the cause, then trigger **Check now**.

**Expect**: status and reason refresh immediately with a just-now timestamp — no waiting for the background cycle. The trigger is disabled while in flight. Repeated rapid triggering eventually yields a `429` from the rate-limit policy rather than unbounded probes.

### Scenario 10 — Token limits never block adding *(US4)*

Expand **OpenAI → Sync from provider**, choose **Select all**, Confirm.

**Expect**: every row is added — all ~97 of them, in one action, with no figures typed. Previously every row failed with *"Context window must be greater than zero."* In the model list the two figures read **"Not published by the vendor"**, never `0`, and — per FR-029a — not the word "Unknown" that the same table uses for absent pricing. Enable one via the existing status control: no requirement to supply the figures first.

### Scenario 11 — Boundary fallback and its budget *(US5)*

Point the vision model at an unreachable or slow stub, then resolve a site boundary.

**Expect**: the boundary resolves normally from the deterministic result, carrying a note that AI verification was unavailable and why. Time it: the added delay is capped near 30 seconds, not the ~120 seconds the shared client timeout allowed. Cancel a request mid-vision-call and confirm it is reported as a cancellation, not a provider failure.

---

## Definition of done

- [ ] All nine classifications produce a distinct, accurate administrator message (SC-001)
- [ ] "An unexpected error occurred" appears for zero provider-originated failures (SC-002)
- [ ] Quota/rate-limit is distinguishable from a bad credential at a glance (SC-003)
- [ ] A credential fix is confirmable within 30 seconds via **Check now** (SC-004)
- [ ] No status displays as current when it is not (SC-005)
- [ ] A vendor list with no token metadata adds every selected row, zero typed figures (SC-006)
- [ ] Boundary resolution survives every vision failure mode within the 30s budget (SC-007)
- [ ] No user-visible message leaks a credential, vendor body, type name, or stack trace (SC-008)
- [ ] Regression coverage for every classification and every vision failure mode; full suite green (SC-009)
- [ ] `dotnet format --verify-no-changes`, `tsc -b --noEmit`, and lint all clean

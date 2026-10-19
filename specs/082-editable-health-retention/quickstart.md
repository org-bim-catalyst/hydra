# Quickstart: Editable Provider Health-Check Retention

**Feature**: 082-editable-health-retention

Validation guide only — implementation details live in `tasks.md`. Contract: [contracts/admin-provider-health-retention-api.md](contracts/admin-provider-health-retention-api.md). Model: [data-model.md](data-model.md).

## Prerequisites

- .NET 10 SDK; Node.js and npm installed.
- The shared test database connection string available (`PERSISTENCE_TESTS_CONNECTION_STRING` for `AskLucy.Web.Tests`, `PERSISTENCE_TESTS_2_CONNECTION_STRING` for `AskLucy.Persistence.Tests`), defined in `src/AskLucy.Web/appsettings.Development.json`.
- The new migration applied to that shared test database **by hand** before running the tests — CI and the test host do not apply migrations to the shared database.

## 1. Build and static checks

```powershell
dotnet build "Ask Lucy.sln"
cd src/AskLucy.Web/ClientApp
npx tsc -b --noEmit
npm run lint
```

Expected: both commands succeed. (`tsc -b` is required; a bare `tsc --noEmit` checks nothing in this project.)

## 2. Generate and apply the migration

```powershell
dotnet ef migrations add AddProviderHealthRetentionSetting `
  --project src/AskLucy.Persistence --startup-project src/AskLucy.Web
```

Expected: one migration with `CreateTable` for `ProviderHealthRetentionSettings` and `ProviderHealthRetentionAuditLogs`, an `InsertData` for the singleton row (30 days), and a working `Down` that drops both tables.

Apply it to the shared test database manually, then:

```powershell
dotnet test "Ask Lucy.sln" --no-build
```

Expected: green. Targeted suites:

```powershell
dotnet test tests/AskLucy.Application.Tests --filter "FullyQualifiedName~ProviderHealthRetention"
dotnet test tests/AskLucy.Infrastructure.Tests --filter "FullyQualifiedName~ProviderHealthCheckRetentionJob"
dotnet test tests/AskLucy.Web.Tests --filter "FullyQualifiedName~AdminProviderHealthRetention"
```

## 3. Backend behaviour (API)

Start the app:

```powershell
dotnet run --project src/AskLucy.Web
```

Sign in as an administrator holding `admin.operational-failures.view` and `.manage`, then:

1. **GET** `/api/v1/admin/provider-health/retention`
   - Expected: `200` with `retentionDays: 30`, `source: "Stored"`, a non-null `rowVersion`, and the existing `intervalMinutes`.
2. **PUT** with `If-Match: <rowVersion>` and `{ "retentionDays": 14 }`
   - Expected: `200`; the response has `retentionDays: 14` and a new `rowVersion`.
3. **PUT again** with the old `rowVersion`
   - Expected: `409` with `"reason": "ConcurrencyConflict"`; the stored value stays 14.
4. **PUT** `{ "retentionDays": 0 }` or `{ "retentionDays": 366 }`
   - Expected: `400` with an `errors` entry for `RetentionDays`; the stored value is unchanged.
5. **PUT without `If-Match`**
   - Expected: `428 "If-Match required"`.
6. **GET as a user without the view permission; PUT as a user with view but not manage**
   - Expected: `403` in both cases.
7. **Audit check**: after each successful PUT, the `ProviderHealthRetentionAuditLogs` table gains exactly one row with the actor, previous value and new value. Rejected calls add none.

## 4. Cleanup job

Unit-level:

```powershell
dotnet test tests/AskLucy.Infrastructure.Tests --filter "FullyQualifiedName~ProviderHealthCheckRetentionJob"
```

Expected: the job deletes rows older than the **stored** value; when the repository returns no row it deletes using the configured fallback (default 30) and still completes.

Optional manual check: set the stored value to 1, trigger the Hangfire job `provider-health-check-retention` (or wait for 03:30), and confirm only rows older than one day are removed and the run logs the cutoff.

## 5. Admin page

```powershell
cd src/AskLucy.Web/ClientApp
npm run test -- src/features/admin/providerHealth/AdminProviderHealthRetentionPage.test.tsx
```

Expected: the page test covers loading, editing and saving, a validation error, a conflict error with reload, and a read-only view for a user without the manage permission.

Manual:

1. Sign in as an administrator and open `/admin/provider-health`.
   - Expected: the current value is shown in an editable field labelled with its unit (days) and the allowed range (1–365); the check interval line is unchanged.
2. Enter a valid value and save.
   - Expected: a success confirmation appears, the new value survives a reload, and entering the page as another administrator shows the same value.
3. Enter 0 or a non-number and save.
   - Expected: an inline error explains the allowed range; no success confirmation.
4. Sign in as a user with only `admin.operational-failures.view`.
   - Expected: the page shows the value and no editable control; a direct PUT is still refused by the API.
5. Open the page while another administrator changes the value in a second session, then save with the stale version.
   - Expected: a conflict message with a reload action; the newer value is not overwritten.

## 6. Regression checks

- `GET` still returns `retentionDays` and `intervalMinutes` with the same names, so the current page code is not broken mid-deploy.
- `docs/DATABASE.md` describes the new tables and the stored-value precedence.
- `ProviderHealthCheck:RetentionDays` still works when the singleton row is absent.

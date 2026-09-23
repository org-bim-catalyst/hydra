# Quickstart / Validation: Custom Model Deployment

Run these in order. Contracts: [admin-custom-models.md](contracts/admin-custom-models.md),
[custom-model-deployment-hub.md](contracts/custom-model-deployment-hub.md). Entities:
[data-model.md](data-model.md).

## 0. Prerequisites

- The untracked `src/AskLucy.Web/appsettings.{Development,Production}.json` files have an `Ftp`
  section (Host, Port, Username, Password, `RootPath: "/hydra"`). The tracked `*.example` files
  contain placeholders only:
  `git grep -n '"Password"' -- '*.example'` must show nothing except empty strings or placeholders.
- `PERSISTENCE_TESTS_CONNECTION_STRING` is set for Web.Tests (see the memory note on Web.Tests
  needing a database connection environment variable).
- The migration has been applied: `dotnet ef database update -p src/AskLucy.Persistence -s src/AskLucy.Web`.

## 1. Automated gates

```bash
dotnet build "Ask Lucy.sln" -warnaserror
dotnet test tests/AskLucy.Domain.Tests  --filter "FullyQualifiedName~CustomModels"
dotnet test tests/AskLucy.Application.Tests --filter "FullyQualifiedName~CustomModels|FullyQualifiedName~Voice"
dotnet test tests/AskLucy.Infrastructure.Tests --filter "FullyQualifiedName~CustomModels|FullyQualifiedName~Supertonic"
dotnet test tests/AskLucy.Web.Tests --filter "FullyQualifiedName~CustomModels"
dotnet format --verify-no-changes
cd src/AskLucy.Web/ClientApp && npx tsc -b --noEmit && npx eslint . && npx vitest run
```

Expected: everything passes. The **full** vitest suite is run, not only the touched files (see the
memory note on page-level tests missing component changes). The test cases that must exist are
listed in plan.md → Testing strategy.

## 2. FTPS reachability (run once, before first production use)

Deploy a tiny public repository, `hf-internal-testing/tiny-random-bert` (a few MB), to
`Models/_smoke-test` from the **production** admin panel.

| Result | Meaning / action |
|---|---|
| Completed | FTPS works. |
| Failed: "the deployment server does not accept encrypted connections" | The host refuses AUTH TLS. Decide consciously whether to set `Ftp:AllowPlainFtp: true` in the untracked production settings (research D2). |
| Failed: "TLS certificate is not trusted" | The host's certificate doesn't match. Don't disable validation; raise it with the host. |

Deploy the same repository to `Models/_smoke-test` a second time. The job ends `Completed` and
lists every file as overwritten, each with its previous size (FR-010a).

Both records stay in the list as `Unavailable`. A Completed model can't be removed in this feature
(FR-031). Delete `/hydra/Models/_smoke-test` with an FTP client if you want the files gone.

## 3. End-to-end: Supertonic (SC-008)

1. Open **Admin → AI Providers**. The Custom Models section sits below the providers, showing an
   empty state and the **Add model** button.
2. Choose Add model. In Source, enter `https://huggingface.co/Supertone/supertonic-3/tree/main`.
   The name `supertonic-3` is derived and no Name field appears. In Destination, enter
   `Models/supertonic-3`. Don't deploy over the live `App_Data/Models/supertonic-3`: its ONNX files
   are loaded, and a locked file fails the job (plan.md → Risks).
3. A row appears immediately as `Queued`, then `Listing`, then `Transferring`. Once Listing ends it
   shows 38 files and about 415 MB. The per-file and overall bars move at least every 2 s (SC-004).
   While the job runs, Voice "+" still offers Supertonic from its configured folder: only a
   Completed record takes over (FR-039).
4. Open the same page in a second tab. The same progress shows there. Reload mid-transfer: the
   progress shown is no more than about 2 s behind.
5. The job ends `Completed` with no overwritten files, because `Models/supertonic-3` was empty.
   Overwrite reporting is checked in §2.
6. Availability is `Unavailable`. Check **Admin → Voice → "+"**: Supertonic is **not** offered.
7. Set the model Available. Supertonic now appears in "+". Add it, Preview F1 in English and
   Arabic, and Set as Lucy's voice. Audio plays.
8. Set the model Unavailable. On the Voice page, Supertonic shows **model unavailable**. Preview
   fails with that reason. A chat reply is spoken by the next provider, and the server log shows a
   `VoiceProviderFailoverEvent`. No restart was needed (FR-038).

## 4. Guardrails (SC-002) — each must be refused **before** any job starts

| Source / Destination | Expected |
|---|---|
| `https://evil.example/Supertone/supertonic-3` | 400 source: "must be a huggingface.co model URL" |
| `https://huggingface.co@evil.example/x/y` | 400 source |
| `https://huggingface.co/datasets/x/y` | 400 source: "not a model repository" |
| destination `../etc` · `Models/../../x` · `/Models/x` · `Models\x` · `Models/%2e%2e/x` · `Models/x/CON` | 400 destination |
| destination `Models` (the prefix itself) · `wwwroot/x` · `` (empty) | 400 destination: reserved location |

A repository that contains `web.config` must fail during Listing with `ReservedFileName`, after
zero bytes have been uploaded.

## 5. Failure visibility (SC-003, SC-005, SC-006)

- **Wrong password** (edit the untracked Development settings): the job fails within about 10 s
  with "the deployment server rejected the login". Check that the password does not appear in the
  response, the failure reason, the stored records, or the server log (SC-006):
  - `grep -c "<the password>" logs/*.log` returns 0.
  - `SELECT COUNT(*) FROM CustomModels WHERE FailureReason LIKE '%<the password>%' OR SourceUrl LIKE '%<the password>%'`
    returns 0, and the same check on `CustomModelOverwrittenFiles.RelativePath` returns 0.
- **Size cap**: set `CustomModels:MaxDeploymentBytes: 1000000` and deploy supertonic-3. The job
  fails during Listing with "415 MB exceeds the 1 MB limit", after zero bytes have been uploaded.
- **Cancel** mid-transfer: the state is `Cancelled` within 5 s, and
  `App_Data/Temp/custom-models/{id}` no longer exists (SC-005).
- **Restart mid-transfer** (recycle the app pool): after boot the row is `Failed` with "interrupted
  by a server restart", and its temporary folder is gone.
- **Hub drop** (stop the network in DevTools): the "Live updates disconnected" banner shows. On
  reconnect, the row catches up.

## 6. Permissions (FR-025, FR-026)

Create a custom role with only `admin.custom-models.view`. That user sees the AI Providers nav
entry, and the page shows **only** the Custom Models section, with no 403 toast. Add model, Cancel,
the availability switch and Remove are hidden. Calling `POST /api/v1/admin/custom-models` directly
returns 403. A role holding only `admin.ai-providers.*` does not see the section, and in DevTools
its connection to `/hubs/custom-model-deployments` is refused (FR-026).

## 7. CI deploy safety (research D10)

After §3, push any commit to `main` and let the deploy run. Then check
`/hydra/Models/supertonic-3/` over FTP: all 38 files are still there, and the voice still
works.

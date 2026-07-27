# Quickstart: Validating the Legacy Modernization Migration

This is a validation guide, not an implementation guide — it proves the migration satisfies `spec.md`'s Acceptance Criteria and Success Criteria end-to-end. See `data-model.md` for entity details and `contracts/api-v1.md` for endpoint shapes.

## Prerequisites

- .NET 10 SDK, Node.js (matching the frontend's `engines` once scaffolded), SQL Server (local instance or Testcontainers), Docker only if used to run a local SQL Server/Testcontainers dependency (not for hosting the app itself — see `plan.md` § Constitution Check).
- A restored copy of the production database for rehearsing the `UserChats` PK migration (`research.md` Topic 5) before it ever touches production.
- Valid OpenAI API key in local user-secrets/environment (never in `appsettings.json`).

## 1. Stand up the new backend alongside the legacy app

```bash
dotnet build "Ask Lucy.sln"          # legacy app still builds and runs, unchanged
dotnet build src/AskLucy.WebAPI       # new Clean Architecture backend
dotnet ef database update --project src/AskLucy.Persistence --startup-project src/AskLucy.WebAPI
dotnet run --project src/AskLucy.WebAPI
```

Expected: both the legacy app and the new WebAPI run independently; the new WebAPI's `/swagger` shows the `contracts/api-v1.md` endpoints.

## 2. Stand up the new frontend

```bash
cd frontend
npm install
npm run dev
```

Expected: the React app serves the chat UI at the dev URL, calling the new `/api/v1/*` endpoints (CORS allow-listed to this exact origin, per `research.md` Topic 7).

## 3. Run the automated test suites

```bash
dotnet test                                   # unit + integration (Testcontainers-backed) tests
cd frontend && npm run test                   # Vitest + React Testing Library
npx playwright test                           # end-to-end regression matrix
```

Expected: all suites pass; CI (`.github/workflows`) runs the same commands and blocks merge on failure (FR-029, User Story 5).

## 4. Validate feature parity (User Story 1, SC-001)

Using an existing (or freshly seeded, non-production) test account with 2FA enabled:

1. Log in — confirm the TOTP challenge appears and succeeds without re-enrollment.
2. Send a chat message — confirm a streamed response begins rendering within 2 seconds P95 (SC-006).
3. Generate an image, request a translation, upload an audio file for transcription — confirm each returns the same category of result as the legacy app.
4. Upload a PDF and confirm client-side text extraction still works (no server round-trip for this step).
5. Use voice input/output — confirm the browser's native speech recognition/synthesis still drives the chat.
6. Create, rename, and delete a saved chat — confirm all three succeed and the deleted chat disappears from the list (FR-033) without affecting other users' chats.
7. Switch light/dark theme and resize the browser — confirm both still work.

## 5. Validate the closed security gaps (User Stories 2–4, SC-003/SC-004/SC-005)

1. Call `POST /api/v1/ai/chat` with no `Authorization` header — expect `401`.
2. As User A, request User B's chat via `GET /api/v1/chats/{userBsChatId}` — expect `403`/`404`, never the chat.
3. As a non-admin authenticated user, call `GET /api/v1/users` — expect `403`.
4. Inspect any `/api/v1/users*` response body — confirm no `passwordHash`/`securityStamp`/`concurrencyStamp` field is present.
5. As an Administrator/Super User, repeat step 3 — expect success, same data an admin could see before migration.

## 6. Validate data integrity (SC-009)

Run the rehearsed `UserChats` migration against a restored production copy; confirm row count and `Title`/owner mapping match the pre-migration source exactly, and that every existing user's avatar is retrievable via the new signed-URL endpoint.

## 7. Validate CI/CD (User Story 5, SC-007)

Open a pull request with an intentionally failing test — confirm the GitHub Actions pipeline blocks the merge. Merge a passing change — confirm it deploys to the existing `site4now.net` target automatically with no manual publish step.

## Done

The migration is validated when every check above passes and `specs/000-legacy-modernization/spec.md` § Acceptance Criteria is fully checked off.

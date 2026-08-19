# Quickstart: Immersive Viewer Platform for AI-Assisted Urban Design

Manual + scripted validation guide for this feature once implemented. See
[data-model.md](./data-model.md) and [contracts/](./contracts/) for the shapes referenced below.

## Prerequisites

- Backend running locally (`dotnet run --project src/AskLucy.Web`) with a valid `Weather` config
  section (research.md Decision 6 — no API key needed for the default keyless provider; if a paid
  provider is later configured, its key must be set via `dotnet user-secrets` per constitution §8, not
  in `appsettings.Development.json`).
- Frontend dev server running (`npm run dev` in `src/AskLucy.Web/ClientApp`), authenticated session.
- A Google Maps Platform API key restricted to `localhost`/the deployed domain (research.md Decision 3),
  set via the frontend's existing env-var mechanism (not committed).
- Browser with geolocation support; test both **granted** and **denied** permission states (most
  browsers let you reset a site's permission from the address-bar padlock).

## Scenario 1 — First load, location granted (US1, US2, US4)

1. Clear the site's geolocation permission, then navigate to `/studio`.
2. **Expect**: the viewer fills the majority of the viewport immediately, showing the static
   placeholder background (not the decorative sphere — confirm the small corner presence card,
   bottom-left, is showing its own sphere independently and unaffected — FR-004, SC-007).
3. Approve the browser's location permission prompt.
4. **Expect** (within ~5s, SC-002/SC-003): the viewer transitions to a Google Maps view centered on
   your resolved location; the weather widget appears with a location name, temperature, and a
   condition icon matching the reference layout.
5. Reload the page. **Expect**: the viewer goes through the placeholder → map transition again (no
   persisted location/weather — FR-012b; nothing in `localStorage`/network tab should show a save
   call for location or weather).

## Scenario 2 — Location denied (US2-AC3, FR-008, SC-005)

1. Deny (or reset-and-ignore) the geolocation permission prompt.
2. **Expect**: the viewer stays on the placeholder background indefinitely; no weather widget appears;
   no error toast/snackbar anywhere on the page; the rest of the workspace (chat, toolbar, presence
   card) remains fully usable.
3. Open devtools console — confirm no unhandled promise rejections, and at most a `console.error` for
   telemetry (per the documented no-silent-failures carve-out in plan.md), never a thrown/unhandled
   error.

## Scenario 3 — Camera controls (US3)

1. With any content active (placeholder or map), select the isometric/plan toggle in the toolbar.
   **Expect**: camera perspective changes immediately (<300ms, SC-004), content stays correctly
   oriented (FR-013/AC2).
2. Select the rotation toggle. **Expect**: auto-rotation stops immediately and holds orientation
   (FR-015). Toggle again — rotation resumes smoothly, not a jump-cut (AC4).
3. With a system/browser "reduce motion" preference enabled, reload. **Expect**: rotation starts
   already stopped (FR-016/SC-008).

## Scenario 4 — Weather widget resilience (US4-AC4)

1. With location granted and the widget showing, simulate the weather endpoint failing (e.g. block
   `GET /api/v1/weather/current` in devtools' network conditions, or stop the backend).
2. Trigger a refresh cycle (or wait for the periodic refresh interval).
3. **Expect**: the widget either shows the last-known reading with a visible "stale" indicator, or
   disappears — never a blank/broken/infinitely-loading widget.

## Scenario 5 — Programmatic viewer API, no AI agent involved (US6, SC-006)

Run directly in the browser devtools console against the mounted viewer engine instance (exposed for
this exact purpose during development — see `contracts/viewer-engine-api.md`):

```ts
viewerEngine.setViewMode('plan')                       // → { ok: true }
viewerEngine.zoomToLocation(51.5074, -0.1278, 12)       // → { ok: true }
viewerEngine.select('gis-current-location', 'marker')   // → { ok: true }, fires `selectionChanged`
viewerEngine.select('does-not-exist', 'x')               // → { ok: false, error: '...' }
viewerEngine.setRotationEnabled(false)                   // → { ok: true }, fires `rotationChanged`
```

**Expect**: every call resolves with a clear `ok`/`error` outcome, and the corresponding `viewerEvent`
fires for each successful state change — proving the command/event contract works end-to-end with
zero AI-agent code involved (matches spec's explicit scope boundary: contract established and
exercised directly, agent integration deferred).

## Scenario 6 — Weather API contract, direct HTTP (backend-only check)

```bash
curl -H "Authorization: Bearer <token>" \
  "https://localhost:<port>/api/v1/weather/current?latitude=51.5074&longitude=-0.1278"
```

**Expect**: `200` with the `WeatherSnapshotDto` shape from `contracts/weather-api.md`. Then try
`latitude=999` — **expect** `400` Problem Details (`.../problems/validation-failed`).

## Automated test suites

```bash
# Backend
dotnet test tests/AskLucy.Application.Tests --filter FullyQualifiedName~Weather
dotnet test tests/AskLucy.Infrastructure.Tests --filter FullyQualifiedName~Weather
dotnet test tests/AskLucy.Web.Tests --filter FullyQualifiedName~Weather

# Frontend
cd src/AskLucy.Web/ClientApp
npm run test -- viewer          # unit/component/a11y tests under viewer/ and features/viewer/

# E2E
npx playwright test tests/AskLucy.E2E.Tests/ImmersiveViewerPlatform.spec.ts
```

All suites MUST pass, and the frontend suite MUST include at least one `jest-axe` a11y check for the
two new toolbar controls and the weather widget (constitution §7/§10), matching the existing
`*.a11y.test.tsx` convention.

# Contract: Panel Opacity Preference API

**Feature**: [../spec.md](../spec.md) | **Research**: [../research.md](../research.md) (Decision 6)

Follows the exact REST shape of the existing voice-preference endpoints (`AiController`), applied to
the new `PanelsController` (`src/AskLucy.Web/Controllers/v1/PanelsController.cs`).

## `GET /api/v1/panels/preferences`

**Auth**: `[Authorize]` (constitution §6/§8) — resolves the caller's own preference only, no `userId`
route/query parameter.

**Response 200** (`application/json`):

```json
{ "opacityPercent": 85 }
```

If no `UserPanelPreference` row exists yet for the caller, the handler returns the default
(`opacityPercent: 85`) **without** creating a row — the row is created lazily on first `PUT`, matching
`UserVoicePreference`'s existing "create on first save" convention (avoids writing a row for every
user who never touches this setting).

**Response on failure**: RFC 7807 Problem Details via the existing global exception-handling
middleware — no ad hoc error shape (constitution §6).

## `PUT /api/v1/panels/preferences`

**Auth**: `[Authorize]`.

**Request** (`application/json`):

```json
{ "opacityPercent": 60 }
```

**Validation** (`SaveUserPanelPreferenceCommandValidator`, FluentValidation): `opacityPercent` MUST be
an integer in `[40, 100]` (spec Clarifications Q4 — the readability floor). A value outside that range
returns `400` Problem Details with a field-level validation error, never silently clamped at the API
boundary (the domain-layer clamp in `UserPanelPreference.SetOpacityPercent` is defense-in-depth for
any future internal caller, not the user-facing contract).

**Response 200**: the saved `{ "opacityPercent": 60 }`.

## Frontend consumption

`features/settings/api/panelPreferencesApi.ts` wraps both endpoints (`apiFetch` pattern, matching
`aiPreferencesApi.ts`/`voiceApi.ts`); `viewer/panels/store/panelPreferencesStore.ts` is the sole
caller (data-model.md), so every UI surface (the Settings "Viewer" tab's slider and every open
`FloatingPanel`'s opacity styling) reads one source of truth rather than each fetching independently.

## Verification

```http
GET /api/v1/panels/preferences
→ 200 { "opacityPercent": 85 }   # default, before any save

PUT /api/v1/panels/preferences
Body: { "opacityPercent": 60 }
→ 200 { "opacityPercent": 60 }

GET /api/v1/panels/preferences
→ 200 { "opacityPercent": 60 }   # persisted

PUT /api/v1/panels/preferences
Body: { "opacityPercent": 10 }
→ 400 Problem Details (opacityPercent below the 40 floor)
```

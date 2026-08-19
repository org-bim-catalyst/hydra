# Data Model: AI-to-UI Floating Panel Framework

Phase 1 output for [plan.md](./plan.md). Panel *instances* and their layout are client-session state
only (spec Assumption — not persisted server-side). The one server-persisted piece is the user's
opacity preference. Where an entity below corresponds to a spec Key Entity, the spec name is noted.

## Client-side state (`viewer/panels/store/floatingPanelStore.ts`)

Session-scoped only (no `persist` middleware — research.md Decision 5), following the
`workspaceOverlayStore`/`viewerEngineStore` convention: every visit to the viewer starts with no
panels open.

### FloatingPanel (spec: *Floating Panel*)

| Field | Type | Notes |
|---|---|---|
| `id` | `string` | Server-supplied `requestId` if provided, else client-generated (uuid). |
| `typeKey` | `string` | Key into `PanelTypeRegistry` (FR-001). |
| `title` | `string` | Display title from the `PanelRequest`. |
| `data` | `unknown` (validated) | Raw payload before validation; see `validationStatus`. |
| `validationStatus` | `'valid' \| 'invalid' \| 'unknown-type'` | Set once at creation from `PanelTypeRegistry.resolve(typeKey)` + the resolved type's zod schema (research.md Decision 4). Drives FR-016/FR-017's fallback rendering. |
| `position` | `{ x: number; y: number }` | Top-left, in viewer-surface-relative pixels. Assigned via the cascade algorithm (FR-021) when the request omits one. |
| `size` | `{ width: number; height: number }` | Initialized from `PanelTypeDefinition.defaultSize`. |
| `resizable` | `boolean` | Copied from `PanelTypeDefinition.resizable` at creation (FR-005). |
| `minimized` | `boolean` | Default `false` (FR-006). |
| `zOrder` | `number` | Monotonically increasing "last focused" counter; highest = frontmost (FR-009). |
| `lastFocusedAtUtc` | `number` (epoch ms) | Drives LRU eviction (FR-022) — the panel with the oldest value is closed first when the max-panel-count is exceeded. |
| `opacityOverride` | `number \| null` | Always `null` in this feature (spec Assumption: opacity is a single global preference, not per-panel) — field exists so a later per-panel override doesn't require a shape change. |
| `contextAssociation` | `ViewerContextAssociation \| null` | See below. |
| `contextStatus` | `'current' \| 'stale' \| 'invalid' \| null` | `null` when `contextAssociation` is `null`. Set to `'stale'`/`'invalid'` by the `ViewerEventBus` subscription (research.md Decision 7, FR-014, US4-AS2). |

### ViewerContextAssociation (spec: *Viewer Context Association*)

| Field | Type | Notes |
|---|---|---|
| `layerId` | `string \| null` | References a `RenderLayer.id` from `viewerEngineStore` (spec 027), if the panel relates to a whole layer. |
| `elementId` | `string \| null` | References a specific selectable element within a layer, if more specific than the whole layer. |

At least one of `layerId`/`elementId` is non-null when `contextAssociation` itself is non-null.

### PanelTypeDefinition (spec: *Panel Type Definition*, registered in `viewer/panels/registry.ts`)

| Field | Type | Notes |
|---|---|---|
| `typeKey` | `string` | Unique registry key (e.g. `"chart"`, `"table"`, `"site-analysis"`). |
| `renderer` | `React.ComponentType<{ data: T }>` | The type-specific presentation component. |
| `schema` | `ZodSchema<T>` | Validates/parses `FloatingPanel.data` into `T` (research.md Decision 4). |
| `defaultSize` | `{ width: number; height: number }` | Used when a `PanelRequest` doesn't specify one. |
| `resizable` | `boolean` | FR-005. |

### PanelRequest (spec: *Panel Request* — wire shape, not stored verbatim; see contracts/panel-hub-events.md)

| Field | Type | Notes |
|---|---|---|
| `requestId` | `string` | Becomes `FloatingPanel.id`. |
| `typeKey` | `string` | Looked up in `PanelTypeRegistry`; unresolved key → `validationStatus: 'unknown-type'` (FR-016). |
| `title` | `string` | |
| `data` | `unknown` | Validated against the resolved type's schema on receipt (FR-017). |
| `position` | `{ x: number; y: number } \| null` | Optional — cascade placement applies when omitted (FR-021). |
| `contextAssociation` | `{ layerId?: string; elementId?: string } \| null` | Optional (FR-013). |

### Registry-derived constant

| Constant | Value | Notes |
|---|---|---|
| `MAX_CONCURRENT_PANELS` | `10` | FR-022. Chosen as a round number comfortably above SC-004's 5-panel guarantee while bounding worst-case DOM/`react-rnd` instance count; not user-configurable in this feature (no requirement calls for that). |

## Server-persisted state (`AskLucy.Domain.Panels`)

Only the opacity preference is persisted — everything else in this feature is client-session state
(spec Assumption).

### UserPanelPreference (spec: *User Panel Preferences*)

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | Surrogate key (constitution §5). |
| `UserId` | `Guid` | Foreign key to the identity user; unique index (one row per user, created lazily on first save — mirrors `UserVoicePreference`). |
| `OpacityPercent` | `int` | Clamped `[40, 100]` at the domain layer (`SetOpacityPercent`) — spec Clarifications Q4. Default `85` when no row exists yet (client and server agree on this default independently; see contracts/panel-preferences-api.md). |
| `CreatedAtUtc` / `CreatedBy` | `DateTimeOffset` / `string` | Populated by the existing `SaveChanges` interceptor (constitution §5), never set by callers. |
| `ModifiedAtUtc` / `ModifiedBy` | `DateTimeOffset` / `string` | Same. |

### GetUserPanelPreferenceQuery / SaveUserPanelPreferenceCommand (MediatR, `Application/Panels`)

| Member | Type | Notes |
|---|---|---|
| `SaveUserPanelPreferenceCommand.OpacityPercent` | `int` | `SaveUserPanelPreferenceCommandValidator` (FluentValidation) rejects values outside `[40, 100]` with a 400 Problem Details response — the domain clamp is defense-in-depth, the validator is the user-facing rejection (FR-011's "bounded range" is enforced at both layers, matching this codebase's existing double-enforcement convention, e.g. `UserVoicePreference`). |

Both return/consume `UserPanelPreferenceDto { OpacityPercent }`.

## Frontend preference state (`features/settings/api/panelPreferencesApi.ts` + `viewer/panels/store/panelPreferencesStore.ts`)

Mirrors `voicePreferencesStore.ts` exactly (research.md Decision 6): Zustand + `persist` (localStorage
key for instant restore before the server round-trip resolves), `hydrateFromServer()`,
`update(patch)` (optimistic local set, then `PUT`, reverting + setting `error` on failure per
constitution §2.VIII no-silent-failures), consumed by every open `FloatingPanel`'s opacity styling and
by the new Settings "Viewer" tab's slider control.

| Field | Type | Notes |
|---|---|---|
| `opacityPercent` | `number` | `[40, 100]`, default `85` until hydrated. |
| `error` | `string \| null` | Surfaced via Snackbar in the Settings tab on save failure. |

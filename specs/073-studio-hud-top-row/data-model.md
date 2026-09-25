# Data Model: Studio HUD Top Row

**Feature**: [spec.md](spec.md) | **Date**: 2026-09-25

This is a presentation-only change. There is **no backend, API, database, or migration change**, and no persisted state is added or removed.

## Existing client state (read-only for this feature)

| Store | Fields used | Change |
|---|---|---|
| `activeSiteBoundaryStore` | `siteName`, `confidenceLevel: 'high' \| 'medium' \| 'low'` | None. `sourceDetail` and `alternativeCandidateNames` stay in the store, but the card no longer reads them (FR-007). |
| `activeLocationStore` | `latitude`, `longitude` | None. |
| `useCurrentWeather` (TanStack Query) | `data`, `isStale`, `isError` | None. |
| `viewerExtensionStore` | `contributions` | Holds one more contribution kind (below). |

## Extended type: `Contribution` (viewer extension framework)

`viewer/extensions/ViewerExtension.ts`: one union member is added.

```ts
| { kind: 'hudItem'; extensionId: string; component: ComponentType }
```

- **Lifecycle**: identical to `overlay`. It is recorded on `contributeHudItem`, and withdrawn by `removeContributionsFor(extensionId)` on stop or failure. No new withdrawal logic is needed, because the component reference is dropped from the list.
- **Ordering**: rendered in contribution order. Instance keys use the same `extensionId:ordinal` scheme `ExtensionOverlayHost` uses.

## Derived presentation mapping: confidence → visual

A pure lookup, colocated with `SiteBoundaryConfidenceBadge`. It is not stored anywhere.

| `confidenceLevel` | Label (unchanged) | Icon | Semantic palette key |
|---|---|---|---|
| `high` | "High confidence" | shield + check | `success` |
| `medium` | "Medium confidence" | plain shield | `warning` |
| `low` | "Low confidence — approximate" | shield + `!` | `error` |

Tone resolution: light theme → `palette[key].main`; dark theme → `palette[key].light` (research D7).

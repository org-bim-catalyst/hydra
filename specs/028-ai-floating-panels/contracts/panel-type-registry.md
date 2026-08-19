# Contract: Panel Type Registry API

**Feature**: [../spec.md](../spec.md) | **Research**: [../research.md](../research.md) (Decision 1)

Satisfies FR-001, FR-015, FR-016 (User Story 1, AS2/AS3). This is the internal, in-process TypeScript
API (`viewer/panels/registry.ts`) that lets a developer add a new AI-response visual category
(dashboard, chart, table, site-analysis, GIS info, environmental analysis, urban design metrics,
diagram, parameter/control panel, alternative design proposal — spec's listed categories) **without**
modifying `floatingPanelStore`, `FloatingPanel.tsx`, or `PanelHub`. It is not an HTTP API — it lives
entirely in the browser bundle.

## `PanelTypeRegistry`

```ts
import type { ZodSchema } from 'zod'
import type { ComponentType } from 'react'

interface PanelTypeDefinition<T = unknown> {
  typeKey: string
  renderer: ComponentType<{ data: T }>
  schema: ZodSchema<T>
  defaultSize: { width: number; height: number }
  resizable: boolean
}

interface PanelTypeRegistry {
  register<T>(definition: PanelTypeDefinition<T>): void // throws in dev if typeKey already registered (fail-fast on a developer mistake, not a runtime AI-request condition)
  resolve(typeKey: string): PanelTypeDefinition | undefined
}
```

## Registering a new panel type (the extensibility contract)

```ts
// viewer/panels/types/chart/ChartPanel.tsx
import { z } from 'zod'
import { panelTypeRegistry } from '../../registry'

const chartDataSchema = z.object({
  series: z.array(z.object({ label: z.string(), values: z.array(z.number()) })),
  chartKind: z.enum(['bar', 'line']),
})

function ChartPanelRenderer({ data }: { data: z.infer<typeof chartDataSchema> }) {
  /* renders `data` using the existing d3-based charting approach (research.md) */
}

panelTypeRegistry.register({
  typeKey: 'chart',
  renderer: ChartPanelRenderer,
  schema: chartDataSchema,
  defaultSize: { width: 480, height: 360 },
  resizable: true,
})
```

Registration happens once, at module load (a single `viewer/panels/types/index.ts` that imports every
built-in type module for its registration side effect — same "import for side effect" convention
`viewer/layers/gis/GoogleMapsGisLayer.ts` already uses). No other file needs to change to add a type.

## Built-in types shipped with this feature

To prove the registry end-to-end (SC-006) and cover the spec's listed categories with a small,
reusable set of primitives rather than one bespoke renderer per category name, this feature ships:

| `typeKey` | Covers spec categories | `resizable` |
|---|---|---|
| `chart` | Charts, environmental analysis, urban design metrics | Yes |
| `table` | Tables, GIS information, analysis dashboards | Yes |
| `summary` | Design recommendations, site analysis, alternative design proposals (each rendered as a titled key/value + narrative block) | Yes |
| `parameters` | Parameters and controls (form-style inputs) | No — fixed size (spec Assumption: simple control panels may be fixed-size) |

Diagrams and any category not covered by the four primitives above are explicitly deferred (spec
lists them as categories the *architecture* must support, not a requirement that every category ships
a bespoke renderer in v1) — adding one later is exactly the one-file `register()` addition this
contract exists to enable.

## Unknown-type / invalid-data handling

See [panel-hub-events.md](./panel-hub-events.md#validation--error-handling-on-receipt) — `resolve()`
returning `undefined`, or the resolved schema failing to parse `data`, are both caller-visible
`FloatingPanel` states, never a thrown exception that crashes the viewer (FR-016/FR-017).

## Verification (no AI agent required, SC-006)

```ts
panelTypeRegistry.resolve('chart') // → PanelTypeDefinition
panelTypeRegistry.resolve('does-not-exist') // → undefined

// Registering a brand-new type end-to-end, with zero changes to registry.ts,
// floatingPanelStore.ts, or FloatingPanel.tsx:
panelTypeRegistry.register({ typeKey: 'urban-metrics', renderer: UrbanMetricsPanel, schema: urbanMetricsSchema, defaultSize: { width: 420, height: 320 }, resizable: true })
floatingPanelStore.getState().openPanel({ requestId: 'test-1', typeKey: 'urban-metrics', title: 'Test', data: { /* valid per schema */ } })
// → a FloatingPanel with validationStatus: 'valid' appears in the store
```

import { z } from 'zod'

/** contracts/content-vocabulary.md — the vocabulary version. Bumped only when a block kind is
 * added, removed or changes shape; incrementing this is what makes such a change identifiable
 * rather than an incidental drift (spec FR-008). */
export const CONTENT_VOCABULARY_VERSION = 1

/** contracts/action-allowlist.md — the shape every declarative action carries. The allowlist
 * itself (viewer/panels/actions/allowlist.ts) owns which `command` values are actually
 * permitted and what `args` each requires; this schema only fixes the envelope so every block
 * that can carry an action agrees on what one looks like. */
const actionSchema = z.object({
  command: z.string().min(1),
  args: z.record(z.string(), z.unknown()),
})

export type ActionRequest = z.infer<typeof actionSchema>

/** data-model.md "Block" — every collection below is bounded (constitution §7: long lists MUST
 * be virtualized; a bounded input satisfies the same concern without a virtualization
 * dependency, and an unbounded table composed by a model is the realistic way this surface
 * becomes unusable). Exceeding a bound is a schema failure at the server gate (research D1), not
 * a rendering problem. */
const headingBlockSchema = z.object({
  kind: z.literal('heading'),
  text: z.string().min(1).max(200),
  level: z.union([z.literal(1), z.literal(2)]).optional(),
})

const textBlockSchema = z.object({
  kind: z.literal('text'),
  text: z.string().min(1).max(4000),
})

const keyValueItemSchema = z.object({
  label: z.string().min(1).max(100),
  value: z.union([z.string(), z.number(), z.null()]),
  action: actionSchema.optional(),
})

const keyValueBlockSchema = z.object({
  kind: z.literal('keyValue'),
  items: z.array(keyValueItemSchema).min(1).max(100),
})

/** A row's `cells` length is not enforced against `columns.length` here — a mismatch is a
 * per-row rendering concern (data-model.md), not a whole-document validity concern: the row
 * renders what is present and is marked malformed, without failing the block or the document. */
const tableRowSchema = z.object({
  cells: z.array(z.union([z.string(), z.number(), z.null()])),
  action: actionSchema.optional(),
})

const tableBlockSchema = z.object({
  kind: z.literal('table'),
  columns: z.array(z.string()).min(1).max(20),
  rows: z.array(tableRowSchema).max(200),
})

const chartSeriesSchema = z.object({
  label: z.string(),
  values: z.array(z.number()).min(1),
})

const chartBlockSchema = z.object({
  kind: z.literal('chart'),
  chartKind: z.enum(['bar', 'line']),
  labels: z.array(z.string()).optional(),
  series: z.array(chartSeriesSchema).min(1).max(10),
})

const metricBlockSchema = z.object({
  kind: z.literal('metric'),
  label: z.string().min(1).max(100),
  value: z.union([z.string(), z.number()]),
  unit: z.string().max(20).optional(),
  action: actionSchema.optional(),
})

/** `fileId` resolves only through the platform's existing file-access mechanism and its
 * entitlement checks (research D10) — an arbitrary external address is rejected by this schema,
 * not merely left unrendered, because an unconstrained URL in model-composed content is both an
 * exfiltration channel and a request-forgery vector. */
const imageBlockSchema = z.object({
  kind: z.literal('image'),
  fileId: z.string().min(1),
  alt: z.string().min(1).max(300),
})

const dividerBlockSchema = z.object({
  kind: z.literal('divider'),
})

/** The strict, per-block schema — every kind's exact shape. Used to validate **one block at a
 * time**, always at render (`ContentRenderer`), never against the whole document at once. A zod
 * `discriminatedUnion` fails its *entire* array the moment one element doesn't match any member,
 * which is the opposite of what contracts/content-vocabulary.md's degradation rules need: one
 * malformed or unrecognised block must show a visible placeholder while every sibling still
 * renders (spec User Story 4). Keeping this schema block-at-a-time, and validating the document
 * envelope separately and loosely (`panelContentSchema` below), is what makes that possible. */
export const blockSchema = z.discriminatedUnion('kind', [
  headingBlockSchema,
  textBlockSchema,
  keyValueBlockSchema,
  tableBlockSchema,
  chartBlockSchema,
  metricBlockSchema,
  imageBlockSchema,
  dividerBlockSchema,
])

export type Block = z.infer<typeof blockSchema>
export type HeadingBlock = z.infer<typeof headingBlockSchema>
export type TextBlock = z.infer<typeof textBlockSchema>
export type KeyValueBlock = z.infer<typeof keyValueBlockSchema>
export type KeyValueItem = z.infer<typeof keyValueItemSchema>
export type TableBlock = z.infer<typeof tableBlockSchema>
export type TableRow = z.infer<typeof tableRowSchema>
export type ChartBlock = z.infer<typeof chartBlockSchema>
export type ChartSeries = z.infer<typeof chartSeriesSchema>
export type MetricBlock = z.infer<typeof metricBlockSchema>
export type ImageBlock = z.infer<typeof imageBlockSchema>
export type DividerBlock = z.infer<typeof dividerBlockSchema>

/** A block that has not yet been validated against its own kind's strict schema — only that it
 * is an object carrying a `kind` string. This, not `Block`, is what a `PanelContent` document
 * actually carries until `ContentRenderer` parses each entry individually. */
const looseBlockEnvelopeSchema = z.object({ kind: z.string() }).passthrough()

export type LooseBlock = z.infer<typeof looseBlockEnvelopeSchema>

/** data-model.md "Panel Content" — the document a content panel renders. Deliberately loose at
 * the block level: this is the whole-document gate (server-side, via
 * contracts/panel-content.schema.json declared as `PresentPanelContentCapability`'s
 * `InputSchemaJson`; client-side, at `floatingPanelStore.openPanel`), and its job is only to
 * enforce the document-level rules — `blocks` is capped at 50 (constitution §7) and must contain
 * at least one entry, so a document with none is refused before a panel ever opens (research D11,
 * spec FR-030) — not to validate individual block shapes, which is `blockSchema`'s job at render
 * time (see its own doc comment above). */
export const panelContentSchema = z.object({
  version: z.literal(CONTENT_VOCABULARY_VERSION),
  blocks: z.array(looseBlockEnvelopeSchema).min(1).max(50),
})

export type PanelContent = z.infer<typeof panelContentSchema>

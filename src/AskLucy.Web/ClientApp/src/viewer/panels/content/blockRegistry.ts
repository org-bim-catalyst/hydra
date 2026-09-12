import type { JSX } from 'react'
import type {
  Block,
  ChartBlock as ChartBlockData,
  HeadingBlock as HeadingBlockData,
  ImageBlock as ImageBlockData,
  KeyValueBlock as KeyValueBlockData,
  MetricBlock as MetricBlockData,
  TableBlock as TableBlockData,
  TextBlock as TextBlockData,
} from './blocks'
import { ChartBlockRenderer } from './blocks/ChartBlock'
import { DividerBlockRenderer } from './blocks/DividerBlock'
import { HeadingBlockRenderer } from './blocks/HeadingBlock'
import { ImageBlockRenderer } from './blocks/ImageBlock'
import { KeyValueBlockRenderer } from './blocks/KeyValueBlock'
import { MetricBlockRenderer } from './blocks/MetricBlock'
import { TableBlockRenderer } from './blocks/TableBlock'
import { TextBlockRenderer } from './blocks/TextBlock'

/** contracts/content-vocabulary.md — the kind→renderer map `ContentRenderer` dispatches through.
 * Internal to `content/`, distinct from the public `registry.ts` (which now holds only
 * registered *live* panel kinds, specs/049 FR-022) — this map is fixed at build time and never
 * grows at runtime, because growing the content vocabulary is a deliberate, versioned change
 * (contracts/content-vocabulary.md "Extending the vocabulary"), not a registration call site.
 *
 * Each entry narrows `Block` to the concrete shape its own renderer expects. The cast is safe
 * because `ContentRenderer` only ever looks a renderer up by `block.kind` immediately before
 * calling it with that same `block` — the map's key and the value it dispatches to are never
 * separated. */
export const blockRegistry: Record<Block['kind'], (props: { block: Block }) => JSX.Element> = {
  heading: ({ block }) => HeadingBlockRenderer({ block: block as HeadingBlockData }),
  text: ({ block }) => TextBlockRenderer({ block: block as TextBlockData }),
  keyValue: ({ block }) => KeyValueBlockRenderer({ block: block as KeyValueBlockData }),
  table: ({ block }) => TableBlockRenderer({ block: block as TableBlockData }),
  chart: ({ block }) => ChartBlockRenderer({ block: block as ChartBlockData }),
  metric: ({ block }) => MetricBlockRenderer({ block: block as MetricBlockData }),
  image: ({ block }) => ImageBlockRenderer({ block: block as ImageBlockData }),
  divider: () => DividerBlockRenderer(),
}

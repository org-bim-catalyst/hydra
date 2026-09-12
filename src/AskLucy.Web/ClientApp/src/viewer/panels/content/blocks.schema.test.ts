/// <reference types="node" />
// Node built-ins, scoped to this file only via the reference directive above — this test runs
// under vitest's Node environment, unlike every other test in `viewer/panels/`, which is why the
// ambient Node types aren't part of tsconfig.app.json's global `types` list.
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { describe, expect, it } from 'vitest'
import { z } from 'zod'
import { panelContentSchema } from './blocks'

/** research.md D2 — the entire drift-prevention mechanism for this feature's one tracked
 * complexity (plan.md Complexity Tracking): the block vocabulary is defined once in zod
 * (blocks.ts) and the server declares a generated JSON Schema artifact as its `InputSchemaJson`
 * (contracts/panel-content.schema.json, consumed by PresentPanelContentCapability). This test
 * regenerates that artifact from the current zod source and fails if it no longer matches the
 * committed file — converting silent drift into a failing build. When the vocabulary
 * legitimately changes, regenerate and recommit the artifact rather than editing it by hand. */
describe('panel content vocabulary schema parity', () => {
  it('matches the committed JSON Schema artifact the backend declares', () => {
    const generated = z.toJSONSchema(panelContentSchema)
    const committedPath = resolve(
      process.cwd(),
      '../../../specs/049-panel-content-model/contracts/panel-content.schema.json',
    )
    const committed = JSON.parse(readFileSync(committedPath, 'utf-8')) as unknown

    expect(generated).toEqual(committed)
  })
})

/** data-model.md "Element" (spec FR-027, FR-028, FR-031) — read from the content as supplied,
 * never platform-managed (spec Out of Scope). `hasProperties: false` is an explicit marker, never
 * an empty object standing in for "nothing to show" (FR-031). */
export interface ElementProperties {
  hasProperties: boolean
  properties: Record<string, unknown> | null
}

/** A single content item's element index — built once when its content loads (research D7). */
export type ElementIndex = Map<string, Record<string, unknown>>

/** Input shape a content loader supplies per addressable node — deliberately minimal so any
 * future loader (not just glTF) can produce it without depending on that format's own types. */
export interface IndexableNode {
  elementId: string
  properties: Record<string, unknown>
}

/** Builds an `ElementIndex` from whatever addressable nodes a content loader found (specs/051
 * FR-027–031). Called by `content/loaders/gltfContentLoader.ts` (T027) once, when content loads —
 * implemented here, in Foundational, so US1's loader and US3's `getElementInfo`/`selectAndFrame`
 * both depend on one already-built module rather than US1 depending on US3's later work
 * (resolved during `/speckit-analyze`, see tasks.md T023a). */
export function buildElementIndex(nodes: IndexableNode[]): ElementIndex {
  const index: ElementIndex = new Map()
  for (const node of nodes) {
    index.set(node.elementId, node.properties)
  }
  return index
}

/** FR-028/FR-031 — returns the element's properties, or an explicit "nothing to show" marker.
 * Never an empty object for "no properties" (FR-031's own forbidden case). */
export function getElementProperties(index: ElementIndex | undefined, elementId: string): ElementProperties {
  const properties = index?.get(elementId)
  if (!properties) {
    return { hasProperties: false, properties: null }
  }
  return { hasProperties: true, properties }
}

/** data-model.md "Element" (spec FR-030) — mirrors `viewer/selection/resolveSelection.ts`'s exact
 * convention, extended with a second tiebreaker: content from more than one capability drawing at
 * the same screen point resolves by drawing-space acquisition order first (the same "later
 * acquired renders on top" rule `DrawingSpaceRegistry`/`ExtensionToolbar` already use, FR-015),
 * then by distance to the camera within a single drawing space. Ties resolve to the last candidate
 * in input order, standing in for "most recently added" — the same fallback
 * `resolveSelection.ts` already uses. */
export interface ElementOverlapCandidate {
  layerId: string
  elementId: string
  /** The acquisition-order index of the drawing space this element was drawn through (higher =
   * acquired later = rendered on top, per FR-015). */
  drawingSpaceOrder: number
  /** Metres from the active camera — only compared within the same `drawingSpaceOrder`. */
  distanceToCamera: number
}

export function resolveElementOverlap(candidates: ElementOverlapCandidate[]): ElementOverlapCandidate | null {
  if (candidates.length === 0) return null

  return candidates.reduce((winner, candidate) => {
    if (candidate.drawingSpaceOrder > winner.drawingSpaceOrder) return candidate
    if (candidate.drawingSpaceOrder < winner.drawingSpaceOrder) return winner
    // Same drawing space — nearest to the camera wins. Equal distance falls through to "last in
    // input order wins," matching resolveSelection.ts's own tie-breaking convention.
    return candidate.distanceToCamera <= winner.distanceToCamera ? candidate : winner
  })
}

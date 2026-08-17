export interface SelectionCandidate {
  layerId: string
  elementId: string
  zIndex: number
}

/** spec.md Edge Cases: "when two overlapping pieces of content are both eligible for
 * selection... the system MUST resolve selection deterministically (e.g., topmost/foreground
 * content wins) rather than selecting an unpredictable or empty target." Ties (equal `zIndex`)
 * resolve to the last candidate in input order, standing in for "most recently added" until a
 * real stacking order is layer-defined. */
export function resolveSelection(candidates: SelectionCandidate[]): SelectionCandidate | null {
  if (candidates.length === 0) return null
  return candidates.reduce((topmost, candidate) => (candidate.zIndex >= topmost.zIndex ? candidate : topmost))
}

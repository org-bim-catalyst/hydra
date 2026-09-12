import './builtin/panelsExtension'
import './builtin/poiMarkerExtension'
import './builtin/boundaryConfidenceExtension'
import './builtin/siteBoundaryExtension'

/** data-model.md "Declared Extension Set" — the ordered list of extension ids the viewer starts
 * when it opens. Changing what the viewer does means changing this list, not the viewer. Fixed in
 * the application — not per-user, not per-tenant, no management UI (spec Out of Scope). Order
 * matters only for predictable start sequencing and stable control ordering (FR-022); no
 * extension may depend on another having started. */
export const DECLARED_EXTENSIONS: readonly string[] = [
  'viewer.panels',
  'viewer.poi-marker',
  'viewer.boundary-confidence',
  'viewer.site-boundary',
]

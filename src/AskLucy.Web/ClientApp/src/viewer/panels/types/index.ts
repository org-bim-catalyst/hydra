// specs/049-panel-content-model retired the four built-in panel types this module used to
// register at import time (chart, table, parameters, summary — now content blocks, see
// viewer/panels/content/). Nothing registers here any more: `registry.ts` now holds only "live"
// panel kinds (spec FR-022), and those are registered by whatever owns them (extensions, per
// specs/050) rather than by a side-effect import barrel.
export {}

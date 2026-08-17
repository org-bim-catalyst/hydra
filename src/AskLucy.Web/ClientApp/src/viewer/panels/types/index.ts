/** contracts/panel-type-registry.md — importing this module registers every built-in panel type
 * (side-effect import, one `panelTypeRegistry.register()` call per module, matching the
 * import-for-side-effect convention used elsewhere in `viewer/`). Adding a new built-in type is a
 * one-line addition here plus its own module — nothing else in the panel framework changes. */
import './chart/ChartPanel'
import './table/TablePanel'
import './parameters/ParametersPanel'
import './summary/SummaryPanel'

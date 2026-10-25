import { z } from 'zod'

/**
 * The type keys and `data` schemas the solar panels register with the viewer.
 *
 * They live beside the components rather than inside them so each panel module exports nothing
 * but its component: Vite's Fast Refresh can only hot-swap a module whose exports are all
 * components, and a module that also exports a constant forces a full reload on every edit.
 */

export const SOLAR_TIME_CONTROL_TYPE_KEY = 'solar.time-control'

/** contracts/solar-panels.md — this live panel is driven entirely by `solarAnalysisStore`
 * (research D12: "interactive code with their own state", which is exactly why it is a live
 * panel and not content). `data` carries nothing the store doesn't already own; the schema exists
 * only to satisfy `PanelTypeDefinition`'s contract. */
export const solarTimeControlDataSchema = z.object({})
export type SolarTimeControlData = z.infer<typeof solarTimeControlDataSchema>

export const SOLAR_CORRECTIONS_TYPE_KEY = 'solar.corrections'

export const solarCorrectionsDataSchema = z.object({})
export type SolarCorrectionsData = z.infer<typeof solarCorrectionsDataSchema>

export const SOLAR_DAY_LENGTH_TYPE_KEY = 'solar.day-length'

/** specs/085 — like the two panels above, this one is driven by `solarAnalysisStore`: the site and
 * the selected date come from there, and the year profile is computed from them. `data` carries
 * nothing, and the schema exists only to satisfy `PanelTypeDefinition`'s contract. The panel is
 * read-only; it writes nothing back. */
export const solarDayLengthDataSchema = z.object({})
export type SolarDayLengthData = z.infer<typeof solarDayLengthDataSchema>

import { create } from 'zustand'
import { copy } from '../copy'

/** data-model.md "Correction" — session-scoped, keyed by site (research D11). Not persisted:
 * cleared on reload, exactly as the spec's Out of Scope requires. */
export interface SiteCorrections {
  buildingHeights: Record<string, number>
  groundOffsetMetres: number
}

const MAX_HEIGHT_METRES = 1000
const MAX_GROUND_OFFSET_METRES = 500

function emptyCorrections(): SiteCorrections {
  return { buildingHeights: {}, groundOffsetMetres: 0 }
}

export interface CorrectionValidationResult {
  ok: boolean
  /** Present only when `ok` is false — the stated reason, drawn from `copy.ts` (FR-027). */
  reason: string | null
}

interface CorrectionsState {
  bySiteKey: Record<string, SiteCorrections>

  getFor: (siteKey: string) => SiteCorrections
  /** FR-025, FR-027 — rejects an invalid height, keeping the previous value, and returns why. */
  setBuildingHeight: (siteKey: string, buildingId: string, heightMetres: number) => CorrectionValidationResult
  /** FR-026, FR-027 — rejects an invalid ground offset, keeping the previous value. */
  setGroundOffset: (siteKey: string, groundOffsetMetres: number) => CorrectionValidationResult
  /** Restores source values for one site (contracts/solar-panels.md "Reset"). */
  reset: (siteKey: string) => void
}

/** FR-027 — height must be > 0 and <= 1000 m; ground offset must be within ±500 m. */
export function validateHeight(heightMetres: number): CorrectionValidationResult {
  if (!Number.isFinite(heightMetres) || heightMetres <= 0 || heightMetres > MAX_HEIGHT_METRES) {
    return { ok: false, reason: copy.invalidHeight }
  }
  return { ok: true, reason: null }
}

export function validateGroundOffset(groundOffsetMetres: number): CorrectionValidationResult {
  if (!Number.isFinite(groundOffsetMetres) || Math.abs(groundOffsetMetres) > MAX_GROUND_OFFSET_METRES) {
    return { ok: false, reason: copy.invalidGroundOffset }
  }
  return { ok: true, reason: null }
}

/** research D11 — a Zustand store mapping a site key to that site's corrections. Deliberately
 * NOT persisted (no `persist` middleware) — corrections apply to the session and the site they
 * were made on, per the spec's Out of Scope and Assumption. */
export const useCorrectionsStore = create<CorrectionsState>()((set, get) => ({
  bySiteKey: {},

  getFor: (siteKey) => get().bySiteKey[siteKey] ?? emptyCorrections(),

  setBuildingHeight: (siteKey, buildingId, heightMetres) => {
    const validation = validateHeight(heightMetres)
    if (!validation.ok) return validation

    set((s) => {
      const existing = s.bySiteKey[siteKey] ?? emptyCorrections()
      return {
        bySiteKey: {
          ...s.bySiteKey,
          [siteKey]: { ...existing, buildingHeights: { ...existing.buildingHeights, [buildingId]: heightMetres } },
        },
      }
    })
    return { ok: true, reason: null }
  },

  setGroundOffset: (siteKey, groundOffsetMetres) => {
    const validation = validateGroundOffset(groundOffsetMetres)
    if (!validation.ok) return validation

    set((s) => {
      const existing = s.bySiteKey[siteKey] ?? emptyCorrections()
      return { bySiteKey: { ...s.bySiteKey, [siteKey]: { ...existing, groundOffsetMetres } } }
    })
    return { ok: true, reason: null }
  },

  reset: (siteKey) => {
    set((s) => {
      const next = { ...s.bySiteKey }
      delete next[siteKey]
      return { bySiteKey: next }
    })
  },
}))

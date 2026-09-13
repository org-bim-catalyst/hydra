import { beforeEach, describe, expect, it } from 'vitest'
import { copy } from '../copy'
import { useCorrectionsStore } from './correctionsStore'

const SITE_A = '25.200000,55.300000'
const SITE_B = '51.500000,-0.100000'

beforeEach(() => {
  useCorrectionsStore.setState({ bySiteKey: {} })
})

describe('correctionsStore — keyed by site, no leakage (FR-028, research D11)', () => {
  it('a correction set for one site does not appear under another', () => {
    useCorrectionsStore.getState().setBuildingHeight(SITE_A, 'osm_way_1', 42)

    expect(useCorrectionsStore.getState().getFor(SITE_A).buildingHeights.osm_way_1).toBe(42)
    expect(useCorrectionsStore.getState().getFor(SITE_B).buildingHeights.osm_way_1).toBeUndefined()
  })

  it('returns empty corrections for a site that has none', () => {
    const corrections = useCorrectionsStore.getState().getFor(SITE_A)
    expect(corrections.buildingHeights).toEqual({})
    expect(corrections.groundOffsetMetres).toBe(0)
  })
})

describe('correctionsStore — height validation (FR-027)', () => {
  it('accepts a valid height', () => {
    const result = useCorrectionsStore.getState().setBuildingHeight(SITE_A, 'osm_way_1', 50)
    expect(result.ok).toBe(true)
    expect(useCorrectionsStore.getState().getFor(SITE_A).buildingHeights.osm_way_1).toBe(50)
  })

  it.each([0, -5, 1001])('rejects an invalid height (%s) and keeps the previous value', (invalidHeight) => {
    useCorrectionsStore.getState().setBuildingHeight(SITE_A, 'osm_way_1', 50)
    const result = useCorrectionsStore.getState().setBuildingHeight(SITE_A, 'osm_way_1', invalidHeight)

    expect(result.ok).toBe(false)
    expect(result.reason).toBe(copy.invalidHeight)
    expect(useCorrectionsStore.getState().getFor(SITE_A).buildingHeights.osm_way_1).toBe(50) // unchanged
  })

  it('accepts the boundary value 1000', () => {
    const result = useCorrectionsStore.getState().setBuildingHeight(SITE_A, 'osm_way_1', 1000)
    expect(result.ok).toBe(true)
  })
})

describe('correctionsStore — ground offset validation (FR-027)', () => {
  it('accepts a valid offset', () => {
    const result = useCorrectionsStore.getState().setGroundOffset(SITE_A, 12)
    expect(result.ok).toBe(true)
    expect(useCorrectionsStore.getState().getFor(SITE_A).groundOffsetMetres).toBe(12)
  })

  it.each([501, -501])('rejects an offset outside +/-500 (%s) and keeps the previous value', (invalidOffset) => {
    useCorrectionsStore.getState().setGroundOffset(SITE_A, 5)
    const result = useCorrectionsStore.getState().setGroundOffset(SITE_A, invalidOffset)

    expect(result.ok).toBe(false)
    expect(result.reason).toBe(copy.invalidGroundOffset)
    expect(useCorrectionsStore.getState().getFor(SITE_A).groundOffsetMetres).toBe(5) // unchanged
  })

  it('accepts the boundary values +/-500', () => {
    expect(useCorrectionsStore.getState().setGroundOffset(SITE_A, 500).ok).toBe(true)
    expect(useCorrectionsStore.getState().setGroundOffset(SITE_A, -500).ok).toBe(true)
  })
})

describe('correctionsStore — reset', () => {
  it('restores source values (clears corrections) for one site only', () => {
    useCorrectionsStore.getState().setBuildingHeight(SITE_A, 'osm_way_1', 42)
    useCorrectionsStore.getState().setBuildingHeight(SITE_B, 'osm_way_2', 30)

    useCorrectionsStore.getState().reset(SITE_A)

    expect(useCorrectionsStore.getState().getFor(SITE_A).buildingHeights).toEqual({})
    expect(useCorrectionsStore.getState().getFor(SITE_B).buildingHeights.osm_way_2).toBe(30)
  })
})

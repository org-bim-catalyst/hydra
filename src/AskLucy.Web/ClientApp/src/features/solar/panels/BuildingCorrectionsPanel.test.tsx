import { fireEvent, render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it } from 'vitest'
import { copy } from '../copy'
import { useCorrectionsStore } from '../store/correctionsStore'
import { useSolarAnalysisStore } from '../store/solarAnalysisStore'
import { BuildingCorrectionsPanel } from './BuildingCorrectionsPanel'

const SITE_BUILDING = {
  id: 'osm_way_1',
  ring: [],
  heightMetres: 30,
  heightProvenance: 'assumed' as const,
  name: 'Site Building',
  isSiteBuilding: true,
}

beforeEach(async () => {
  useCorrectionsStore.setState({ bySiteKey: {} })
  useSolarAnalysisStore.setState({ site: null, moment: null, status: 'idle', failureReason: null, buildingsNotice: null, siteBuildings: [] })
  await useSolarAnalysisStore.getState().open(25.2, 55.3)
  useSolarAnalysisStore.getState().setSiteBuildings([SITE_BUILDING], 200)
})

describe('BuildingCorrectionsPanel (US4 scenarios 1-5, contracts/solar-panels.md)', () => {
  it('shows the site building height and its provenance label (FR-011)', () => {
    render(<BuildingCorrectionsPanel />)
    expect(screen.getByText(copy.heightAssumed)).toBeInTheDocument()
  })

  it('shows which site the corrections apply to (FR-028)', () => {
    render(<BuildingCorrectionsPanel />)
    expect(screen.getByText(copy.correctionsApplyToSite('Asia/Dubai'))).toBeInTheDocument()
  })

  it('applies a valid height correction and it is reflected in correctionsStore (FR-025)', () => {
    render(<BuildingCorrectionsPanel />)
    const input = screen.getByLabelText(copy.siteBuildingHeightLabel)
    fireEvent.change(input, { target: { value: '55' } })
    fireEvent.click(screen.getByRole('button', { name: 'Apply' }))

    const site = useSolarAnalysisStore.getState().site!
    expect(useCorrectionsStore.getState().getFor(site.siteKey).buildingHeights[SITE_BUILDING.id]).toBe(55)
  })

  it('rejects an invalid height with a stated reason and keeps the previous value (FR-027)', () => {
    render(<BuildingCorrectionsPanel />)
    const input = screen.getByLabelText(copy.siteBuildingHeightLabel)
    fireEvent.change(input, { target: { value: '-5' } })
    fireEvent.click(screen.getByRole('button', { name: 'Apply' }))

    expect(screen.getByText(copy.invalidHeight)).toBeInTheDocument()
    const site = useSolarAnalysisStore.getState().site!
    expect(useCorrectionsStore.getState().getFor(site.siteKey).buildingHeights[SITE_BUILDING.id]).toBeUndefined()
  })

  it('applies a valid ground offset (FR-026)', () => {
    render(<BuildingCorrectionsPanel />)
    const input = screen.getByLabelText(copy.groundOffsetLabel)
    fireEvent.change(input, { target: { value: '10' } })
    fireEvent.click(screen.getByRole('button', { name: 'Set' }))

    const site = useSolarAnalysisStore.getState().site!
    expect(useCorrectionsStore.getState().getFor(site.siteKey).groundOffsetMetres).toBe(10)
  })

  it('rejects an invalid ground offset and keeps the previous value (FR-027)', () => {
    render(<BuildingCorrectionsPanel />)
    const input = screen.getByLabelText(copy.groundOffsetLabel)
    fireEvent.change(input, { target: { value: '600' } })
    fireEvent.click(screen.getByRole('button', { name: 'Set' }))

    expect(screen.getByText(copy.invalidGroundOffset)).toBeInTheDocument()
    const site = useSolarAnalysisStore.getState().site!
    expect(useCorrectionsStore.getState().getFor(site.siteKey).groundOffsetMetres).toBe(0)
  })

  it('resetting clears corrections for the current site only (US4 scenario 5)', () => {
    const site = useSolarAnalysisStore.getState().site!
    useCorrectionsStore.getState().setBuildingHeight(site.siteKey, SITE_BUILDING.id, 55)
    render(<BuildingCorrectionsPanel />)

    fireEvent.click(screen.getByRole('button', { name: copy.resetLabel }))

    expect(useCorrectionsStore.getState().getFor(site.siteKey).buildingHeights).toEqual({})
  })
})

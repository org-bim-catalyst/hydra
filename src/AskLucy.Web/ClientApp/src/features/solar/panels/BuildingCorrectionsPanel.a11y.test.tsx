import { render } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { beforeEach, describe, expect, it } from 'vitest'
import { useCorrectionsStore } from '../store/correctionsStore'
import { useSolarAnalysisStore } from '../store/solarAnalysisStore'
import { BuildingCorrectionsPanel } from './BuildingCorrectionsPanel'

expect.extend(toHaveNoViolations)

const SITE_BUILDING = {
  id: 'osm_way_1',
  ring: [],
  heightMetres: 30,
  heightProvenance: 'assumed' as const,
  name: 'Site Building',
  isSiteBuilding: true,
}

describe('BuildingCorrectionsPanel accessibility (FR-032, constitution §7 WCAG 2.1 AA)', () => {
  beforeEach(async () => {
    useCorrectionsStore.setState({ bySiteKey: {} })
    useSolarAnalysisStore.setState({ site: null, moment: null, status: 'idle', failureReason: null, buildingsNotice: null, siteBuildings: [] })
    await useSolarAnalysisStore.getState().open(25.2, 55.3)
    useSolarAnalysisStore.getState().setSiteBuildings([SITE_BUILDING], 200)
  })

  it('has no automatically detectable a11y violations', async () => {
    const { container } = render(<BuildingCorrectionsPanel />)
    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })
})

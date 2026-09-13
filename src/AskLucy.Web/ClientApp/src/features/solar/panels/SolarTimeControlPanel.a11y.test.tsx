import { render } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { beforeEach, describe, expect, it } from 'vitest'
import { useSolarAnalysisStore } from '../store/solarAnalysisStore'
import { SolarTimeControlPanel } from './SolarTimeControlPanel'

expect.extend(toHaveNoViolations)

describe('SolarTimeControlPanel accessibility (FR-032, constitution §7 WCAG 2.1 AA)', () => {
  beforeEach(async () => {
    useSolarAnalysisStore.setState({ site: null, moment: null, status: 'idle', failureReason: null, buildingsNotice: null })
    await useSolarAnalysisStore.getState().open(25.2, 55.3)
  })

  it('has no automatically detectable a11y violations', async () => {
    const { container } = render(<SolarTimeControlPanel />)
    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })
})

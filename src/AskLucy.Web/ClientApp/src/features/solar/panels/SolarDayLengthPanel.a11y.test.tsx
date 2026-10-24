import { render } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { describe, expect, it } from 'vitest'
import { siteKeyFor, useSolarAnalysisStore } from '../store/solarAnalysisStore'
import { SolarDayLengthPanel } from './SolarDayLengthPanel'

expect.extend(toHaveNoViolations)

/** specs/085 FR-019, FR-032 — the chart is an enhancement over the text figures, so it must not
 * introduce an accessibility violation of its own: it carries a description, its bands are named
 * in a legend, and the year's extremes and the selected date are text. */
describe('Day-length chart accessibility (FR-019, FR-032, constitution §7 WCAG 2.1 AA)', () => {
  it('has no automatically detectable a11y violations', async () => {
    const site = { latitude: 25.2, longitude: 55.3, timeZoneId: 'Asia/Dubai', timeBasisLabel: 'Asia/Dubai' }
    useSolarAnalysisStore.setState({
      site: { ...site, siteKey: siteKeyFor(site.latitude, site.longitude) },
      moment: {
        instantUtc: new Date('2026-09-25T12:00:00Z'),
        localDate: '2026-09-25',
        localMinuteOfDay: 12 * 60,
        isPlaying: false,
        playbackMinutesPerSecond: 120,
      },
      status: 'ready',
      failureReason: null,
      buildingsNotice: null,
    })

    const { container } = render(<SolarDayLengthPanel />)
    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })
})

import { render } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { describe, expect, it } from 'vitest'
import { ContentRenderer } from '../../../viewer/panels/content/ContentRenderer'
import { daySummary } from '../solar/daySummary'
import { solarPosition } from '../solar/solarPosition'
import { buildSolarFiguresContent } from './solarFiguresContent'

expect.extend(toHaveNoViolations)

/** FR-031 — the figures are panel CONTENT, not a bespoke component, so they inherit
 * `ContentRenderer`'s existing accessibility (constitution §7) rather than needing new a11y
 * surface of their own. Rendered here through the same renderer a live content panel uses. */
describe('Solar figures content accessibility (FR-031, FR-032, constitution §7 WCAG 2.1 AA)', () => {
  it('has no automatically detectable a11y violations', async () => {
    const instant = new Date(Date.UTC(2026, 8, 13, 10, 0))
    const content = buildSolarFiguresContent({
      localDate: '2026-09-13',
      localMinuteOfDay: 14 * 60,
      timeZoneId: 'Asia/Dubai',
      timeBasisLabel: 'Asia/Dubai',
      solarPosition: solarPosition(instant, 25.2, 55.3),
      daySummary: daySummary(instant, 25.2, 55.3),
      siteBuildingHeightAssumed: true,
    })

    const { container } = render(<ContentRenderer content={content} />)
    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })
})

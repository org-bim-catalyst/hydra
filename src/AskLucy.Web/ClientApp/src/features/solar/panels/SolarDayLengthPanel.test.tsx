import { fireEvent, render, screen } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { cleanup } from '@testing-library/react'
import { copy } from '../copy'
import { siteKeyFor, useSolarAnalysisStore } from '../store/solarAnalysisStore'
import { SolarDayLengthPanel } from './SolarDayLengthPanel'

const DUBAI = { latitude: 25.2, longitude: 55.3, timeZoneId: 'Asia/Dubai', timeBasisLabel: 'Asia/Dubai' }

function openAt(localDate: string): void {
  useSolarAnalysisStore.setState({
    site: { ...DUBAI, siteKey: siteKeyFor(DUBAI.latitude, DUBAI.longitude) },
    moment: {
      instantUtc: new Date(`${localDate}T12:00:00Z`),
      localDate,
      localMinuteOfDay: 12 * 60,
      isPlaying: false,
      playbackMinutesPerSecond: 120,
    },
    status: 'ready',
    failureReason: null,
    buildingsNotice: null,
  })
}

beforeEach(() => openAt('2026-09-25'))
afterEach(() => {
  cleanup()
  vi.restoreAllMocks()
})

describe('SolarDayLengthPanel (specs/085 US2, FR-011 … FR-019)', () => {
  it('draws every day of the selected date’s year (FR-011)', () => {
    render(<SolarDayLengthPanel />)

    const canvas = screen.getByRole('img')
    expect(canvas).toHaveAttribute('data-day-count', '365')
  })

  it('marks the selected date and states the latitude and hemisphere (FR-013)', () => {
    render(<SolarDayLengthPanel />)

    expect(screen.getByRole('img')).toHaveAttribute('data-selected-date', '2026-09-25')
    expect(screen.getByText(copy.dayLengthChartCaption(25.2, 'northern'))).toBeInTheDocument()
  })

  it('names every band, so the chart is not read by colour alone (FR-019)', () => {
    render(<SolarDayLengthPanel />)

    for (const label of [
      copy.phaseNightLabel,
      copy.phaseAstronomicalLabel,
      copy.phaseNauticalLabel,
      copy.phaseCivilLabel,
      copy.phaseDayLabel,
    ]) {
      expect(screen.getByText(label)).toBeInTheDocument()
    }
  })

  it('carries a text description for assistive technology (FR-019)', () => {
    render(<SolarDayLengthPanel />)

    expect(screen.getByRole('img')).toHaveAccessibleName(copy.dayLengthChartDescription(2026, 'northern'))
  })

  it('states the year’s longest and shortest day in text (research D13)', () => {
    render(<SolarDayLengthPanel />)

    expect(screen.getByText(new RegExp(`^${copy.longestDayLabel}:`))).toBeInTheDocument()
    expect(screen.getByText(new RegExp(`^${copy.shortestDayLabel}:`))).toBeInTheDocument()
    // A northern-hemisphere site's longest day is in June.
    expect(screen.getByText(new RegExp(`^${copy.longestDayLabel}:`)).textContent).toContain('Jun')
  })

  it('changes nothing when clicked — it is not a control (FR-015)', () => {
    render(<SolarDayLengthPanel />)
    const before = useSolarAnalysisStore.getState()
    const beforeSite = before.site
    const beforeMoment = before.moment

    fireEvent.click(screen.getByRole('img'))
    fireEvent.pointerDown(screen.getByRole('img'))
    fireEvent.keyDown(screen.getByRole('img'), { key: 'ArrowRight' })

    const after = useSolarAnalysisStore.getState()
    expect(after.site).toBe(beforeSite)
    expect(after.moment).toBe(beforeMoment)
  })

  it('renders nothing when there is no site', () => {
    useSolarAnalysisStore.setState({ site: null, moment: null, status: 'idle' })

    const { container } = render(<SolarDayLengthPanel />)

    expect(container).toBeEmptyDOMElement()
  })

  it('renders without throwing where the environment cannot draw (no 2d context)', () => {
    // jsdom has no canvas implementation: the chart is an enhancement, and the text carries the
    // information (FR-019), so the panel must still render.
    expect(HTMLCanvasElement.prototype.getContext).toBeDefined()
    expect(() => render(<SolarDayLengthPanel />)).not.toThrow()
  })
})

// jsdom has no canvas, so the tests above prove the panel renders — not that it draws. This one
// records the paint calls, which is the only way a broken band calculation fails a test.
describe('SolarDayLengthPanel — what it paints', () => {
  it('paints a band for every phase, for every day, and marks the selected date', () => {
    const fills: { style: string; x: number; y: number; width: number; height: number }[] = []
    const texts: { text: string; x: number; y: number }[] = []
    let strokes = 0
    let dashedStroke = false
    let dashes: number[] = []
    const context = {
      fillStyle: '',
      strokeStyle: '',
      lineWidth: 1,
      font: '',
      textAlign: 'left',
      textBaseline: 'alphabetic',
      setTransform: () => {},
      clearRect: () => {},
      beginPath: () => {},
      moveTo: () => {},
      lineTo: () => {},
      setLineDash: (segments: number[]) => { dashes = segments },
      stroke: () => { strokes += 1; dashedStroke = dashes.length > 0 },
      fillRect: (x: number, y: number, width: number, height: number) => {
        fills.push({ style: String(context.fillStyle), x, y, width, height })
      },
      fillText: (text: string, x: number, y: number) => {
        texts.push({ text, x, y })
      },
    }
    vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockReturnValue(context as unknown as CanvasRenderingContext2D)
    vi.spyOn(HTMLCanvasElement.prototype, 'getBoundingClientRect').mockReturnValue({
      width: 300,
      height: 320,
      top: 0,
      left: 0,
      right: 300,
      bottom: 320,
      x: 0,
      y: 0,
      toJSON: () => ({}),
    } as DOMRect)

    render(<SolarDayLengthPanel />)

    // Gridlines span the plot; bands are column-width slivers. Separating them keeps the band
    // assertions about bands.
    const bands = fills.filter((fill) => fill.width < 5)
    const gridlines = fills.filter((fill) => fill.width >= 5)

    // Five phases a day across the year: at least one band per phase per day.
    expect(bands.length).toBeGreaterThanOrEqual(365 * 5)
    expect(new Set(bands.map((fill) => fill.style)).size).toBe(5)
    // Gridlines at 00, 02 … 24, plus the left-hand hour gutter they are labelled in.
    expect(gridlines.length).toBe(13)
    expect(Math.min(...fills.map((fill) => fill.x))).toBe(30)
    // Every band sits inside the chart and has a positive extent.
    for (const fill of fills) {
      expect(fill.width).toBeGreaterThan(0)
      expect(fill.height).toBeGreaterThan(0)
      expect(fill.x).toBeGreaterThanOrEqual(0)
      expect(fill.x + fill.width).toBeLessThanOrEqual(300.001)
      expect(fill.y).toBeGreaterThanOrEqual(0)
      expect(fill.y + fill.height).toBeLessThanOrEqual(321)
    }
    // The hour axis is labelled every two hours, and the selected day's two crossings read as
    // times (06:… and 18:… in Dubai on 25 September) — the grid has readings, not just marks.
    const hourLabels = texts.filter((text) => /^\d{2}$/.test(text.text)).map((text) => text.text)
    expect(hourLabels).toEqual(['00', '02', '04', '06', '08', '10', '12', '14', '16', '18', '20', '22', '24'])
    const crossings = texts.filter((text) => /^\d{2}:\d{2}$/.test(text.text)).map((text) => text.text)
    expect(crossings).toHaveLength(2)
    expect(crossings[0] < crossings[1]).toBe(true)
    // The selected date's marker is the one stroke drawn across the chart, and it is dashed.
    expect(strokes).toBe(1)
    expect(dashedStroke).toBe(true)
  })
})

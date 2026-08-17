import { render } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { panelTypeRegistry } from '../../registry'
import { chartDataSchema } from './ChartPanel'
import './ChartPanel'

describe('chart panel type', () => {
  it('registers itself under the "chart" type key with a resizable default size', () => {
    const definition = panelTypeRegistry.resolve('chart')
    expect(definition).toBeDefined()
    expect(definition?.resizable).toBe(true)
    expect(definition?.defaultSize).toEqual({ width: 480, height: 360 })
  })

  it('accepts valid bar-chart data', () => {
    const result = chartDataSchema.safeParse({
      chartKind: 'bar',
      labels: ['Mon', 'Tue'],
      series: [{ label: 'Exposure', values: [4, 6] }],
    })
    expect(result.success).toBe(true)
  })

  it('rejects data missing required fields', () => {
    const result = chartDataSchema.safeParse({ nonsense: true })
    expect(result.success).toBe(false)
  })

  it('rejects an unsupported chartKind', () => {
    const result = chartDataSchema.safeParse({
      chartKind: 'pie',
      series: [{ label: 'A', values: [1] }],
    })
    expect(result.success).toBe(false)
  })

  it('renders an accessible svg for valid data via its registered renderer', () => {
    const definition = panelTypeRegistry.resolve('chart')!
    const Renderer = definition.renderer
    const data = chartDataSchema.parse({
      chartKind: 'bar',
      series: [{ label: 'Exposure', values: [4, 6, 8] }],
    })
    const { getByRole } = render(<Renderer data={data} />)
    expect(getByRole('img')).toBeInTheDocument()
  })
})

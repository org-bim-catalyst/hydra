import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { ChartBlockRenderer } from './ChartBlock'

describe('ChartBlockRenderer', () => {
  it('renders a bar chart with an accessible label describing its shape', () => {
    render(
      <ChartBlockRenderer
        block={{
          kind: 'chart',
          chartKind: 'bar',
          labels: ['Jan', 'Feb'],
          series: [{ label: 'Sun hours', values: [4.2, 5.1] }],
        }}
      />,
    )
    expect(screen.getByRole('img', { name: /bar chart with 1 series across 2 categories/i })).toBeInTheDocument()
  })

  it('renders a line chart', () => {
    render(
      <ChartBlockRenderer
        block={{ kind: 'chart', chartKind: 'line', series: [{ label: 'Trend', values: [1, 2, 3] }] }}
      />,
    )
    expect(screen.getByRole('img', { name: /line chart/i })).toBeInTheDocument()
  })

  it('shows a legend when there is more than one series', () => {
    render(
      <ChartBlockRenderer
        block={{
          kind: 'chart',
          chartKind: 'bar',
          series: [
            { label: 'Series A', values: [1] },
            { label: 'Series B', values: [2] },
          ],
        }}
      />,
    )
    expect(screen.getByText('Series A')).toBeInTheDocument()
    expect(screen.getByText('Series B')).toBeInTheDocument()
  })
})

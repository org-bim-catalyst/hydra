import { render, screen } from '@testing-library/react'
import type { ReactNode } from 'react'
import { describe, expect, it } from 'vitest'
import { PanelDensityContext } from '../../chrome/density'
import { KeyValueBlockRenderer } from './KeyValueBlock'
import { MetricBlockRenderer } from './MetricBlock'

function renderCompact(ui: ReactNode) {
  return render(<PanelDensityContext.Provider value="compact">{ui}</PanelDensityContext.Provider>)
}

describe('content blocks in a compact panel', () => {
  it('renders a metric as one row — label and value with its unit side by side', () => {
    renderCompact(<MetricBlockRenderer block={{ kind: 'metric', label: 'Azimuth', value: 126.8, unit: '°' }} />)

    expect(screen.getByText('Azimuth').parentElement).toHaveTextContent('Azimuth126.8°')
  })

  it('renders each key/value item as its own row', () => {
    renderCompact(
      <KeyValueBlockRenderer
        block={{
          kind: 'keyValue',
          items: [
            { label: 'Sunrise', value: '06:04' },
            { label: 'Sunset', value: '18:24' },
          ],
        }}
      />,
    )

    expect(screen.getByText('Sunrise').parentElement).toHaveTextContent('Sunrise06:04')
    expect(screen.getByText('Sunset').parentElement).toHaveTextContent('Sunset18:24')
  })

  it('still marks a missing value as not available rather than leaving it blank', () => {
    renderCompact(<KeyValueBlockRenderer block={{ kind: 'keyValue', items: [{ label: 'Day length', value: null }] }} />)

    expect(screen.getByText('Not available')).toBeInTheDocument()
  })
})

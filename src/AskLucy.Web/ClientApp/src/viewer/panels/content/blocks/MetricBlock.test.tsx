import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { MetricBlockRenderer } from './MetricBlock'

describe('MetricBlockRenderer', () => {
  it('renders the label and value', () => {
    render(<MetricBlockRenderer block={{ kind: 'metric', label: 'Boundary confidence', value: 'High' }} />)
    expect(screen.getByText('Boundary confidence')).toBeInTheDocument()
    expect(screen.getByText('High')).toBeInTheDocument()
  })

  it('renders a numeric value with its unit', () => {
    render(<MetricBlockRenderer block={{ kind: 'metric', label: 'Day length', value: 11.4, unit: 'h' }} />)
    expect(screen.getByText('11.4')).toBeInTheDocument()
    expect(screen.getByText('h')).toBeInTheDocument()
  })

  it('omits the unit element when none is given', () => {
    const { container } = render(<MetricBlockRenderer block={{ kind: 'metric', label: 'Count', value: 3 }} />)
    expect(screen.getByText('3')).toBeInTheDocument()
    expect(container.textContent).toBe('Count3')
  })
})

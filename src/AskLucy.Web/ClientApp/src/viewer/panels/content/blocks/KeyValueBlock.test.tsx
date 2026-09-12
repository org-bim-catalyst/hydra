import { render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { KeyValueBlockRenderer } from './KeyValueBlock'

vi.mock('../../../engine/viewerEngineInstance', () => ({
  viewerEngine: { select: () => ({ ok: true }) },
}))

describe('KeyValueBlockRenderer', () => {
  it('renders each label and its value', () => {
    render(
      <KeyValueBlockRenderer
        block={{
          kind: 'keyValue',
          items: [
            { label: 'Address', value: 'Al Wasl Road, Dubai' },
            { label: 'Coordinates', value: '25.1412, 55.2210' },
          ],
        }}
      />,
    )
    expect(screen.getByText('Address')).toBeInTheDocument()
    expect(screen.getByText('Al Wasl Road, Dubai')).toBeInTheDocument()
    expect(screen.getByText('Coordinates')).toBeInTheDocument()
    expect(screen.getByText('25.1412, 55.2210')).toBeInTheDocument()
  })

  it('renders an explicit unavailable marker for a null value, not an empty cell', () => {
    render(
      <KeyValueBlockRenderer
        block={{ kind: 'keyValue', items: [{ label: 'Plot number', value: null }] }}
      />,
    )
    expect(screen.getByText('Plot number')).toBeInTheDocument()
    expect(screen.getByText('Not available')).toBeInTheDocument()
  })

  it('renders a numeric value', () => {
    render(<KeyValueBlockRenderer block={{ kind: 'keyValue', items: [{ label: 'Count', value: 42 }] }} />)
    expect(screen.getByText('42')).toBeInTheDocument()
  })

  it('renders an item carrying a valid action as activatable (specs/049 US2)', () => {
    render(
      <KeyValueBlockRenderer
        block={{
          kind: 'keyValue',
          items: [
            {
              label: 'Boundary layer',
              value: 'Visible',
              action: { command: 'setLayerVisibility', args: { layerId: 'boundary-1', visible: false } },
            },
          ],
        }}
      />,
    )
    expect(screen.getByRole('button')).toBeInTheDocument()
  })
})

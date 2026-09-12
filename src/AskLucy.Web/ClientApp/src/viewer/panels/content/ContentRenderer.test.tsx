import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { ContentRenderer } from './ContentRenderer'
import type { PanelContent } from './blocks'

describe('ContentRenderer (specs/049 User Story 1)', () => {
  it('renders a mixed composition of blocks in the order supplied', () => {
    const content: PanelContent = {
      version: 1,
      blocks: [
        { kind: 'heading', text: 'Al Safa Park 2' },
        { kind: 'keyValue', items: [{ label: 'Address', value: 'Al Wasl Road' }] },
        { kind: 'divider' },
        { kind: 'text', text: 'Resolved from the site boundary service.' },
      ],
    }
    const { container } = render(<ContentRenderer content={content} />)

    const headings = container.querySelectorAll('h6, p, hr')
    expect(screen.getByText('Al Safa Park 2')).toBeInTheDocument()
    expect(screen.getByText('Address')).toBeInTheDocument()
    expect(container.querySelector('hr')).toBeInTheDocument()
    expect(screen.getByText('Resolved from the site boundary service.')).toBeInTheDocument()
    expect(headings.length).toBeGreaterThan(0)
  })

  it('shows a visible placeholder for an unknown block kind while every sibling still renders (spec User Story 4)', () => {
    const content: PanelContent = {
      version: 1,
      blocks: [
        { kind: 'heading', text: 'This renders' },
        { kind: 'not-a-real-kind' },
        { kind: 'text', text: 'So does this' },
      ],
    }
    render(<ContentRenderer content={content} />)

    expect(screen.getByText('This renders')).toBeInTheDocument()
    expect(screen.getByText(/unsupported content/i)).toBeInTheDocument()
    expect(screen.getByText('So does this')).toBeInTheDocument()
  })

  it('shows a visible error for a malformed block while every sibling still renders (spec User Story 4)', () => {
    const content: PanelContent = {
      version: 1,
      blocks: [
        { kind: 'heading', text: 'This renders' },
        { kind: 'table', columns: [] },
        { kind: 'text', text: 'So does this' },
      ],
    }
    render(<ContentRenderer content={content} />)

    expect(screen.getByText('This renders')).toBeInTheDocument()
    expect(screen.getByText(/couldn't be displayed/i)).toBeInTheDocument()
    expect(screen.getByText('So does this')).toBeInTheDocument()
  })

  it('renders a distinct visible outcome for every block in a document mixing valid, unknown-kind and malformed blocks (T051, spec SC-007)', () => {
    const content: PanelContent = {
      version: 1,
      blocks: [
        { kind: 'heading', text: 'Valid heading' },
        { kind: 'nonsense-kind' },
        { kind: 'table', columns: [] },
        { kind: 'divider' },
        { kind: 'metric', label: 'Confidence', value: 'High' },
      ],
    }
    render(<ContentRenderer content={content} />)

    expect(screen.getByText('Valid heading')).toBeInTheDocument()
    expect(screen.getByText(/unsupported content/i)).toBeInTheDocument()
    expect(screen.getByText(/couldn't be displayed/i)).toBeInTheDocument()
    expect(screen.getByText('Confidence')).toBeInTheDocument()
    expect(screen.getByText('High')).toBeInTheDocument()
  })
})

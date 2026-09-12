import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { blockSchema } from '../blocks'
import { TableBlockRenderer } from './TableBlock'

describe('TableBlockRenderer', () => {
  it('renders columns and row cells', () => {
    render(
      <TableBlockRenderer
        block={{
          kind: 'table',
          columns: ['Element', 'Id'],
          rows: [{ cells: ['Wall', 'wall-42'] }],
        }}
      />,
    )
    expect(screen.getByText('Element')).toBeInTheDocument()
    expect(screen.getByText('Wall')).toBeInTheDocument()
    expect(screen.getByText('wall-42')).toBeInTheDocument()
  })

  it('shows a placeholder when there are no rows', () => {
    render(<TableBlockRenderer block={{ kind: 'table', columns: ['A'], rows: [] }} />)
    expect(screen.getByText(/no data to display/i)).toBeInTheDocument()
  })

  it('renders a row with fewer cells than columns, padded and marked malformed, without failing the block', () => {
    render(
      <TableBlockRenderer
        block={{
          kind: 'table',
          columns: ['A', 'B', 'C'],
          rows: [{ cells: ['only-one'] }],
        }}
      />,
    )
    expect(screen.getByText('only-one')).toBeInTheDocument()
    expect(screen.getByRole('img', { name: /row is malformed/i })).toBeInTheDocument()
    // The missing cells render as an explicit placeholder rather than being silently dropped.
    expect(screen.getAllByText('—')).toHaveLength(2)
  })

  it('renders a well-formed row alongside a malformed one — the malformed row does not fail the block', () => {
    render(
      <TableBlockRenderer
        block={{
          kind: 'table',
          columns: ['A', 'B'],
          rows: [{ cells: ['ok1', 'ok2'] }, { cells: ['too', 'many', 'cells'] }],
        }}
      />,
    )
    expect(screen.getByText('ok1')).toBeInTheDocument()
    expect(screen.getByText('too')).toBeInTheDocument()
    expect(screen.getByRole('img', { name: /row is malformed/i })).toBeInTheDocument()
  })
})

describe('table block schema — row cap (constitution §7)', () => {
  it('rejects a table exceeding the 200-row cap at the schema, rather than leaving it to render', () => {
    const rows = Array.from({ length: 201 }, () => ({ cells: ['x'] }))
    const parsed = blockSchema.safeParse({ kind: 'table', columns: ['A'], rows })
    expect(parsed.success).toBe(false)
  })

  it('accepts a table at exactly the 200-row cap', () => {
    const rows = Array.from({ length: 200 }, () => ({ cells: ['x'] }))
    const parsed = blockSchema.safeParse({ kind: 'table', columns: ['A'], rows })
    expect(parsed.success).toBe(true)
  })
})

import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { panelTypeRegistry } from '../../registry'
import { tableDataSchema } from './TablePanel'
import './TablePanel'

describe('table panel type', () => {
  it('registers itself under the "table" type key with a resizable default size', () => {
    const definition = panelTypeRegistry.resolve('table')
    expect(definition).toBeDefined()
    expect(definition?.resizable).toBe(true)
  })

  it('accepts valid tabular data', () => {
    const result = tableDataSchema.safeParse({
      columns: ['Name', 'Value'],
      rows: [
        ['A', 1],
        ['B', null],
      ],
    })
    expect(result.success).toBe(true)
  })

  it('rejects data with no columns', () => {
    const result = tableDataSchema.safeParse({ columns: [], rows: [] })
    expect(result.success).toBe(false)
  })

  it('rejects a row cell of an unsupported type', () => {
    const result = tableDataSchema.safeParse({ columns: ['A'], rows: [[{ nested: true }]] })
    expect(result.success).toBe(false)
  })

  it('renders column headers and row values via its registered renderer', () => {
    const definition = panelTypeRegistry.resolve('table')!
    const Renderer = definition.renderer
    const data = tableDataSchema.parse({ columns: ['Name', 'Value'], rows: [['Widget', 42]] })
    render(<Renderer data={data} />)
    expect(screen.getByText('Name')).toBeInTheDocument()
    expect(screen.getByText('Widget')).toBeInTheDocument()
    expect(screen.getByText('42')).toBeInTheDocument()
  })

  it('renders an empty-state message when there are no rows', () => {
    const definition = panelTypeRegistry.resolve('table')!
    const Renderer = definition.renderer
    const data = tableDataSchema.parse({ columns: ['Name'], rows: [] })
    render(<Renderer data={data} />)
    expect(screen.getByText(/no data to display/i)).toBeInTheDocument()
  })
})

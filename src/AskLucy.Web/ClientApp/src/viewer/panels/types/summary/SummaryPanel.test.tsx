import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { panelTypeRegistry } from '../../registry'
import { summaryDataSchema } from './SummaryPanel'
import './SummaryPanel'

describe('summary panel type', () => {
  it('registers itself under the "summary" type key', () => {
    const definition = panelTypeRegistry.resolve('summary')
    expect(definition).toBeDefined()
    expect(definition?.resizable).toBe(true)
  })

  it('accepts valid heading/body data', () => {
    const result = summaryDataSchema.safeParse({ heading: 'Site Notes', body: 'Some analysis text.' })
    expect(result.success).toBe(true)
  })

  it('rejects data missing the required fields', () => {
    const result = summaryDataSchema.safeParse({ heading: 'Only heading' })
    expect(result.success).toBe(false)
  })

  it('renders the heading and body via its registered renderer', () => {
    const definition = panelTypeRegistry.resolve('summary')!
    const Renderer = definition.renderer
    const data = summaryDataSchema.parse({ heading: 'Site Notes', body: 'Some analysis text.' })
    render(<Renderer data={data} />)
    expect(screen.getByText('Site Notes')).toBeInTheDocument()
    expect(screen.getByText('Some analysis text.')).toBeInTheDocument()
  })
})

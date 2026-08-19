import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { panelTypeRegistry } from '../../registry'
import { parametersDataSchema } from './ParametersPanel'
import './ParametersPanel'

describe('parameters panel type', () => {
  it('registers itself under the "parameters" type key as fixed-size (not resizable)', () => {
    const definition = panelTypeRegistry.resolve('parameters')
    expect(definition).toBeDefined()
    expect(definition?.resizable).toBe(false)
  })

  it('accepts valid field data', () => {
    const result = parametersDataSchema.safeParse({
      fields: [{ key: 'height', label: 'Height (m)', type: 'number', value: 12 }],
    })
    expect(result.success).toBe(true)
  })

  it('rejects data with no fields', () => {
    const result = parametersDataSchema.safeParse({ fields: [] })
    expect(result.success).toBe(false)
  })

  it('rejects a field with an unsupported type', () => {
    const result = parametersDataSchema.safeParse({
      fields: [{ key: 'x', label: 'X', type: 'slider', value: 1 }],
    })
    expect(result.success).toBe(false)
  })

  it('renders a labeled input per field via its registered renderer', () => {
    const definition = panelTypeRegistry.resolve('parameters')!
    const Renderer = definition.renderer
    const data = parametersDataSchema.parse({
      fields: [{ key: 'height', label: 'Height (m)', type: 'number', value: 12 }],
    })
    render(<Renderer data={data} />)
    expect(screen.getByLabelText('Height (m)')).toBeInTheDocument()
  })
})

import { render } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { useActiveLocationStore } from '../../../store/activeLocationStore'
import { useViewerEngineStore } from '../../../viewer/store/viewerEngineStore'
import { ViewerSurface } from './ViewerSurface'

expect.extend(toHaveNoViolations)

const { useWebGLSupportMock } = vi.hoisted(() => ({ useWebGLSupportMock: vi.fn() }))
vi.mock('../../../hooks/useWebGLSupport', () => ({ useWebGLSupport: useWebGLSupportMock }))

const initialState = useViewerEngineStore.getState()

describe('ViewerSurface accessibility (FR-001/FR-004)', () => {
  beforeEach(() => {
    useViewerEngineStore.setState(initialState, true)
    useActiveLocationStore.getState().clear()
    useWebGLSupportMock.mockReturnValue(true)
  })

  afterEach(() => {
    useActiveLocationStore.getState().clear()
  })

  it('has no automatically detectable a11y violations (neutral / no location state)', async () => {
    // Store is empty — source === null; renders placeholder (FR-004).
    const { container } = render(<ViewerSurface />)
    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })

  it('the placeholder is aria-hidden and never traps keyboard focus', () => {
    const { getByTestId, container } = render(<ViewerSurface />)
    expect(getByTestId('viewer-placeholder')).toHaveAttribute('aria-hidden', 'true')
    expect(container.querySelectorAll('button, a, input, [tabindex]')).toHaveLength(0)
  })
})

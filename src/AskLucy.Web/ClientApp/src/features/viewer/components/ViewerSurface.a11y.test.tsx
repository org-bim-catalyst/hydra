import { render } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useViewerEngineStore } from '../../../viewer/store/viewerEngineStore'
import { ViewerSurface } from './ViewerSurface'

expect.extend(toHaveNoViolations)

const { useWebGLSupportMock } = vi.hoisted(() => ({ useWebGLSupportMock: vi.fn() }))
vi.mock('../../../hooks/useWebGLSupport', () => ({ useWebGLSupport: useWebGLSupportMock }))

const initialState = useViewerEngineStore.getState()

describe('ViewerSurface accessibility (FR-001/FR-004)', () => {
  beforeEach(() => {
    useViewerEngineStore.setState(initialState, true)
    useWebGLSupportMock.mockReturnValue(true)
  })

  it('has no automatically detectable a11y violations', async () => {
    const { container } = render(<ViewerSurface geolocation={{ status: 'resolving', latitude: null, longitude: null }} />)
    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })

  it('the placeholder is aria-hidden and never traps keyboard focus', () => {
    const { getByTestId, container } = render(<ViewerSurface geolocation={{ status: 'resolving', latitude: null, longitude: null }} />)
    expect(getByTestId('viewer-placeholder')).toHaveAttribute('aria-hidden', 'true')
    expect(container.querySelectorAll('button, a, input, [tabindex]')).toHaveLength(0)
  })
})

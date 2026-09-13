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
    // specs/052-solar-analysis: the viewer's own extension toolbar (`ExtensionToolbar`, a sibling
    // of the placeholder, not part of it) now legitimately contains a real control regardless of
    // whether a site is shown (FR-029) — the first built-in extension to contribute one. This
    // assertion's actual intent, unchanged, is that the inert PLACEHOLDER GRAPHIC ITSELF never
    // becomes keyboard-reachable — scoped to that element rather than the whole surface.
    const { getByTestId } = render(<ViewerSurface />)
    const placeholder = getByTestId('viewer-placeholder')
    expect(placeholder).toHaveAttribute('aria-hidden', 'true')
    expect(placeholder.querySelectorAll('button, a, input, [tabindex]')).toHaveLength(0)
  })
})

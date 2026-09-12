import { act, render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it } from 'vitest'
import { viewerEngine } from '../../engine/viewerEngineInstance'
import { useContentStore } from '../../content/contentStore'
import type { ViewerContent } from '../../content/ViewerContent'
import { viewerExtensionRegistry } from '../registry'
import { useViewerExtensionStore } from '../store/viewerExtensionStore'
import type { ViewerExtension } from '../ViewerExtension'
import { ExtensionFailureNotice } from './ExtensionFailureNotice'

const initialState = useViewerExtensionStore.getState()
const initialContentState = useContentStore.getState()

function makeContent(overrides: Partial<ViewerContent> = {}): ViewerContent {
  return {
    id: 'c1',
    layerId: 'l1',
    source: { kind: 'gis', provider: 'google-maps', center: { latitude: 0, longitude: 0 } },
    placement: null,
    loadState: 'failed',
    failureReason: 'unreachable-or-corrupt',
    ...overrides,
  }
}

function uniqueId(prefix: string): string {
  return `${prefix}-${Math.random().toString(36).slice(2)}`
}

function registerNamed(id: string, displayName: string): ViewerExtension {
  const extension: ViewerExtension = {
    id,
    manifest: { displayName, description: 'test' },
    start: () => {},
    stop: () => {},
  }
  viewerExtensionRegistry.register(extension)
  return extension
}

describe('ExtensionFailureNotice (research D5, FR-029, FR-031)', () => {
  beforeEach(() => {
    useViewerExtensionStore.setState(initialState, true)
    useContentStore.setState(initialContentState, true)
  })

  it('renders nothing when nothing has failed', () => {
    render(<ExtensionFailureNotice />)
    expect(screen.queryByTestId('extension-failure-notice')).not.toBeInTheDocument()
  })

  it('renders nothing for an extension that is merely starting or started', () => {
    const id = uniqueId('ext')
    registerNamed(id, 'Fine')
    useViewerExtensionStore.getState().setLifecycle(id, 'started')

    render(<ExtensionFailureNotice />)
    expect(screen.queryByTestId('extension-failure-notice')).not.toBeInTheDocument()
  })

  it('names the failed capability by its manifest displayName', () => {
    const id = uniqueId('ext')
    registerNamed(id, 'Solar Analysis')
    useViewerExtensionStore.getState().setLifecycle(id, 'failed', 'start blew up')

    render(<ExtensionFailureNotice />)
    expect(screen.getByTestId('extension-failure-notice')).toHaveTextContent('Solar Analysis unavailable')
  })

  it('names a still-running capability whose event handler threw, worded as an error rather than unavailable (FR-017)', () => {
    const id = uniqueId('ext')
    registerNamed(id, 'Solar Analysis')
    useViewerExtensionStore.getState().setLifecycle(id, 'started')
    useViewerExtensionStore.getState().recordEventFailure(id, 'handler blew up')

    render(<ExtensionFailureNotice />)
    expect(screen.getByTestId('extension-failure-notice')).toHaveTextContent('Solar Analysis reported an error')
  })

  it('summarizes when more than one capability has failed', () => {
    const id1 = uniqueId('ext')
    const id2 = uniqueId('ext')
    registerNamed(id1, 'A')
    registerNamed(id2, 'B')
    useViewerExtensionStore.getState().setLifecycle(id1, 'failed', 'boom a')
    useViewerExtensionStore.getState().setLifecycle(id2, 'failed', 'boom b')

    render(<ExtensionFailureNotice />)
    expect(screen.getByTestId('extension-failure-notice')).toHaveTextContent('2 capabilities affected')
  })

  it('T054 (US5): each ContentFailureReason produces a distinct message', () => {
    useContentStore.getState().upsert(makeContent({ failureReason: 'unsupported-format' }))
    render(<ExtensionFailureNotice />)
    expect(screen.getByTestId('extension-failure-notice')).toHaveTextContent('format not supported')
  })

  it('T054 (US5): a distinct message for unreachable-or-corrupt content', () => {
    useContentStore.getState().upsert(makeContent({ failureReason: 'unreachable-or-corrupt' }))
    render(<ExtensionFailureNotice />)
    expect(screen.getByTestId('extension-failure-notice')).toHaveTextContent('could not be loaded')
  })

  it('T054 (US5): a distinct message for unplaceable content', () => {
    useContentStore.getState().upsert(makeContent({ failureReason: 'unplaceable' }))
    render(<ExtensionFailureNotice />)
    expect(screen.getByTestId('extension-failure-notice')).toHaveTextContent('has no position and cannot be shown')
  })

  it('T054 (US5): a drawingRequirementConflict event produces its own distinct message', async () => {
    render(<ExtensionFailureNotice />)
    act(() => {
      viewerEngine.notifyDrawingRequirementConflict('shadows', ['ext-a', 'ext-b'])
    })
    expect(await screen.findByTestId('extension-failure-notice')).toHaveTextContent('shadows requirement conflict')
  })

  it('T054 (US5): a drawingCallbackFailed event produces its own distinct message', async () => {
    render(<ExtensionFailureNotice />)
    act(() => {
      viewerEngine.notifyDrawingCallbackFailed('ext-drawing', 'boom')
    })
    expect(await screen.findByTestId('extension-failure-notice')).toHaveTextContent('reported a drawing error')
  })
})

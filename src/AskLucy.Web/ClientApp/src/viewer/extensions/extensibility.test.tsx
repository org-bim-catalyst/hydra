import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, render, screen } from '@testing-library/react'
import * as THREE from 'three'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { z } from 'zod'
import { worldToLocal } from '../api/coordinateFrame'
import { sceneAnchor } from '../scene/SceneAnchor'
import { ExtensionHudItemHost } from './components/ExtensionHudItemHost'
import { ExtensionOverlayHost } from './components/ExtensionOverlayHost'
import { ExtensionToolbar } from './components/ExtensionToolbar'
import type { ExtensionContext } from './context'
import { viewerExtensionLoader } from './loader'
import { panelTypeRegistry } from '../panels/registry'
import { useFloatingPanelStore } from '../panels/store/floatingPanelStore'
import { viewerExtensionRegistry } from './registry'
import { useViewerExtensionStore } from './store/viewerExtensionStore'
import type { ViewerExtension } from './ViewerExtension'

const initialExtensionState = useViewerExtensionStore.getState()
const initialPanelState = useFloatingPanelStore.getState()

function uniqueId(prefix: string): string {
  return `${prefix}-${Math.random().toString(36).slice(2)}`
}

// specs/057-site-analysis-agent — viewer.panels' overlay now also mounts useSiteAnalysisHub,
// which calls useQueryClient() unconditionally on every render.
function renderExtensionHosts() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={queryClient}>
      <ExtensionOverlayHost />
      <ExtensionToolbar />
      <ExtensionHudItemHost />
    </QueryClientProvider>,
  )
}

/** quickstart Scenario 3 (SC-003) — a throwaway extension contributing all three kinds this
 * feature supports. Built fresh per test (unique ids) so tests don't interfere with each other
 * via the module-level registry singleton. */
function makeScratchExtension(id: string, typeKey: string): ViewerExtension {
  return {
    id,
    manifest: { displayName: 'Scratch', description: 'Throwaway.' },
    start(context: ExtensionContext) {
      context.contributeOverlay(() => <div data-testid="scratch-overlay">hello</div>)
      context.contributeToolbarEntry({ id: `${id}-entry`, label: 'Scratch', icon: () => null, onClick: () => {} })
      context.registerLivePanelKind({
        typeKey,
        renderer: () => <div data-testid="scratch-panel-render">rendered</div>,
        schema: z.object({}),
        chrome: { titleBar: true, resizable: true, defaultSize: { width: 320, height: 240 } },
      })
    },
    stop() {},
  }
}

describe('extensibility (quickstart Scenario 3, SC-003)', () => {
  beforeEach(() => {
    useViewerExtensionStore.setState(initialExtensionState, true)
    useFloatingPanelStore.setState(initialPanelState, true)
  })

  it('a scratch extension contributing an overlay, a toolbar entry and a live panel kind appears in full, and leaves no trace when removed', async () => {
    const id = uniqueId('viewer.scratch')
    const typeKey = uniqueId('scratch-panel')
    const scratch = makeScratchExtension(id, typeKey)
    viewerExtensionRegistry.register(scratch)

    await act(() => viewerExtensionLoader.start(id))

    renderExtensionHosts()

    expect(screen.getByTestId('scratch-overlay')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Scratch' })).toBeInTheDocument()
    expect(panelTypeRegistry.resolve(typeKey)).toBeDefined()

    // "removed from the declared set" — stopping is the framework's equivalent, since nothing
    // outside the declared list changed to make it start in the first place (SC-003).
    await act(() => viewerExtensionLoader.stop(id))

    expect(panelTypeRegistry.resolve(typeKey)).toBeUndefined()
    expect(useViewerExtensionStore.getState().contributions).toHaveLength(0)
  })

  it('FR-005: a new contribution kind can be added to the store without any existing consumer breaking', async () => {
    // A throwaway kind the context/store don't formally declare a `contribute` helper for —
    // standing in for a future kind specs/051 introduces (scene access, frame callbacks). Proves
    // the store's contribution list is open to a new tagged variant: existing hosts, which filter
    // by `kind`, simply don't recognize it rather than crash on it.
    useViewerExtensionStore.getState().addContribution({
      // @ts-expect-error — deliberately not a currently-declared Contribution kind (FR-005 proof).
      kind: 'futureKind',
      extensionId: 'ext-future',
    })
    expect(useViewerExtensionStore.getState().contributions).toHaveLength(1)

    renderExtensionHosts()
    // Neither host crashes or renders anything for the kind it doesn't recognize.
    expect(document.body.textContent).toBe('')

    // The migrated built-in extensions register and start unchanged — the new kind above
    // required no edit to any of them.
    const { DECLARED_EXTENSIONS } = await import('./declared')
    expect(DECLARED_EXTENSIONS).toEqual([
      'viewer.panels',
      'viewer.poi-marker',
      'viewer.boundary-confidence',
      'viewer.site-boundary',
      'viewer.solar-analysis',
    ])
    for (const id of DECLARED_EXTENSIONS) {
      await act(() => viewerExtensionLoader.start(id))
      expect(useViewerExtensionStore.getState().extensions[id]?.lifecycle).toBe('started')
    }
  })

  // specs/073 contract X4 — each host renders only its own kind, so a HUD item never also appears
  // as a viewer overlay, and vice versa.
  it("hosts ignore a kind they don't recognise: hudItem and overlay each render in exactly one host", () => {
    useViewerExtensionStore.getState().addContribution({
      kind: 'hudItem',
      extensionId: 'ext-hud',
      component: () => <span data-testid="hud-item">hud</span>,
    })
    useViewerExtensionStore.getState().addContribution({
      kind: 'overlay',
      extensionId: 'ext-overlay',
      component: () => <span data-testid="overlay-item">overlay</span>,
    })

    renderExtensionHosts()

    expect(screen.getAllByTestId('hud-item')).toHaveLength(1)
    expect(screen.getAllByTestId('overlay-item')).toHaveLength(1)
  })

  // specs/073 contract X7 — neither host had an error boundary before, so one throwing overlay
  // took down the whole viewer (and, now that HUD items live in WorkspaceOverlay, one throwing
  // HUD item would take down chat and every control).
  it('a throwing overlay marks its extension failed without unmounting the viewer', async () => {
    vi.spyOn(console, 'error').mockImplementation(() => {})
    const id = uniqueId('viewer.scratch-throwing')
    viewerExtensionRegistry.register({
      id,
      manifest: { displayName: 'Throwing', description: 'Throwaway.' },
      start(context: ExtensionContext) {
        context.contributeOverlay(() => {
          throw new Error('overlay broke')
        })
        context.contributeToolbarEntry({ id: `${id}-entry`, label: 'Still here', icon: () => null, onClick: () => {} })
      },
      stop() {},
    })
    await act(() => viewerExtensionLoader.start(id))

    renderExtensionHosts()

    expect(screen.getByRole('button', { name: 'Still here' })).toBeInTheDocument()
    expect(useViewerExtensionStore.getState().extensions[id]).toMatchObject({
      lifecycle: 'failed',
      failureReason: 'Render failed: overlay broke',
    })
    vi.restoreAllMocks()
  })

  it('T035 (US2): a scratch extension draws through its own Drawing Space, positioned via worldToLocal, at the expected local coordinates', async () => {
    sceneAnchor.set({ latitude: 25.2048, longitude: 55.2708 })
    const point = { latitude: 25.21, longitude: 55.28 }
    const expected = worldToLocal(point, 5)

    const id = uniqueId('viewer.scratch-drawing')
    let mesh: THREE.Object3D | null = null
    viewerExtensionRegistry.register({
      id,
      manifest: { displayName: 'Scratch Drawing', description: 'Throwaway.' },
      start(context: ExtensionContext) {
        const handle = context.acquireDrawingSpace()
        mesh = new THREE.Object3D()
        const local = worldToLocal(point, 5)
        mesh.position.set(local.x, local.y, local.z)
        handle.group.add(mesh)
      },
      stop() {},
    })

    await act(() => viewerExtensionLoader.start(id))

    expect(mesh).not.toBeNull()
    expect(mesh!.position.x).toBeCloseTo(expected.x)
    expect(mesh!.position.y).toBeCloseTo(expected.y)
    expect(mesh!.position.z).toBe(expected.z)
  })
})

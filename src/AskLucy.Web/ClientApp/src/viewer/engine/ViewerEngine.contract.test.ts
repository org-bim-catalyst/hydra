import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useViewerEngineStore } from '../store/viewerEngineStore'
import { ViewerEngine } from './ViewerEngine'

const initialState = useViewerEngineStore.getState()

/**
 * contracts/viewer-engine-api.md, end to end (User Story 6, SC-006): every documented command
 * succeeds/fails as specified and fires its corresponding event — proving the full command/event
 * contract works with zero AI-agent code involved, matching quickstart.md Scenario 5's own
 * devtools-console verification steps.
 */
describe('ViewerEngine — full contract (US6, SC-006)', () => {
  let engine: ViewerEngine

  beforeEach(() => {
    useViewerEngineStore.setState(initialState, true)
    engine = new ViewerEngine()
  })

  it('every command resolves to a ViewerCommandResult and never throws, for both success and documented failure inputs', () => {
    // addLayer
    expect(engine.addLayer({ id: 'gis-1', kind: 'gis' })).toEqual({ ok: true, data: { layerId: 'gis-1' } })
    expect(engine.addLayer({ id: 'gis-1', kind: 'gis' }).ok).toBe(false) // duplicate id

    // setLayerVisibility
    expect(engine.setLayerVisibility('gis-1', false)).toEqual({ ok: true, data: undefined })
    expect(engine.setLayerVisibility('unknown', false).ok).toBe(false)

    // zoomToLocation
    expect(engine.zoomToLocation(51.5074, -0.1278, 12)).toEqual({ ok: true, data: undefined })
    expect(engine.zoomToLocation(999, 0).ok).toBe(false) // out-of-range latitude

    // setViewMode / setRotationEnabled
    expect(engine.setViewMode('plan')).toEqual({ ok: true, data: undefined })
    expect(engine.setRotationEnabled(false)).toEqual({ ok: true, data: undefined })

    // select / clearSelection
    engine.registerSelectableElement('gis-1', 'marker')
    expect(engine.select('gis-1', 'marker')).toEqual({ ok: true, data: undefined })
    expect(engine.select('gis-1', 'unknown-element').ok).toBe(false)
    expect(engine.clearSelection()).toEqual({ ok: true, data: undefined })

    // displayContent
    expect(engine.displayContent('gis-1', { foo: 'bar' })).toEqual({ ok: true, data: undefined })
    expect(engine.displayContent('unknown-layer', {}).ok).toBe(false)

    // createOverlay
    expect(engine.createOverlay({ id: 'overlay-1' })).toEqual({ ok: true, data: { overlayId: 'overlay-1' } })
    expect(engine.createOverlay({ id: 'overlay-1' }).ok).toBe(false) // duplicate id

    // removeLayer
    expect(engine.removeLayer('overlay-1')).toEqual({ ok: true, data: undefined })
    expect(engine.removeLayer('overlay-1').ok).toBe(false) // already removed
  })

  it('every documented event fires exactly once per corresponding successful command', () => {
    const events: string[] = []
    for (const type of [
      'layerAdded',
      'layerRemoved',
      'contentLoaded',
      'selectionChanged',
      'viewModeChanged',
      'rotationChanged',
    ] as const) {
      engine.on(type, () => events.push(type))
    }

    engine.addLayer({ id: 'gis-1', kind: 'gis' }) // layerAdded
    engine.displayContent('gis-1', { foo: 'bar' }) // contentLoaded
    engine.registerSelectableElement('gis-1', 'marker')
    engine.select('gis-1', 'marker') // selectionChanged
    engine.clearSelection() // selectionChanged
    engine.setViewMode('plan') // viewModeChanged
    engine.setRotationEnabled(false) // rotationChanged
    engine.removeLayer('gis-1') // layerRemoved

    expect(events).toEqual([
      'layerAdded',
      'contentLoaded',
      'selectionChanged',
      'selectionChanged',
      'viewModeChanged',
      'rotationChanged',
      'layerRemoved',
    ])
  })

  it('a subscriber can observe every command outcome via on()/off() without polling (FR-023)', () => {
    const handler = vi.fn()
    const unsubscribe = engine.on('viewModeChanged', handler)

    engine.setViewMode('plan')
    expect(handler).toHaveBeenCalledTimes(1)

    unsubscribe()
    engine.setViewMode('isometric')
    expect(handler).toHaveBeenCalledTimes(1) // no further calls after unsubscribing
  })
})

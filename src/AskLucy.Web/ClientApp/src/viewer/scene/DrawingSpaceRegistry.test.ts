import * as THREE from 'three'
import { describe, expect, it, vi } from 'vitest'
import { createExtensionContext } from '../extensions/context'
import { useViewerExtensionStore } from '../extensions/store/viewerExtensionStore'
import { DrawingSpaceRegistry } from './DrawingSpaceRegistry'

describe('DrawingSpaceRegistry (FR-014, FR-015, FR-019, FR-033, research D3, D3a)', () => {
  it('two acquired handles have distinct, non-overlapping groups', () => {
    const scene = new THREE.Scene()
    const registry = new DrawingSpaceRegistry(scene)

    const a = registry.acquire('ext-a')
    const b = registry.acquire('ext-b')

    expect(a.group).not.toBe(b.group)
    expect(scene.children).toContain(a.group)
    expect(scene.children).toContain(b.group)
  })

  it('release() removes the group from the scene and clears its onFrame subscriptions', () => {
    const scene = new THREE.Scene()
    const registry = new DrawingSpaceRegistry(scene)
    const handle = registry.acquire('ext-a')
    const callback = vi.fn()
    handle.onFrame(callback)

    registry.release('ext-a')

    expect(scene.children).not.toContain(handle.group)
    registry.invokeFrameCallbacks(0.016)
    expect(callback).not.toHaveBeenCalled()
  })

  it('acquire() called twice for the same extension id returns the same handle (idempotent)', () => {
    const scene = new THREE.Scene()
    const registry = new DrawingSpaceRegistry(scene)

    const first = registry.acquire('ext-a')
    const second = registry.acquire('ext-a')

    expect(first).toBe(second)
    expect(scene.children).toHaveLength(1)
  })

  it('release() disposes every descendant geometry/material', () => {
    const scene = new THREE.Scene()
    const registry = new DrawingSpaceRegistry(scene)
    const handle = registry.acquire('ext-a')

    const geometry = new THREE.BoxGeometry()
    const material = new THREE.MeshBasicMaterial()
    const disposeGeometry = vi.spyOn(geometry, 'dispose')
    const disposeMaterial = vi.spyOn(material, 'dispose')
    handle.group.add(new THREE.Mesh(geometry, material))

    registry.release('ext-a')

    expect(disposeGeometry).toHaveBeenCalled()
    expect(disposeMaterial).toHaveBeenCalled()
  })

  it('T010a: one drawing space\'s throwing onFrame callback does not prevent another\'s from running, and the failure is surfaced', () => {
    const scene = new THREE.Scene()
    const onFailure = vi.fn()
    const registry = new DrawingSpaceRegistry(scene, onFailure)

    const thrower = registry.acquire('ext-thrower')
    const listener = registry.acquire('ext-listener')
    let listenerRan = false
    thrower.onFrame(() => {
      throw new Error('boom')
    })
    listener.onFrame(() => {
      listenerRan = true
    })

    registry.invokeFrameCallbacks(0.016)

    expect(listenerRan).toBe(true)
    expect(onFailure).toHaveBeenCalledWith('ext-thrower', 'boom')
  })

  it('bind() parents every already-acquired group into the scene, preserving acquisition order', () => {
    const registry = new DrawingSpaceRegistry()
    const a = registry.acquire('ext-a')
    const b = registry.acquire('ext-b')

    const scene = new THREE.Scene()
    registry.bind(scene)

    expect(scene.children).toEqual([a.group, b.group])
  })

  it('T010b: groups are children of the scene in acquisition order, stable across an unrelated release/re-acquire cycle', () => {
    const scene = new THREE.Scene()
    const registry = new DrawingSpaceRegistry(scene)

    const a = registry.acquire('ext-a')
    const b = registry.acquire('ext-b')
    const c = registry.acquire('ext-c')

    expect(scene.children).toEqual([a.group, b.group, c.group])

    // An unrelated extension (b) releasing and re-acquiring must not reorder a/c relative to
    // each other, and b's new group goes to the end (new acquisition), not back to its old slot.
    registry.release('ext-b')
    const newB = registry.acquire('ext-b')

    expect(scene.children).toEqual([a.group, c.group, newB.group])
  })

  it('T036 (US2, SC-003): deliberately attempts, via every means ExtensionContext/DrawingSpaceHandle expose, to reach or mutate another extension\'s content — zero successful attempts', () => {
    const initialState = useViewerExtensionStore.getState()
    useViewerExtensionStore.setState(initialState, true)

    const contextA = createExtensionContext('ext-a-attempt')
    const contextB = createExtensionContext('ext-b-attempt')
    const handleA = contextA.acquireDrawingSpace()
    const handleB = contextB.acquireDrawingSpace()
    const bMesh = new THREE.Object3D()
    handleB.group.add(bMesh)

    // Attempt 1: ExtensionContext exposes no way to name another extension or fetch its handle —
    // acquireDrawingSpace() takes no id argument at all, so there is no "acquire someone else's
    // space" call to even attempt.
    expect(contextA.acquireDrawingSpace().group).not.toBe(handleB.group)

    // Attempt 2: DrawingSpaceHandle exposes only `group`/`invalidate`/`onFrame`/
    // `declareDrawingRequirement` — none of which take another handle, group, or extension id as
    // an argument, so there is no method call through which A could reach B's group.
    const handleAKeys = Object.keys(handleA)
    expect(handleAKeys).toEqual(['group', 'invalidate', 'onFrame', 'declareDrawingRequirement'])

    // Attempt 3: A's own group has no reference to B's group or to the shared scene/camera/
    // renderer — walking A's group subtree never encounters B's mesh.
    let foundBMesh = false
    handleA.group.traverse((child) => {
      if (child === bMesh) foundBMesh = true
    })
    expect(foundBMesh).toBe(false)

    // Attempt 4: removing A's own group cannot remove B's — THREE.Object3D.remove() only
    // detaches an object that is actually a child of the group it's called on.
    handleA.group.remove(bMesh)
    expect(bMesh.parent).not.toBeNull()

    useViewerExtensionStore.setState(initialState, true)
  })

  it('T049 (US4, FR-023/FR-024): onFrame fires once per actual invokeFrameCallbacks call, never on its own, and ends automatically when the extension stops', () => {
    const scene = new THREE.Scene()
    const registry = new DrawingSpaceRegistry(scene)
    const handle = registry.acquire('ext-a')
    let fireCount = 0
    handle.onFrame(() => {
      fireCount += 1
    })

    // No implicit firing — only invokeFrameCallbacks (called once per actual draw) triggers it.
    expect(fireCount).toBe(0)

    registry.invokeFrameCallbacks(0.016)
    registry.invokeFrameCallbacks(0.016)
    expect(fireCount).toBe(2)

    // Stopping (release) ends the subscription — no further calls, even if invoked again.
    registry.release('ext-a')
    registry.invokeFrameCallbacks(0.016)
    expect(fireCount).toBe(2)
  })

  it('T050 (US4, SC-005): fifty acquire/declare/subscribe/release cycles accumulate nothing', async () => {
    const { rendererState } = await import('./rendererState')
    const fakeRenderer = { shadowMap: { enabled: false } } as unknown as import('three').WebGLRenderer
    rendererState.bind(fakeRenderer, () => {})

    const scene = new THREE.Scene()
    const registry = new DrawingSpaceRegistry(scene)
    const baselineChildCount = scene.children.length

    for (let cycle = 0; cycle < 50; cycle++) {
      const handle = registry.acquire('cycling-ext')
      const geometry = new THREE.BoxGeometry()
      const material = new THREE.MeshBasicMaterial()
      handle.group.add(new THREE.Mesh(geometry, material))
      handle.declareDrawingRequirement('shadows')
      handle.onFrame(() => {})
      expect(fakeRenderer.shadowMap.enabled).toBe(true)

      registry.release('cycling-ext')
      expect(fakeRenderer.shadowMap.enabled).toBe(false)
    }

    expect(scene.children.length).toBe(baselineChildCount)
  })
})

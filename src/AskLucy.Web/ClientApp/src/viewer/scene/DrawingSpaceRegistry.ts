import * as THREE from 'three'
import { redrawScheduler } from './RedrawScheduler'
import { rendererState, type DrawingRequirement } from './rendererState'

type FrameCallback = (deltaSeconds: number) => void
type FailureReporter = (extensionId: string, message: string) => void

/** contracts/coordinate-frame-and-drawing-space.md — what a capability draws through. Never
 * carries a reference to the `THREE.Scene`, `Camera` or `WebGLRenderer` — only its own `group`. */
export interface DrawingSpaceHandle {
  readonly group: THREE.Group
  invalidate(): void
  onFrame(callback: FrameCallback): void
  declareDrawingRequirement(requirement: DrawingRequirement): void
}

function disposeObject3D(object: THREE.Object3D): void {
  object.traverse((child) => {
    const mesh = child as THREE.Mesh
    if (mesh.geometry) mesh.geometry.dispose()
    const material = mesh.material
    if (Array.isArray(material)) {
      material.forEach((m) => disposeMaterial(m))
    } else if (material) {
      disposeMaterial(material)
    }
  })
}

function disposeMaterial(material: THREE.Material): void {
  for (const value of Object.values(material)) {
    if (value && typeof value === 'object' && 'isTexture' in value) {
      ;(value as THREE.Texture).dispose()
    }
  }
  material.dispose()
}

/** research D3, D3a — issues each drawing capability its own isolated `THREE.Group` ("Drawing
 * Space"), appended to the one shared scene in acquisition order (FR-015), and contains any
 * exception a capability's `onFrame` callback throws (FR-019, constitution §2.VIII) so one
 * capability's failure never stops another's callback or the render loop. */
export class DrawingSpaceRegistry {
  private scene: THREE.Scene | null
  private onFailure: FailureReporter
  private readonly handles = new Map<string, DrawingSpaceHandle>()
  private readonly frameCallbacks = new Map<string, FrameCallback[]>()

  /** `scene`/`onFailure` are optional at construction (unit tests supply their own for a
   * self-contained instance); the production singleton is constructed with neither and `bind()`s
   * the real map-bridge scene once it exists asynchronously (T037) — mirrors `RedrawScheduler`/
   * `rendererState`'s own bind-after-construction convention, corrected from this task's original
   * constructor-injection sketch once it became clear the real scene is not available
   * synchronously at singleton-creation time. */
  constructor(scene: THREE.Scene | null = null, onFailure: FailureReporter = () => {}) {
    this.scene = scene
    this.onFailure = onFailure
  }

  /** Called once, alongside `bind(scene)`, to route `onFrame` containment failures (T009a) to the
   * real viewer event bus. */
  bindFailureReporter(onFailure: FailureReporter): void {
    this.onFailure = onFailure
  }

  /** Called once by the map bridge once its scene exists. Every group already acquired (a
   * capability that started before the map finished loading) is parented in, preserving
   * acquisition order (FR-015) — nothing acquired early is lost. */
  bind(scene: THREE.Scene): void {
    this.scene = scene
    for (const handle of this.handles.values()) {
      scene.add(handle.group)
    }
  }

  /** Idempotent per extension — calling it twice for the same id returns the same handle rather
   * than creating a second group (mirrors specs/050's start-when-started posture). */
  acquire(extensionId: string): DrawingSpaceHandle {
    const existing = this.handles.get(extensionId)
    if (existing) return existing

    const group = new THREE.Group()
    this.scene?.add(group)

    const handle: DrawingSpaceHandle = {
      group,
      invalidate: () => redrawScheduler.invalidate(),
      onFrame: (callback) => {
        const callbacks = this.frameCallbacks.get(extensionId) ?? []
        callbacks.push(callback)
        this.frameCallbacks.set(extensionId, callbacks)
      },
      declareDrawingRequirement: (requirement) => rendererState.declareRequirement(extensionId, requirement),
    }

    this.handles.set(extensionId, handle)
    return handle
  }

  /** Removes the group from the scene, disposes every descendant's geometry/material/texture,
   * clears every frame subscription, and withdraws every declared drawing requirement (FR-014,
   * FR-033). */
  release(extensionId: string): void {
    const handle = this.handles.get(extensionId)
    if (!handle) return

    this.scene?.remove(handle.group)
    disposeObject3D(handle.group)
    this.frameCallbacks.delete(extensionId)
    rendererState.withdrawRequirements(extensionId)
    this.handles.delete(extensionId)
  }

  /** Called once per actual draw by the map bridge (FR-023) — never by a capability. Each
   * subscriber's callback runs inside a try/catch (FR-019/T009a): a thrown callback is recorded
   * via `onFailure` and does not stop any other subscriber's callback from running on the same
   * frame. */
  invokeFrameCallbacks(deltaSeconds: number): void {
    for (const [extensionId, callbacks] of this.frameCallbacks) {
      for (const callback of callbacks) {
        try {
          callback(deltaSeconds)
        } catch (error) {
          const message = error instanceof Error ? error.message : String(error)
          this.onFailure(extensionId, message)
        }
      }
    }
  }
}

/** Single module-level instance for production use — bound to the real map-bridge scene once it
 * exists (T037), and its `onFailure` reporter wired to the viewer engine's event bus there too.
 * Mirrors every other viewer singleton's convention. */
export const drawingSpaceRegistry = new DrawingSpaceRegistry()

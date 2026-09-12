import type * as THREE from 'three'

/** The one live `THREE.Scene` the map bridge owns, published so viewer-owned content (not
 * capability-owned drawing spaces — see `DrawingSpaceRegistry.ts` for those) has somewhere to be
 * added. Bound once by `GoogleMapsGisLayer.ts` when its scene is created, mirroring
 * `RedrawScheduler`/`rendererState`'s own bind-after-construction convention. */
class ActiveSceneHolder {
  private scene: THREE.Scene | null = null

  bind(scene: THREE.Scene): void {
    this.scene = scene
  }

  get(): THREE.Scene | null {
    return this.scene
  }
}

export const activeScene = new ActiveSceneHolder()

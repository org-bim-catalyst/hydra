/** The camera a restore is trying to reach. Center is deliberately absent: Maps JS preserves the
 * center it was constructed with, and re-applying it would fight a pan the user has already
 * started. Only zoom and heading need defending. */
export interface RestoredCamera {
  zoom: number
  heading: number
}

export interface RestorableCameraTarget {
  /** The camera the map is showing right now, or `null` while it has no readable state. */
  getCamera(): { zoom: number; heading: number } | null
  /** One atomic camera write — only the fields given are changed. */
  setCamera(camera: { zoom?: number; heading?: number }): void
}

export interface CameraRestoreGuardOptions {
  /** Whether heading is this guard's to defend. False while auto-rotation is running: the
   * rotation driver is seeded with the restored heading and resumes from it, so it restores
   * heading on its own and a second writer would only fight it frame by frame. */
  shouldEnforceHeading(): boolean
}

/** Matches at the precision the map reports back — Maps JS returns floating-point zoom/heading,
 * so an exact equality test would never be satisfied and the guard would never release. */
const MATCH_EPSILON = 1e-3

/** An upper bound on how long the guard will argue with the map. Reaching it means something is
 * writing the camera on every settle, and continuing would be an unbounded loop rather than a
 * restore; the guard gives up and leaves the camera wherever it is. */
const MAX_ATTEMPTS = 20

/** Keeps a restored camera applied across Google's post-construction map initialisation.
 *
 * Passing zoom/heading/tilt as construction options is not enough to restore a camera: Maps JS
 * finishes wiring the map's camera *after* the constructor returns, and that wiring overwrites
 * them. Verified from call stacks against Maps JS 3.66 (2026-09-22): heading is zeroed by
 * `MVCObject.bindTo` → `heading_changed`, tilt by `bindTo` → `mapTypeId_changed` →
 * `actualTilt_changed`, and fractional zoom is snapped to a whole level by the map's own
 * internal zoom handling — none of them from any application frame.
 *
 * Tilt already survives this, because `MapRenderTarget` re-asserts the active view mode's tilt
 * on every `tilt_changed`. Heading only survived by accident, when auto-rotation happened to be
 * enabled. Zoom had no defence at all, which is why returning to the workspace changed the zoom
 * level every time. This guard gives zoom and heading the same durability tilt already had.
 *
 * It is deliberately short-lived: it stops at the first settle where the map already matches the
 * restored camera, and is released outright by the first user gesture — from that moment the
 * camera belongs to the user, not to the restore.
 */
export class CameraRestoreGuard {
  private attempts = 0
  private released = false

  private readonly target: RestorableCameraTarget
  private readonly desired: RestoredCamera
  private readonly options: CameraRestoreGuardOptions

  constructor(
    target: RestorableCameraTarget,
    desired: RestoredCamera,
    options: CameraRestoreGuardOptions,
  ) {
    this.target = target
    this.desired = desired
    this.options = options
  }

  get isActive(): boolean {
    return !this.released
  }

  /** Call on every camera-settled event (the map's `idle`). Re-applies whatever the map has
   * drifted away from, and releases as soon as there is nothing left to correct. */
  enforce(): void {
    if (this.released) return

    const current = this.target.getCamera()
    if (!current) return

    const correction: { zoom?: number; heading?: number } = {}
    if (Math.abs(current.zoom - this.desired.zoom) > MATCH_EPSILON) {
      correction.zoom = this.desired.zoom
    }
    if (
      this.options.shouldEnforceHeading() &&
      Math.abs(current.heading - this.desired.heading) > MATCH_EPSILON
    ) {
      correction.heading = this.desired.heading
    }

    if (correction.zoom === undefined && correction.heading === undefined) {
      // The map is showing the restored camera — the restore is complete and this guard has no
      // further reason to exist. Releasing here (rather than staying armed) is what keeps it
      // from ever fighting a later, legitimate camera change.
      this.released = true
      return
    }

    this.attempts += 1
    if (this.attempts > MAX_ATTEMPTS) {
      this.released = true
      return
    }
    this.target.setCamera(correction)
  }

  /** The user has taken hold of the camera (a drag, a wheel zoom). The restore is abandoned:
   * a guard that kept pulling the camera back would be indistinguishable from a broken map. */
  release(): void {
    this.released = true
  }
}

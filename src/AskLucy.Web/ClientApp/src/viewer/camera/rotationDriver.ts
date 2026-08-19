const ROTATION_SPEED_DEG_PER_SEC = 6

export interface RotationTarget {
  setHeading(heading: number): void
}

/** FR-014/FR-015: drives continuous heading rotation on whichever real render target is active,
 * via `requestAnimationFrame`. Pausing (`setEnabled(false)`) cancels the frame loop and holds
 * the current heading; resuming continues from that same heading rather than resetting to 0 —
 * "resumes from a smooth, natural starting point rather than jumping abruptly" (US3-AC4). */
export class RotationDriver {
  private frameId: number | null = null
  private heading = 0
  private lastTimestamp: number | null = null
  private enabled = false
  private readonly target: RotationTarget

  constructor(target: RotationTarget) {
    this.target = target
  }

  setEnabled(enabled: boolean): void {
    this.enabled = enabled
    if (enabled && this.frameId === null) {
      this.lastTimestamp = null
      this.frameId = requestAnimationFrame(this.tick)
    } else if (!enabled && this.frameId !== null) {
      cancelAnimationFrame(this.frameId)
      this.frameId = null
    }
  }

  dispose(): void {
    if (this.frameId !== null) cancelAnimationFrame(this.frameId)
    this.frameId = null
  }

  private tick = (timestamp: number): void => {
    if (!this.enabled) return
    if (this.lastTimestamp !== null) {
      const deltaSeconds = (timestamp - this.lastTimestamp) / 1000
      this.heading = (this.heading + ROTATION_SPEED_DEG_PER_SEC * deltaSeconds) % 360
      this.target.setHeading(this.heading)
    }
    this.lastTimestamp = timestamp
    this.frameId = requestAnimationFrame(this.tick)
  }
}

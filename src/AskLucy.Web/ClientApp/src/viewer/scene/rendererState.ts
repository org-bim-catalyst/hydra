import type * as THREE from 'three'

/** data-model.md "Drawing Requirement" — a feature of the viewer's rendering a capability
 * declares it needs, rather than switching on itself (FR-016). */
export type DrawingRequirement = 'shadows' | 'toneMapping'

type ConflictReporter = (requirement: DrawingRequirement, requestedBy: string[]) => void

/** research D3 — resolves the *union* of every currently-declared requirement onto the one shared
 * renderer (FR-016, FR-017). A requirement, once declared by any active capability, stays applied
 * until every capability that declared it has withdrawn. A capability never sets
 * `renderer.shadowMap.enabled`/`toneMapping` itself — only this module does, and only in response
 * to declared requirements. */
export class RendererState {
  private readonly declarations = new Map<DrawingRequirement, Set<string>>()
  private renderer: THREE.WebGLRenderer | null = null
  private onConflict: ConflictReporter | null = null

  /** Called once by the map bridge to supply the real renderer instance and the conflict-report
   * callback. Never called by a capability.
   *
   * FOUND LIVE (2026-09-13): an extension can call `declareRequirement` before this ever runs —
   * `start()` (where `solarAnalysisExtension`/`siteBoundaryExtension` declare 'shadows'/
   * 'toneMapping') is not ordered against the map bridge's async `onContextRestored`, which is
   * where this `bind()` call actually happens. `resolve()` previously only ran from
   * `declareRequirement`/`withdrawRequirements`, both no-ops while `this.renderer` was still
   * null — so a requirement declared that early was silently dropped forever, never applied even
   * once the renderer became available. Mirrors `DrawingSpaceRegistry.bind()`, which already
   * replays every group acquired before its own scene existed for the same reason. */
  bind(renderer: THREE.WebGLRenderer, onConflict: ConflictReporter): void {
    this.renderer = renderer
    this.onConflict = onConflict
    for (const requirement of this.declarations.keys()) {
      this.resolve(requirement)
    }
  }

  /** Rebinds just the conflict-report callback — used by `MapRenderTarget.tsx` to route it to the
   * real viewer event bus, which `GoogleMapsGisLayer.ts` deliberately does not depend on. */
  bindConflictReporter(onConflict: ConflictReporter): void {
    this.onConflict = onConflict
  }

  declareRequirement(extensionId: string, requirement: DrawingRequirement): void {
    let declarers = this.declarations.get(requirement)
    if (!declarers) {
      declarers = new Set()
      this.declarations.set(requirement, declarers)
    }
    declarers.add(extensionId)
    this.resolve(requirement)
  }

  /** Withdraws every requirement `extensionId` declared — called on extension stop. */
  withdrawRequirements(extensionId: string): void {
    for (const [requirement, declarers] of this.declarations) {
      if (declarers.delete(extensionId)) {
        this.resolve(requirement)
      }
    }
  }

  private resolve(requirement: DrawingRequirement): void {
    const declarers = this.declarations.get(requirement)
    const active = (declarers?.size ?? 0) > 0
    if (!this.renderer) return

    switch (requirement) {
      case 'shadows':
        this.renderer.shadowMap.enabled = active
        break
      case 'toneMapping':
        // FR-018's one-time treatment (research D5) already sets a considered default
        // (ACESFilmicToneMapping); a capability declaring 'toneMapping' asks to keep that
        // default active rather than something reverting it — there is no second tone curve
        // to switch between in this feature, so "declared" here means "at least one capability
        // still needs the treatment," not "which curve." A future capability needing a
        // genuinely different curve is exactly the FR-017 conflict case, reported below rather
        // than silently overriding.
        break
    }
  }

  /** FR-017 — reports (never silently decides) when two capabilities' declared requirements are
   * incompatible. This feature's only two requirements ('shadows', 'toneMapping') are additive
   * and never conflict with each other; this hook exists for a future requirement that does,
   * exercised by a synthetic test case rather than a real one today. */
  reportConflict(requirement: DrawingRequirement, requestedBy: string[]): void {
    this.onConflict?.(requirement, requestedBy)
  }
}

/** Single module-level instance, mirroring every other viewer singleton's convention. */
export const rendererState = new RendererState()

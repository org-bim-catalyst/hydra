import * as THREE from 'three'

export type BorderConfidenceLevel = 'low' | 'medium' | 'high'

export interface AnimatedBorderHighlight {
  /** Add this to the scene/group that owns the boundary. */
  object3D: THREE.Object3D
  /** Advances the comet animation(s) — call once per frame with the elapsed seconds. */
  update(deltaSeconds: number): void
  /** Rebuilds the glow/comet behavior for a new confidence level — no geometry rebuild needed for the static perimeter. */
  setConfidenceLevel(level: BorderConfidenceLevel): void
  dispose(): void
}

/** A single point in local scene-space meters (not lat/lng) — already projected by the caller. */
export interface LocalPoint {
  x: number
  y: number
}

const COMET_TAIL_LENGTH = 1.7
const COMET_SEGMENTS = 60
const COMET_SPEED_HIGH = 1.15
const COMET_SPEED_MEDIUM = 0.5

// specs/042-site-boundary-resolution — corrected after a live production check: the reference
// file's AdditiveBlending glow only reads against a near-black canvas (its own background is
// #02050a). Google's default roadmap basemap is light, and additive blending of any dim/mid-tone
// color onto already-bright pixels is visually imperceptible — the comets rendered but were
// invisible. Switched to normal alpha blending (below) with bold, opaque colors that read
// against light AND dark basemaps (roadmap/satellite/hybrid). The brand accent (#9C62DE) anchors
// medium/high confidence for visual continuity with SiteBoundaryConfidenceBadge.
const COLOR_HIGH_1 = 0x9c62de // brand accent — matches SiteBoundaryConfidenceBadge
const COLOR_HIGH_2 = 0x22d3ee // complementary cyan — second comet, high confidence only
const COLOR_MEDIUM = 0x9c62de
const COLOR_LOW_STATIC = 0x757575
const COLOR_STATIC_DEFAULT = 0x6a3fa0 // deeper violet — bold enough to read as a solid outline on any basemap

/**
 * specs/042-site-boundary-resolution — generalized, confidence-aware adaptation of
 * `docs/BORDER_HIGHLIGHT.html`'s technique: a dim static perimeter line plus animated
 * arc-length-parameterized "comet" segments with a head-brightening intensity curve. Unlike the
 * reference file, this uses normal (alpha) blending, not `AdditiveBlending` — additive only glows
 * against a near-black background, and the viewer's basemap is not reliably dark. Deliberately
 * still **not** using `UnrealBloomPass`/`EffectComposer` (research.md #9, viewer's GIS render path
 * runs no post-processing pipeline) — the "hot head, fading tail" character comes from ramping
 * alpha and mixing the head toward white, not from a bloom pass or from over-driving color values
 * past 1.0 (which would just clip to white under normal blending, losing the hue entirely).
 *
 * Takes any ordered, closed ring of points (not hardcoded to a rectangle) — the point of
 * "keep it modular so it can be reused for other projects."
 *
 * Confidence modulates the effect (FR-006 — visual, not just textual, distinction):
 * - `high`: full two-comet animation, brightest additive glow, solid perimeter.
 * - `medium`: one slower/dimmer comet, solid perimeter.
 * - `low`: static dashed perimeter only — no comets (this IS the approximation/uncertainty cue).
 */
export function createAnimatedBorderHighlight(
  ring: LocalPoint[],
  initialConfidenceLevel: BorderConfidenceLevel,
): AnimatedBorderHighlight {
  const group = new THREE.Group()
  const points = ring.map((p) => new THREE.Vector3(p.x, p.y, 0))

  // ============================================================
  // Static perimeter line — always present; genuinely dashed for `low` (previously used
  // LineBasicMaterial with computeLineDistances() called but never applied — dashing needs
  // LineDashedMaterial specifically, or the dash/gap sizes are silently ignored and the line
  // always renders solid regardless of confidence level).
  // ============================================================
  const staticGeometry = new THREE.BufferGeometry().setFromPoints(points)
  const staticMaterial = new THREE.LineDashedMaterial({
    color: COLOR_STATIC_DEFAULT,
    transparent: true,
    opacity: 0.9,
    dashSize: 1_000,
    gapSize: 0, // effectively solid for medium/high; overridden to a real dash pattern for `low`
  })
  const staticLine = new THREE.Line(staticGeometry, staticMaterial)
  staticLine.computeLineDistances()
  group.add(staticLine)

  // ============================================================
  // Perimeter distance table — shared by every comet.
  // ============================================================
  const distances: number[] = [0]
  let totalLength = 0
  for (let i = 0; i < points.length - 1; i++) {
    totalLength += points[i].distanceTo(points[i + 1])
    distances.push(totalLength)
  }

  function getPointAtDistance(distance: number): THREE.Vector3 {
    if (totalLength === 0) return points[0]?.clone() ?? new THREE.Vector3()
    const wrapped = ((distance % totalLength) + totalLength) % totalLength
    for (let i = 0; i < distances.length - 1; i++) {
      const start = distances[i]
      const end = distances[i + 1]
      if (wrapped >= start && wrapped <= end) {
        const t = end === start ? 0 : (wrapped - start) / (end - start)
        return points[i].clone().lerp(points[i + 1], t)
      }
    }
    return points[0].clone()
  }

  // ============================================================
  // Comet shader material — normal (alpha) blending, so it reads against any basemap brightness.
  // The reference file's fragment shader over-drove color up to 6x, relying on AdditiveBlending
  // to turn that into an on-top glow against a black canvas; under normal blending, multiplying
  // a color that far past 1.0 just clips to flat white, losing the hue entirely. Here the "hot
  // head, fading tail" character comes from alpha (near-opaque at the head, fading out along the
  // tail) and a mild mix-toward-white at the head, not from over-driving the raw color values.
  // ============================================================
  function createCometMaterial(color: number): THREE.ShaderMaterial {
    return new THREE.ShaderMaterial({
      uniforms: { uColor: { value: new THREE.Color(color) } },
      vertexShader: `
        attribute float aProgress;
        varying float vProgress;
        void main() {
          vProgress = aProgress;
          gl_Position = projectionMatrix * modelViewMatrix * vec4(position, 1.0);
        }
      `,
      fragmentShader: `
        uniform vec3 uColor;
        varying float vProgress;
        void main() {
          float tailFade = smoothstep(0.0, 0.18, vProgress);
          float head = smoothstep(0.55, 1.0, vProgress);
          float alpha = pow(vProgress, 1.6) * tailFade;
          alpha = clamp(alpha * (0.55 + head * 0.55), 0.0, 1.0);
          vec3 color = mix(uColor, vec3(1.0), head * 0.5);
          gl_FragColor = vec4(color, alpha);
        }
      `,
      transparent: true,
      depthWrite: false,
    })
  }

  interface Comet {
    line: THREE.Line
    geometry: THREE.BufferGeometry
    position: number
    speed: number
  }

  const comets: Comet[] = []

  function clearComets() {
    for (const comet of comets) {
      group.remove(comet.line)
      comet.geometry.dispose()
      ;(comet.line.material as THREE.Material).dispose()
    }
    comets.length = 0
  }

  function updateCometGeometry(comet: Comet) {
    const positions = new Float32Array(COMET_SEGMENTS * 3)
    const progress = new Float32Array(COMET_SEGMENTS)
    for (let i = 0; i < COMET_SEGMENTS; i++) {
      const t = i / (COMET_SEGMENTS - 1)
      const distance = comet.position - COMET_TAIL_LENGTH + COMET_TAIL_LENGTH * t
      const point = getPointAtDistance(distance)
      positions[i * 3] = point.x
      positions[i * 3 + 1] = point.y
      positions[i * 3 + 2] = 0.01
      progress[i] = t
    }
    comet.geometry.setAttribute('position', new THREE.BufferAttribute(positions, 3))
    comet.geometry.setAttribute('aProgress', new THREE.BufferAttribute(progress, 1))
  }

  function buildForConfidence(level: BorderConfidenceLevel) {
    clearComets()

    if (level === 'low') {
      staticMaterial.color.setHex(COLOR_LOW_STATIC)
      staticMaterial.opacity = 0.8
      // A real dash pattern (see the LineDashedMaterial note above) — this IS the
      // approximation/uncertainty cue for `low` (FR-006), not just a dimmer solid line.
      staticMaterial.dashSize = 8
      staticMaterial.gapSize = 5
      return
    }

    staticMaterial.color.setHex(COLOR_STATIC_DEFAULT)
    staticMaterial.opacity = 0.9
    staticMaterial.dashSize = 1_000
    staticMaterial.gapSize = 0

    const specs: { color: number; startFraction: number; speed: number }[] =
      level === 'high'
        ? [
            { color: COLOR_HIGH_1, startFraction: 0, speed: COMET_SPEED_HIGH },
            { color: COLOR_HIGH_2, startFraction: 0.5, speed: COMET_SPEED_HIGH },
          ]
        : [{ color: COLOR_MEDIUM, startFraction: 0, speed: COMET_SPEED_MEDIUM }]

    for (const spec of specs) {
      const geometry = new THREE.BufferGeometry()
      const material = createCometMaterial(spec.color)
      const line = new THREE.Line(geometry, material)
      group.add(line)
      const comet: Comet = { line, geometry, position: totalLength * spec.startFraction, speed: spec.speed }
      updateCometGeometry(comet)
      comets.push(comet)
    }
  }

  buildForConfidence(initialConfidenceLevel)

  return {
    object3D: group,
    update(deltaSeconds) {
      for (const comet of comets) {
        comet.position += comet.speed * deltaSeconds
        updateCometGeometry(comet)
      }
    },
    setConfidenceLevel(level) {
      buildForConfidence(level)
    },
    dispose() {
      clearComets()
      group.remove(staticLine)
      staticGeometry.dispose()
      staticMaterial.dispose()
    },
  }
}

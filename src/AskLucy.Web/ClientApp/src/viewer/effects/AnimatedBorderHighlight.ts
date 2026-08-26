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

const COLOR_HIGH_1 = 0x00eaff
const COLOR_HIGH_2 = 0x4d7cff
const COLOR_MEDIUM = 0x6d93a8
const COLOR_LOW_STATIC = 0x8a8a8a
const COLOR_STATIC_DEFAULT = 0x1c3d47

/**
 * specs/042-site-boundary-resolution — generalized, confidence-aware adaptation of
 * `docs/BORDER_HIGHLIGHT.html`'s technique: a dim static perimeter line plus animated
 * arc-length-parameterized "comet" segments using additive blending and a head-brightening
 * intensity curve. Deliberately **not** using `UnrealBloomPass`/`EffectComposer` — confirmed the
 * viewer's GIS render path runs no post-processing pipeline (research.md #9); the glow here comes
 * entirely from the shader's own additive blending, matching where most of the reference file's
 * visual impact already comes from.
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
  // Static perimeter line — always present; dashed only for `low`.
  // ============================================================
  const staticGeometry = new THREE.BufferGeometry().setFromPoints(points)
  const staticMaterial = new THREE.LineBasicMaterial({
    color: COLOR_STATIC_DEFAULT,
    transparent: true,
    opacity: 0.7,
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
  // Comet shader material — additive blending + head-brightening intensity curve,
  // ported directly from docs/BORDER_HIGHLIGHT.html's fragment shader.
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
          float intensity = pow(vProgress, 2.8);
          intensity *= 0.25 + head * 3.0;
          float alpha = intensity * tailFade;
          vec3 color = uColor * (1.0 + head * 5.0);
          gl_FragColor = vec4(color, alpha);
        }
      `,
      transparent: true,
      blending: THREE.AdditiveBlending,
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
      staticMaterial.opacity = 0.5
      return
    }

    staticMaterial.color.setHex(COLOR_STATIC_DEFAULT)
    staticMaterial.opacity = 0.7

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

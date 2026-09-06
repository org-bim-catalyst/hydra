import * as THREE from 'three'

export type BorderConfidenceLevel = 'low' | 'medium' | 'high'

export interface AnimatedBorderHighlight {
  /** Add this to the scene/group that owns the boundary. */
  object3D: THREE.Object3D
  /** Advances the border rotation and checkpoint activation — call once per frame with the elapsed seconds. */
  update(deltaSeconds: number): void
  /** Rebuilds the border/activation behavior for a new confidence level — no geometry rebuild needed for the static perimeter. */
  setConfidenceLevel(level: BorderConfidenceLevel): void
  dispose(): void
}

/** A single point in local scene-space meters (not lat/lng) — already projected by the caller. */
export interface LocalPoint {
  x: number
  y: number
}

/** How long the one-time "checkpoint reached" activation plays before settling into the ambient loop. */
const ACTIVATION_DURATION_SECONDS = 0.9

// A shader translation of a CSS conic-gradient spinning-border technique
// (coding2go.com/tutorials/border-animation): a rainbow ring rotates around the shape via an
// animated `--angle` custom property, drawn crisp with no blur/glow (an earlier stacked-band
// glow attempt read as concentric squares — flat per-layer opacity has a hard step at every
// layer's own edge; this was dropped rather than fixed, since the confirmed direction is a thin,
// crisp, glowless ring). Same four colour stops as the reference, same colour-ramp technique
// (four mix() segments picked by step(), which avoids dynamic array indexing for GPU/driver
// compatibility) — computed per-fragment from the angle around the shape's own centroid instead
// of a CSS custom property, traced along the boundary's real perimeter with round corner joins
// (the reference's card has a CSS border-radius; a naive shared-vertex miter join here produced
// visible spikes at sharp corners, replaced with per-edge-independent rectangles plus a filled
// disc at every vertex).
const BORDER_ROTATION_SECONDS_HIGH = 3 // matches the CSS reference's "3s spin linear infinite"
const BORDER_ROTATION_SECONDS_MEDIUM = 6 // slower — FR-006's medium/high visual distinction
const BORDER_OPACITY_MEDIUM = 0.75
// The ring's width is real geometry (a THREE.Mesh), unlike a THREE.Line's constant ~1px screen
// width regardless of scale — a fixed metre value tuned against a small demo shape (a few metres
// across) is imperceptible around an actual, real-world-scale park (hundreds of metres across):
// this was live-verified as a bug — the ring never became visible on a real boundary, only the
// checkpoint shockwave (a Line) did. Scaling the half-width to a fraction of the boundary's own
// bounding-box diagonal keeps it reading equally thin whether the parcel is 50m or 500m across,
// with a floor so a tiny lot doesn't get an imperceptibly thin ring either.
const BORDER_WIDTH_RATIO = 0.002
const BORDER_MIN_HALF_WIDTH_METERS = 0.15
const BORDER_CORNER_SEGMENTS = 12

const CONIC_GRADIENT_GLSL = `
  vec3 conicGradient(float t) {
    t = fract(t) * 4.0;
    vec3 c0 = vec3(1.0, 0.2706, 0.2706); // #ff4545
    vec3 c1 = vec3(0.0, 1.0, 0.6);       // #00ff99
    vec3 c2 = vec3(0.0, 0.4157, 1.0);    // #006aff
    vec3 c3 = vec3(1.0, 0.0, 0.5843);    // #ff0095
    vec3 result = mix(c0, c1, clamp(t, 0.0, 1.0));
    result = mix(result, mix(c1, c2, clamp(t - 1.0, 0.0, 1.0)), step(1.0, t));
    result = mix(result, mix(c2, c3, clamp(t - 2.0, 0.0, 1.0)), step(2.0, t));
    result = mix(result, mix(c3, c0, clamp(t - 3.0, 0.0, 1.0)), step(3.0, t));
    return result;
  }
`

const COLOR_LOW_STATIC = 0x757575
const COLOR_STATIC_DEFAULT = 0x6a3fa0 // unused once the border ring replaces the flat perimeter, kept for `low`
// The one-time "checkpoint reached" shockwave ring's own gradient (independent of the border
// ring's rotating rainbow) — brand purple fading to complementary cyan.
const SHOCKWAVE_COLOR_START = 0x9c62de
const SHOCKWAVE_COLOR_END = 0x22d3ee
const SHOCKWAVE_HALO_SCALES = [1.05, 1.1]
const SHOCKWAVE_HALO_PEAK_OPACITIES = [0.5, 0.25]

/**
 * specs/042-site-boundary-resolution — the confirmed boundary's border, for medium/high
 * confidence: a rotating rainbow ring translated from a CSS `conic-gradient` spinning-border
 * technique (see `CONIC_GRADIENT_GLSL` above), replacing an earlier animated-comet design.
 * `low` confidence is unaffected — a plain dashed, muted perimeter, no ring, no activation; an
 * uncertain/approximate result shouldn't get a celebratory arrival (FR-006).
 *
 * Takes any ordered, closed ring of points (not hardcoded to a rectangle) — the point of
 * "keep it modular so it can be reused for other projects."
 *
 * 2026-09-06: added a one-time "checkpoint reached" activation for medium/high — every
 * `setPolygon` call rebuilds this from scratch (see `SiteBoundaryRenderer`), so every boundary
 * confirmation gets the flourish, not just the first ever. A ring the same shape as the boundary
 * itself scales in from the centroid (starting collapsed, expanding to full size) while fading
 * out, and the border ring fades in underneath it over the same window, so the ambient rotation
 * is already settled by the time the activation ring finishes — a brief "arrival" moment rather
 * than the boundary just appearing. The border ring then keeps rotating indefinitely afterward
 * (an ambient "confirmed and energized" state), matching the CSS reference's own infinite loop.
 */
export function createAnimatedBorderHighlight(
  ring: LocalPoint[],
  initialConfidenceLevel: BorderConfidenceLevel,
): AnimatedBorderHighlight {
  const group = new THREE.Group()
  const points = ring.map((p) => new THREE.Vector3(p.x, p.y, 0))
  const centroid = points.reduce((sum, p) => sum.add(p), new THREE.Vector3()).divideScalar(points.length)
  const bounds = new THREE.Box3().setFromPoints(points)
  const borderHalfWidth = Math.max(bounds.min.distanceTo(bounds.max) * BORDER_WIDTH_RATIO, BORDER_MIN_HALF_WIDTH_METERS)

  function angleFromCentroid(p: THREE.Vector3): number {
    return (Math.atan2(p.y - centroid.y, p.x - centroid.x) / (Math.PI * 2) + 1) % 1
  }

  // ============================================================
  // Static perimeter line — `low` confidence only now. Genuinely dashed (LineBasicMaterial with
  // computeLineDistances() called but never applied wouldn't dash — LineDashedMaterial is
  // required, or the dash/gap sizes are silently ignored and the line always renders solid).
  // Hidden (not disposed — cheap to keep around) once medium/high replaces it with the border ring.
  // ============================================================
  const staticGeometry = new THREE.BufferGeometry().setFromPoints(points)
  const staticMaterial = new THREE.LineDashedMaterial({
    color: COLOR_STATIC_DEFAULT,
    transparent: true,
    opacity: 0.9,
    dashSize: 1_000,
    gapSize: 0,
  })
  const staticLine = new THREE.Line(staticGeometry, staticMaterial)
  staticLine.computeLineDistances()
  group.add(staticLine)

  // ============================================================
  // Border ring — medium/high confidence. A closed ribbon traced along the boundary's own
  // perimeter, coloured by a rotating conic gradient. Built once per confidence level (geometry
  // is static; only the rotation uniform animates), independent per-edge rectangles (not a
  // shared vertex-averaged normal, which produces a sharp miter spike at corners) plus a filled
  // disc at every vertex for a proper round join.
  // ============================================================
  function createBorderMaterial(opacity: number): THREE.ShaderMaterial {
    return new THREE.ShaderMaterial({
      uniforms: { uRotation: { value: 0 }, uOpacity: { value: opacity }, uIntro: { value: 0 } },
      vertexShader: `
        attribute float aAngle;
        attribute float aEdge;
        varying float vAngle;
        varying float vEdge;
        void main() {
          vAngle = aAngle;
          vEdge = aEdge;
          gl_Position = projectionMatrix * modelViewMatrix * vec4(position, 1.0);
        }
      `,
      fragmentShader: `
        uniform float uRotation;
        uniform float uOpacity;
        uniform float uIntro;
        varying float vAngle;
        varying float vEdge;
        ${CONIC_GRADIENT_GLSL}
        void main() {
          float d = abs(vEdge); // 0 at the centreline, 1 at the ribbon's outer edge
          float alpha = 1.0 - smoothstep(0.82, 1.0, d); // crisp edge, just enough softness for AA
          vec3 color = conicGradient(vAngle - uRotation);
          gl_FragColor = vec4(color, alpha * uOpacity * uIntro);
        }
      `,
      transparent: true,
      depthWrite: false,
      side: THREE.DoubleSide,
    })
  }

  function buildBorderRingGeometry(halfWidth: number): THREE.BufferGeometry {
    const n = points.length - 1 // ring closes with points[n] === points[0]
    const positions: number[] = []
    const angles: number[] = []
    const edges: number[] = []
    const indices: number[] = []

    function pushVertex(x: number, y: number, angle: number, edge: number): number {
      positions.push(x, y, 0.01)
      angles.push(angle)
      edges.push(edge)
      return positions.length / 3 - 1
    }

    for (let i = 0; i < n; i++) {
      const a = points[i]
      const b = points[(i + 1) % n]
      let tx = b.x - a.x
      let ty = b.y - a.y
      const len = Math.hypot(tx, ty) || 1
      tx /= len
      ty /= len
      const px = -ty * halfWidth
      const py = tx * halfWidth
      const i0 = pushVertex(a.x + px, a.y + py, angleFromCentroid(a), 1)
      const i1 = pushVertex(a.x - px, a.y - py, angleFromCentroid(a), -1)
      const i2 = pushVertex(b.x + px, b.y + py, angleFromCentroid(b), 1)
      const i3 = pushVertex(b.x - px, b.y - py, angleFromCentroid(b), -1)
      indices.push(i0, i1, i2, i1, i3, i2)
    }

    for (let i = 0; i < n; i++) {
      const p = points[i]
      const angle = angleFromCentroid(p)
      const centerIdx = pushVertex(p.x, p.y, angle, 0)
      const ringStart = positions.length / 3
      for (let s = 0; s <= BORDER_CORNER_SEGMENTS; s++) {
        const theta = (s / BORDER_CORNER_SEGMENTS) * Math.PI * 2
        pushVertex(p.x + Math.cos(theta) * halfWidth, p.y + Math.sin(theta) * halfWidth, angle, 1)
      }
      for (let s = 0; s < BORDER_CORNER_SEGMENTS; s++) {
        indices.push(centerIdx, ringStart + s, ringStart + s + 1)
      }
    }

    const geometry = new THREE.BufferGeometry()
    geometry.setAttribute('position', new THREE.BufferAttribute(new Float32Array(positions), 3))
    geometry.setAttribute('aAngle', new THREE.BufferAttribute(new Float32Array(angles), 1))
    geometry.setAttribute('aEdge', new THREE.BufferAttribute(new Float32Array(edges), 1))
    geometry.setIndex(indices)
    return geometry
  }

  interface BorderRing {
    mesh: THREE.Mesh
    geometry: THREE.BufferGeometry
    material: THREE.ShaderMaterial
    rotationSeconds: number
  }

  let borderRing: BorderRing | null = null
  let elapsedSeconds = 0

  function buildBorderRing(rotationSeconds: number, targetOpacity: number) {
    const geometry = buildBorderRingGeometry(borderHalfWidth)
    const material = createBorderMaterial(targetOpacity)
    const mesh = new THREE.Mesh(geometry, material)
    group.add(mesh)
    borderRing = { mesh, geometry, material, rotationSeconds }
  }

  function disposeBorderRing() {
    if (!borderRing) return
    group.remove(borderRing.mesh)
    borderRing.geometry.dispose()
    borderRing.material.dispose()
    borderRing = null
  }

  // ============================================================
  // Checkpoint activation — a copy of the boundary's own shape, scaled in from its centroid, in
  // its own purple-to-cyan gradient (independent of the border ring's rotating rainbow) with two
  // additive "halo" copies for a soft glow. Built fresh by buildForConfidence() below whenever
  // it's called with medium/high, i.e. on every real confirmation (SiteBoundaryRenderer.setPolygon
  // always rebuilds from scratch).
  // ============================================================
  interface ShockwaveHalo {
    line: THREE.Line
    geometry: THREE.BufferGeometry
    material: THREE.LineBasicMaterial
    scaleMultiplier: number
    peakOpacity: number
  }

  let shockwave: {
    coreLine: THREE.Line
    coreGeometry: THREE.BufferGeometry
    coreMaterial: THREE.LineBasicMaterial
    halos: ShockwaveHalo[]
  } | null = null
  let activationElapsedSeconds = 0
  let activationActive = false

  function buildShockwaveRingGeometry(centeredPoints: THREE.Vector3[]): THREE.BufferGeometry {
    const geometry = new THREE.BufferGeometry().setFromPoints(centeredPoints)
    const start = new THREE.Color(SHOCKWAVE_COLOR_START)
    const end = new THREE.Color(SHOCKWAVE_COLOR_END)
    const colors = new Float32Array(centeredPoints.length * 3)
    for (let i = 0; i < centeredPoints.length; i++) {
      const t = i / (centeredPoints.length - 1)
      const c = start.clone().lerp(end, t)
      colors[i * 3] = c.r
      colors[i * 3 + 1] = c.g
      colors[i * 3 + 2] = c.b
    }
    geometry.setAttribute('color', new THREE.BufferAttribute(colors, 3))
    return geometry
  }

  function buildShockwave() {
    const centeredPoints = points.map((p) => p.clone().sub(centroid))

    const coreGeometry = buildShockwaveRingGeometry(centeredPoints)
    const coreMaterial = new THREE.LineBasicMaterial({ vertexColors: true, transparent: true, opacity: 1 })
    const coreLine = new THREE.Line(coreGeometry, coreMaterial)
    coreLine.position.copy(centroid)
    coreLine.scale.setScalar(0.01)
    group.add(coreLine)

    const halos: ShockwaveHalo[] = SHOCKWAVE_HALO_SCALES.map((scaleMultiplier, i) => {
      const geometry = buildShockwaveRingGeometry(centeredPoints)
      const material = new THREE.LineBasicMaterial({
        vertexColors: true,
        transparent: true,
        opacity: 0,
        blending: THREE.AdditiveBlending,
        depthWrite: false,
      })
      const line = new THREE.Line(geometry, material)
      line.position.copy(centroid)
      line.scale.setScalar(0.01)
      group.add(line)
      return { line, geometry, material, scaleMultiplier, peakOpacity: SHOCKWAVE_HALO_PEAK_OPACITIES[i] }
    })

    shockwave = { coreLine, coreGeometry, coreMaterial, halos }
    activationElapsedSeconds = 0
    activationActive = true
    if (borderRing) borderRing.material.uniforms.uIntro.value = 0
  }

  function disposeShockwave() {
    activationActive = false
    if (!shockwave) return
    group.remove(shockwave.coreLine)
    shockwave.coreGeometry.dispose()
    shockwave.coreMaterial.dispose()
    for (const halo of shockwave.halos) {
      group.remove(halo.line)
      halo.geometry.dispose()
      halo.material.dispose()
    }
    shockwave = null
  }

  function advanceActivation(deltaSeconds: number) {
    if (!activationActive || !shockwave) return

    activationElapsedSeconds += deltaSeconds
    const t = Math.min(1, activationElapsedSeconds / ACTIVATION_DURATION_SECONDS)
    const eased = 1 - (1 - t) ** 3 // ease-out cubic — fast start, gentle settle
    const scale = 0.01 + eased * 0.99
    const fade = 1 - eased

    shockwave.coreLine.scale.setScalar(scale)
    shockwave.coreMaterial.opacity = fade
    for (const halo of shockwave.halos) {
      halo.line.scale.setScalar(scale * halo.scaleMultiplier)
      halo.material.opacity = fade * halo.peakOpacity
    }

    if (borderRing) borderRing.material.uniforms.uIntro.value = eased

    if (t >= 1) {
      disposeShockwave()
      if (borderRing) borderRing.material.uniforms.uIntro.value = 1
    }
  }

  function buildForConfidence(level: BorderConfidenceLevel) {
    disposeBorderRing()
    disposeShockwave()

    if (level === 'low') {
      staticLine.visible = true
      staticMaterial.color.setHex(COLOR_LOW_STATIC)
      staticMaterial.opacity = 0.8
      // A real dash pattern (see the LineDashedMaterial note above) — this IS the
      // approximation/uncertainty cue for `low` (FR-006), not just a dimmer solid line.
      staticMaterial.dashSize = 8
      staticMaterial.gapSize = 5
      return
    }

    // The rotating border ring below replaces the flat perimeter for medium/high.
    staticLine.visible = false

    const rotationSeconds = level === 'high' ? BORDER_ROTATION_SECONDS_HIGH : BORDER_ROTATION_SECONDS_MEDIUM
    const targetOpacity = level === 'high' ? 1 : BORDER_OPACITY_MEDIUM
    buildBorderRing(rotationSeconds, targetOpacity)

    // Medium/high only — a low-confidence, approximate result doesn't get a celebratory arrival.
    buildShockwave()
  }

  buildForConfidence(initialConfidenceLevel)

  return {
    object3D: group,
    update(deltaSeconds) {
      elapsedSeconds += deltaSeconds
      if (borderRing) {
        borderRing.material.uniforms.uRotation.value = (elapsedSeconds / borderRing.rotationSeconds) % 1
      }
      advanceActivation(deltaSeconds)
    },
    setConfidenceLevel(level) {
      buildForConfidence(level)
    },
    dispose() {
      disposeBorderRing()
      disposeShockwave()
      group.remove(staticLine)
      staticGeometry.dispose()
      staticMaterial.dispose()
    },
  }
}

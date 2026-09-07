import { useFrame } from '@react-three/fiber'
import { useMemo, useRef } from 'react'
import * as THREE from 'three'
import { useThemeStore } from '../../../store/themeStore'
import auraGlowUrl from '../../../assets/chat-scene/aura-glow.png'
import outerGlowUrl from '../../../assets/chat-scene/outer-glow.png'
import type { FrequencyBands } from '../voice/useVoiceAnalyzer'
import { getDotMeshColors } from './dotMeshTheme'
import { SPHERE_RADIUS } from './ReactiveSphere'
import { computeBreathValue } from './sphereBreath'
import fragmentShader from './magmaGlow.frag.glsl?raw'
import vertexShader from './magmaGlow.vert.glsl?raw'

// Radii/scale ratios preserved from the ics.media "magma effect" demo (magmaFlare/{Aura,InGlow,
// OutGlow}.ts), scaled down from its own r=2 base sphere to this project's SPHERE_RADIUS.
const RADIUS_SCALE = SPHERE_RADIUS / 2
const AURA_RADIUS = 2.02 * RADIUS_SCALE
const IN_GLOW_RADIUS = 2.03 * RADIUS_SCALE
const OUT_GLOW_SCALE = 11 * RADIUS_SCALE

const AURA_SCROLL_SPEED = 0.25 // matches the demo's own -performance.now() / 1000 / 4
const AURA_OPACITY_IDLE = 0.6
const AURA_OPACITY_REACTIVE_MAX = 1.0

// The demo's InGlow used a fixed 0.55 alpha strength; idle/reactive range here instead so
// speaking reads as a brighter rim, matching how the rest of this sphere reacts to volume.
const IN_GLOW_STRENGTH_IDLE = 0.4
const IN_GLOW_STRENGTH_REACTIVE_MAX = 0.7

const OUT_GLOW_OPACITY_IDLE = 0.5
const OUT_GLOW_OPACITY_REACTIVE_MAX = 0.9

// Same constants ReactiveSphere.tsx uses for its own idle breathing pulse. Both components start
// their own local `elapsed` clock at 0 on mount and advance it by the same per-frame delta, so
// reusing these keeps this layer's pulse in sync with the particle sphere it's layered onto,
// without the two needing to share a single clock instance.
const BREATH_FREQUENCY = 0.6
const BREATH_AMPLITUDE = 0.035
// A group-scale breathing pulse reads more strongly than ReactiveSphere's own per-point radial
// displacement at the same amplitude, so it's damped here to stay equally subtle.
const GROUP_BREATH_SCALE = 0.5

// Scratch objects reused every frame instead of allocating a new THREE.Color per material per
// frame - three additive layers x 60fps adds up to real, avoidable GC churn otherwise.
const scratchIdleColor = new THREE.Color()
const scratchReactiveColor = new THREE.Color()

interface MagmaGlowSphereProps {
  /** Same real per-band FFT getter ReactiveSphere/SphereBloom already read every frame - see
   * ReactiveSphere.tsx's own doc comment for why this is a ref-based getter, not a prop value. */
  getFrequencyBands: () => FrequencyBands
  /** Freezes texture scrolling and breathing when the user prefers reduced motion, matching
   * ReactiveSphere's own handling. */
  reducedMotion: boolean
}

/** Aura / Glow Inside / Glow Outside layers from ics.media's "magma effect" demo
 * (https://ics.media/en/entry/13973/, source magmaFlare/{Aura,InGlow,OutGlow}.ts), layered onto
 * the particle sphere (ReactiveSphere.tsx) rather than replacing it - live user request,
 * 2026-09-07, from a customized version of that demo with only these 3 of its 6 layers enabled
 * (the Magma lava texture, the Flare rings, and the 500-sprite Spark emitter are intentionally
 * not ported here - not part of what was asked for, and real added GPU cost on hardware this
 * session already spent hours getting stable). Despite the demo itself running on WebGPU/TSL,
 * these 3 layers are simple enough to be plain THREE.Mesh/Sprite + GLSL - see magmaGlow.frag.glsl
 * for the InGlow port. Mounted as a child of ReactiveSphere's own `<group>` so it inherits that
 * group's rotation/position for free and reads as one object, not two independently-animating
 * overlays. Colors come from the same theme-driven getDotMeshColors ReactiveSphere/sphere.frag.glsl
 * already use (not the demo's own hardcoded blue), so this layer doesn't clash with the particle
 * sphere's own palette. */
export function MagmaGlowSphere({ getFrequencyBands, reducedMotion }: MagmaGlowSphereProps) {
  const groupRef = useRef<THREE.Group>(null)
  const auraMaterialRef = useRef<THREE.MeshBasicMaterial>(null)
  const inGlowMaterialRef = useRef<THREE.ShaderMaterial>(null)
  const outGlowMaterialRef = useRef<THREE.SpriteMaterial>(null)
  const elapsed = useRef(0)
  const mode = useThemeStore((s) => s.mode)

  // Plain THREE.TextureLoader (matching the ics.media demo's own Aura.ts/OutGlow.ts), not
  // @react-three/drei's useTexture - the Aura texture's offset needs animating every frame
  // (below), and this project's react-hooks/immutability lint rule (correctly) rejects mutating
  // a value useTexture returns, since it's treated as hook-owned. A texture built here, in
  // useMemo, is plainly owned by this component instead.
  const auraMap = useMemo(() => {
    const texture = new THREE.TextureLoader().load(auraGlowUrl)
    texture.colorSpace = THREE.SRGBColorSpace
    texture.wrapS = texture.wrapT = THREE.RepeatWrapping
    return texture
  }, [])
  const outGlowMap = useMemo(() => {
    const texture = new THREE.TextureLoader().load(outerGlowUrl)
    texture.colorSpace = THREE.SRGBColorSpace
    return texture
  }, [])

  const inGlowUniforms = useMemo(
    () => ({
      uColor: { value: new THREE.Color() },
      uStrength: { value: IN_GLOW_STRENGTH_IDLE },
    }),
    [],
  )

  useFrame((_, delta) => {
    if (!reducedMotion) {
      elapsed.current += delta
    }

    const bands = getFrequencyBands()
    const reactiveIntensity = reducedMotion ? 0 : Math.max(bands.low, bands.mid, bands.high)
    const colors = getDotMeshColors(mode)
    const mixedColor = scratchIdleColor.set(colors.idle).lerp(scratchReactiveColor.set(colors.reactive), reactiveIntensity)

    if (auraMaterialRef.current) {
      // Routed through the material ref's own `.map` (not the closed-over `auraMap` variable
      // useMemo returned) - matches this file's ReactiveSphere.tsx sibling's own pattern for
      // mutating a memoized value from inside useFrame (there: `material.uniforms`, here:
      // `material.map`), which this project's react-hooks/immutability lint rule accepts.
      const map = auraMaterialRef.current.map as THREE.Texture
      map.offset.x = -elapsed.current * AURA_SCROLL_SPEED
      map.offset.y = -elapsed.current * AURA_SCROLL_SPEED
      auraMaterialRef.current.opacity =
        AURA_OPACITY_IDLE + reactiveIntensity * (AURA_OPACITY_REACTIVE_MAX - AURA_OPACITY_IDLE)
      auraMaterialRef.current.color.copy(mixedColor)
    }

    if (inGlowMaterialRef.current) {
      const u = inGlowMaterialRef.current.uniforms as typeof inGlowUniforms
      u.uStrength.value =
        IN_GLOW_STRENGTH_IDLE + reactiveIntensity * (IN_GLOW_STRENGTH_REACTIVE_MAX - IN_GLOW_STRENGTH_IDLE)
      u.uColor.value.copy(mixedColor)
    }

    if (outGlowMaterialRef.current) {
      outGlowMaterialRef.current.opacity =
        OUT_GLOW_OPACITY_IDLE + reactiveIntensity * (OUT_GLOW_OPACITY_REACTIVE_MAX - OUT_GLOW_OPACITY_IDLE)
      outGlowMaterialRef.current.color.copy(mixedColor)
    }

    if (groupRef.current && !reducedMotion) {
      const breath = computeBreathValue(elapsed.current, BREATH_FREQUENCY, BREATH_AMPLITUDE)
      groupRef.current.scale.setScalar(1 + breath * GROUP_BREATH_SCALE)
    }
  })

  return (
    <group ref={groupRef}>
      <mesh>
        <sphereGeometry args={[AURA_RADIUS, 40, 40]} />
        <meshBasicMaterial
          ref={auraMaterialRef}
          map={auraMap}
          blending={THREE.AdditiveBlending}
          transparent
          depthWrite={false}
        />
      </mesh>
      <mesh>
        <sphereGeometry args={[IN_GLOW_RADIUS, 40, 40]} />
        <shaderMaterial
          ref={inGlowMaterialRef}
          vertexShader={vertexShader}
          fragmentShader={fragmentShader}
          uniforms={inGlowUniforms}
          blending={THREE.AdditiveBlending}
          transparent
          depthWrite={false}
          side={THREE.FrontSide}
        />
      </mesh>
      <sprite scale={[OUT_GLOW_SCALE, OUT_GLOW_SCALE, 1]}>
        <spriteMaterial
          ref={outGlowMaterialRef}
          map={outGlowMap}
          blending={THREE.AdditiveBlending}
          transparent
          depthWrite={false}
        />
      </sprite>
    </group>
  )
}

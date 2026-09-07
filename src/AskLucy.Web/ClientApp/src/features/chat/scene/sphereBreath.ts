/** Idle "breathing" pulse value for the particle sphere (spec 011-particle-sphere-engine
 * FR-006, research.md §4) — a simple sine, distinct from the existing noise-driven idle
 * wobble and voice-reactive amplitude, folded additively into the vertex shader's per-point
 * displacement (`uBreath` in sphere.vert.glsl) rather than scaling the whole `<group>`. */
export function computeBreathValue(elapsedSeconds: number, frequency: number, amplitude: number): number {
  return Math.sin(elapsedSeconds * frequency) * amplitude
}

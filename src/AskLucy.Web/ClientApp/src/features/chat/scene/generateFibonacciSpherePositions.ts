const GOLDEN_ANGLE = Math.PI * (3 - Math.sqrt(5))

/** Samples points on a unit sphere using a golden-angle Fibonacci lattice (spec
 * 011-particle-sphere-engine FR-001, research.md §1) — every point gets a unique latitude band
 * *and* a golden-angle azimuthal offset, giving a uniform pole-to-equator spread with no visible
 * ring banding, unlike the ring-grouped sampling it replaces (`generateRingSpherePositions`,
 * spec 010-lucy-brand-refresh). */
export function generateFibonacciSpherePositions(totalPoints: number, radius: number): Float32Array {
  const positions = new Float32Array(totalPoints * 3)

  for (let i = 0; i < totalPoints; i++) {
    const y = 1 - (i / (totalPoints - 1)) * 2
    const ringRadius = Math.sqrt(1 - y * y)
    const theta = i * GOLDEN_ANGLE

    const index = i * 3
    positions[index] = Math.cos(theta) * ringRadius * radius
    positions[index + 1] = y * radius
    positions[index + 2] = Math.sin(theta) * ringRadius * radius
  }

  return positions
}

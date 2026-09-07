import { describe, expect, it } from 'vitest'
import { generateFibonacciSpherePositions } from './generateFibonacciSpherePositions'

describe('generateFibonacciSpherePositions', () => {
  it('returns a flat array of length totalPoints * 3', () => {
    const positions = generateFibonacciSpherePositions(500, 1.4)

    expect(positions).toHaveLength(500 * 3)
  })

  it('places every point at the given radius from the origin (within floating-point tolerance)', () => {
    const radius = 1.4
    const positions = generateFibonacciSpherePositions(500, radius)

    for (let i = 0; i < positions.length; i += 3) {
      const x = positions[i]
      const y = positions[i + 1]
      const z = positions[i + 2]
      const distance = Math.sqrt(x * x + y * y + z * z)

      expect(distance).toBeCloseTo(radius, 5)
    }
  })

  it('never produces identical coordinates for two consecutive points (FR-001 — no ring/pole clustering)', () => {
    const positions = generateFibonacciSpherePositions(500, 1.4)

    for (let i = 0; i < positions.length - 3; i += 3) {
      const current = [positions[i], positions[i + 1], positions[i + 2]]
      const next = [positions[i + 3], positions[i + 4], positions[i + 5]]

      expect(current).not.toEqual(next)
    }
  })

  it('spreads points across the full latitude range rather than clustering at the poles', () => {
    const radius = 1
    const positions = generateFibonacciSpherePositions(1000, radius)

    let minY = Infinity
    let maxY = -Infinity
    for (let i = 1; i < positions.length; i += 3) {
      minY = Math.min(minY, positions[i])
      maxY = Math.max(maxY, positions[i])
    }

    expect(minY).toBeLessThan(-radius * 0.95)
    expect(maxY).toBeGreaterThan(radius * 0.95)
  })
})

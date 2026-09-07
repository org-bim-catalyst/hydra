import { describe, expect, it } from 'vitest'
import { computeBreathValue } from './sphereBreath'

describe('computeBreathValue', () => {
  it('stays within [-amplitude, amplitude]', () => {
    const amplitude = 0.05
    for (let t = 0; t < 20; t += 0.37) {
      const value = computeBreathValue(t, 0.6, amplitude)
      expect(value).toBeGreaterThanOrEqual(-amplitude)
      expect(value).toBeLessThanOrEqual(amplitude)
    }
  })

  it('is zero at elapsed = 0', () => {
    expect(computeBreathValue(0, 0.6, 0.05)).toBe(0)
  })

  it('is periodic with period 2*PI / frequency', () => {
    const frequency = 0.6
    const amplitude = 0.05
    const period = (2 * Math.PI) / frequency

    expect(computeBreathValue(1.3, frequency, amplitude)).toBeCloseTo(
      computeBreathValue(1.3 + period, frequency, amplitude),
      10,
    )
  })

  it('scales linearly with amplitude', () => {
    const value = computeBreathValue(1.1, 0.6, 1)
    expect(computeBreathValue(1.1, 0.6, 0.05)).toBeCloseTo(value * 0.05, 10)
  })
})

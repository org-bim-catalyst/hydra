import { render } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { describe, expect, it } from 'vitest'
import { VoiceAnalyzer } from './VoiceAnalyzer'

expect.extend(toHaveNoViolations)

describe('VoiceAnalyzer accessibility', () => {
  it('has no automatically detectable a11y violations', async () => {
    const { container } = render(<VoiceAnalyzer state="listening" getIntensity={() => 0.5} />)
    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })
})

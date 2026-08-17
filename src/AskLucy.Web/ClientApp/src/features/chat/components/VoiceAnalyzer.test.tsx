import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { VoiceAnalyzer, type VoiceAnalyzerState } from './VoiceAnalyzer'

describe('VoiceAnalyzer', () => {
  it.each<VoiceAnalyzerState>(['idle', 'processing', 'speaking', 'listening'])(
    'renders a distinguishable status for state=%s (FR-004)',
    (state) => {
      render(<VoiceAnalyzer state={state} getIntensity={() => 0.5} />)
      expect(screen.getByRole('img', { name: `Voice status: ${state}` })).toBeInTheDocument()
    },
  )

  it('does not throw when getIntensity is polled repeatedly', () => {
    const getIntensity = () => Math.random()
    expect(() =>
      render(<VoiceAnalyzer state="speaking" getIntensity={getIntensity} />),
    ).not.toThrow()
  })
})

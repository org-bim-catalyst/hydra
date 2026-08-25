import { fireEvent, render, screen } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { describe, expect, it, vi } from 'vitest'
import { RecordingReviewControls } from './RecordingReviewControls'
import { VoiceAnalyzer } from './VoiceAnalyzer'

expect.extend(toHaveNoViolations)

// specs/040-composer-interaction-bug-fixes US3 — first-ever tests for
// RecordingReviewControls, covering the cancel → middle → finish render order fix
// (Figure 3) and the constitution §7/§10 a11y merge gate (T008a).

describe('RecordingReviewControls — render order (specs/040 US3 — T008)', () => {
  it('renders nothing when phase is idle', () => {
    const { container } = render(
      <RecordingReviewControls phase="idle" onFinish={vi.fn()} onCancelRecording={vi.fn()} />,
    )
    expect(container).toBeEmptyDOMElement()
  })

  it('renders cancel immediately before finish (adjacent, no middle) — CollapsedVoiceControls usage', () => {
    render(
      <RecordingReviewControls phase="recording" onFinish={vi.fn()} onCancelRecording={vi.fn()} />,
    )
    const buttons = screen.getAllByRole('button')
    const cancelIndex = buttons.findIndex((b) =>
      /cancel recording/i.test(b.getAttribute('aria-label') ?? ''),
    )
    const finishIndex = buttons.findIndex((b) =>
      /finished speaking/i.test(b.getAttribute('aria-label') ?? ''),
    )
    expect(cancelIndex).toBeGreaterThanOrEqual(0)
    expect(finishIndex).toBe(cancelIndex + 1)
  })

  it('renders cancel → middle → finish in DOM order when middle is provided (ChatComposer tap-review usage)', () => {
    render(
      <RecordingReviewControls
        phase="recording"
        onFinish={vi.fn()}
        onCancelRecording={vi.fn()}
        middle={<div data-testid="waveform-slot">waveform</div>}
      />,
    )
    const cancelBtn = screen.getByRole('button', { name: /cancel recording/i })
    const finishBtn = screen.getByRole('button', { name: /finished speaking/i })
    const waveform = screen.getByTestId('waveform-slot')

    // DOCUMENT_POSITION_FOLLOWING (4): the argument node comes after the reference node.
    const FOLLOWING = Node.DOCUMENT_POSITION_FOLLOWING
    expect(cancelBtn.compareDocumentPosition(waveform) & FOLLOWING).toBe(FOLLOWING)
    expect(waveform.compareDocumentPosition(finishBtn) & FOLLOWING).toBe(FOLLOWING)
  })

  it('shows only cancel (no finish) when phase is transcribing — finish is gated on recording phase', () => {
    render(
      <RecordingReviewControls
        phase="transcribing"
        onFinish={vi.fn()}
        onCancelRecording={vi.fn()}
        middle={<div data-testid="waveform-slot">waveform</div>}
      />,
    )
    expect(screen.getByRole('button', { name: /cancel recording/i })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /finished speaking/i })).not.toBeInTheDocument()
  })

  it('Cancel click calls onCancelRecording', () => {
    const onCancelRecording = vi.fn()
    render(
      <RecordingReviewControls
        phase="recording"
        onFinish={vi.fn()}
        onCancelRecording={onCancelRecording}
      />,
    )
    fireEvent.click(screen.getByRole('button', { name: /cancel recording/i }))
    expect(onCancelRecording).toHaveBeenCalledTimes(1)
  })

  it('Finish click calls onFinish', () => {
    const onFinish = vi.fn()
    render(
      <RecordingReviewControls phase="recording" onFinish={onFinish} onCancelRecording={vi.fn()} />,
    )
    fireEvent.click(screen.getByRole('button', { name: /finished speaking/i }))
    expect(onFinish).toHaveBeenCalledTimes(1)
  })
})

// constitution §7/§10 — automated a11y check is required for every user-facing UI change
// with interactive controls. RecordingReviewControls is getting its first-ever test
// coverage here, and its control order changes in this feature — both render variants must
// be verified (T008a).
describe('RecordingReviewControls accessibility (specs/040 US3 — T008a)', () => {
  it('has no automatically detectable a11y violations without a middle slot (CollapsedVoiceControls variant)', async () => {
    const { container } = render(
      <RecordingReviewControls phase="recording" onFinish={vi.fn()} onCancelRecording={vi.fn()} />,
    )
    expect(await axe(container)).toHaveNoViolations()
  })

  it('has no automatically detectable a11y violations with a VoiceAnalyzer in the middle slot (ChatComposer tap-review variant)', async () => {
    const { container } = render(
      <RecordingReviewControls
        phase="recording"
        onFinish={vi.fn()}
        onCancelRecording={vi.fn()}
        middle={<VoiceAnalyzer state="listening" getIntensity={() => 0} />}
      />,
    )
    expect(await axe(container)).toHaveNoViolations()
  })
})

import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import type { SuggestedAction } from '../api/aiApi'
import { MessageBubble } from './MessageBubble'
import { SuggestedActionCard } from './SuggestedActionCard'

const actions: SuggestedAction[] = [
  {
    kind: 'capability',
    capabilityKey: 'search_knowledge_base',
    text: null,
    label: 'Search my knowledge bases',
    description: 'Look for this site in your attached documents.',
    arguments: { query: 'Al Safa Park 2' },
    isDecline: false,
  },
  {
    kind: 'followUp',
    capabilityKey: null,
    text: 'Tell me more about it',
    label: 'Tell me more',
    description: 'More detail on the park itself.',
    arguments: null,
    isDecline: false,
  },
  {
    kind: 'decline',
    capabilityKey: null,
    text: null,
    label: 'Nothing for now',
    description: '',
    arguments: null,
    isDecline: true,
  },
]

describe('SuggestedActionCard (specs/045-conversational-agent-runtime US2/US3, T078)', () => {
  it('disables the submit control until a row is selected', () => {
    render(
      <SuggestedActionCard
        question="What would you like to do next?"
        actions={actions}
        isLive
        isSubmitting={false}
        error={null}
        onSelect={vi.fn()}
      />,
    )

    expect(screen.getByRole('button', { name: /choose/i })).toBeDisabled()
  })

  it('calls onSelect with the chosen row once one is picked, and enables submit', async () => {
    const user = userEvent.setup()
    const onSelect = vi.fn().mockResolvedValue(undefined)
    render(
      <SuggestedActionCard
        question="What would you like to do next?"
        actions={actions}
        isLive
        isSubmitting={false}
        error={null}
        onSelect={onSelect}
      />,
    )

    await user.click(screen.getByRole('radio', { name: 'Tell me more' }))
    const submit = screen.getByRole('button', { name: /choose/i })
    expect(submit).toBeEnabled()

    await user.click(submit)

    expect(onSelect).toHaveBeenCalledTimes(1)
    expect(onSelect).toHaveBeenCalledWith(actions[1])
  })

  it('renders the decline row last and distinct, never pre-selected', () => {
    render(
      <SuggestedActionCard
        question="What would you like to do next?"
        actions={actions}
        isLive
        isSubmitting={false}
        error={null}
        onSelect={vi.fn()}
      />,
    )

    const radios = screen.getAllByRole('radio')
    expect(radios).toHaveLength(3)
    expect(radios[2]).toHaveAccessibleName('Nothing for now')
    radios.forEach((radio) => expect(radio).not.toBeChecked())
  })

  it('disables every row and the submit control while submitting, and sets aria-busy', () => {
    render(
      <SuggestedActionCard
        question="What would you like to do next?"
        actions={actions}
        isLive
        isSubmitting
        error={null}
        onSelect={vi.fn()}
      />,
    )

    screen.getAllByRole('radio').forEach((radio) => expect(radio).toBeDisabled())
    expect(screen.getByRole('button', { name: /working/i })).toBeDisabled()
  })

  it('renders the error inline without losing the current selection', async () => {
    const user = userEvent.setup()
    render(
      <SuggestedActionCard
        question="What would you like to do next?"
        actions={actions}
        isLive
        isSubmitting={false}
        error="That action is no longer available."
        onSelect={vi.fn()}
      />,
    )

    expect(screen.getByRole('alert')).toHaveTextContent('That action is no longer available.')

    // The card returns to its selectable state so the user can retry (contracts/suggested-actions-api.md §3).
    await user.click(screen.getByRole('radio', { name: 'Search my knowledge bases' }))
    expect(screen.getByRole('button', { name: /choose/i })).toBeEnabled()
  })

  it('renders as inert plain text — no radios, no submit — when the offer is not live', () => {
    render(
      <SuggestedActionCard
        question="What would you like to do next?"
        actions={actions}
        isLive={false}
        isSubmitting={false}
        error={null}
        onSelect={vi.fn()}
      />,
    )

    expect(screen.getByText('What would you like to do next?')).toBeInTheDocument()
    expect(screen.getByText('Search my knowledge bases')).toBeInTheDocument()
    expect(screen.getByText('Tell me more')).toBeInTheDocument()
    // The decline row is never rendered in inert history text — nothing to decline any more.
    expect(screen.queryByText('Nothing for now')).not.toBeInTheDocument()
    expect(screen.queryByRole('radio')).not.toBeInTheDocument()
    expect(screen.queryByRole('button')).not.toBeInTheDocument()
  })

  it('renders the accepted choice instead of the full menu once selectedLabel is known (2026-09-11)', () => {
    render(
      <SuggestedActionCard
        question="What would you like to do next?"
        actions={actions}
        isLive={false}
        isSubmitting={false}
        error={null}
        onSelect={vi.fn()}
        selectedLabel="Tell me more"
      />,
    )

    expect(screen.getByText('You chose: Tell me more')).toBeInTheDocument()
    expect(screen.queryByText('What would you like to do next?')).not.toBeInTheDocument()
    expect(screen.queryByText('Search my knowledge bases')).not.toBeInTheDocument()
  })

  it('renders a distinct decline message when selectedLabel is null', () => {
    render(
      <SuggestedActionCard
        question="What would you like to do next?"
        actions={actions}
        isLive={false}
        isSubmitting={false}
        error={null}
        onSelect={vi.fn()}
        selectedLabel={null}
      />,
    )

    expect(screen.getByText('Declined.')).toBeInTheDocument()
  })
})

/**
 * specs/068 US3 T067 (FR-016/FR-016a/FR-017/FR-020, SC-006a) — how wide the card actually renders.
 *
 * The reported symptom was a card squeezed into roughly half the panel with two or three words per
 * line. That was two caps multiplying: the bubble at 75% and the card at 75% of the bubble. Both
 * are asserted here from the same transcript, because the requirement is relative — an
 * offer-carrying message is wider than the ordinary reply next to it, not wide in the absolute.
 */
describe('SuggestedActionCard width (specs/068 US3)', () => {
  const offerMessage = {
    id: 'msg-offer',
    role: 'assistant' as const,
    content: 'Found Al Safa Park 2.',
    question: 'What would you like to do next?',
    suggestedActions: actions,
  }

  /** The column wrapper MessageBubble sizes: the bubble Paper's own parent. */
  const wrapperOf = (text: string) => {
    const paper = screen.getByText(text).closest('.MuiPaper-root')
    expect(paper).not.toBeNull()
    return paper!.parentElement as HTMLElement
  }

  const renderTranscript = (isLiveOffer = true) =>
    render(
      <>
        <MessageBubble
          message={{ id: 'msg-plain', role: 'assistant', content: 'An ordinary reply.' }}
        />
        <MessageBubble message={offerMessage} isLiveOffer={isLiveOffer} onSelectAction={vi.fn()} />
      </>,
    )

  it('renders an offer-carrying message at the full panel width, and its neighbour at the reply width', () => {
    renderTranscript()

    // SC-006a — both in one transcript: the widening is scoped to the message that carries the
    // offer and ends at the next message that does not (FR-017).
    expect(wrapperOf('Found Al Safa Park 2.')).toHaveStyle({ maxWidth: '100%' })
    expect(wrapperOf('An ordinary reply.')).toHaveStyle({ maxWidth: '75%' })
  })

  it('gives the card no width cap of its own', () => {
    renderTranscript()

    // FR-016a — 75% of the bubble's 75% is ~56%, which is the width that was reported. Asserting
    // the absence is the point: the card fills whatever bubble it is given.
    const card = screen.getByText('Suggested').closest('.MuiPaper-root') as HTMLElement
    expect(card).not.toHaveStyle({ maxWidth: '75%' })
  })

  it('keeps an answered or historical offer at the same width, still not interactive', () => {
    // FR-020 — a card does not shrink once it has been answered; the transcript would reflow on
    // every choice. `getByText` rather than `getByRole` throughout, per the jsdom note above.
    renderTranscript(false)

    expect(wrapperOf('Found Al Safa Park 2.')).toHaveStyle({ maxWidth: '100%' })
    expect(screen.queryByRole('radio')).not.toBeInTheDocument()
  })

  it('keeps the reply prose readable when the bubble goes full width', () => {
    renderTranscript()

    // FR-017a — the accepted trade-off is that the prose widens too; the requirement is that it
    // stays readable there, which is a measure cap on the text and not on the card below it.
    // 68ch, which jsdom resolves at its 8px-per-ch default. Asserting the resolved value keeps
    // this honest about what is actually being checked: that a cap is applied, not merely declared.
    const prose = screen.getByText('Found Al Safa Park 2.').closest('.MuiTypography-body1')
    expect(prose).toHaveStyle({ maxWidth: '544px' })
  })

  it('puts each option control, label and description on one band', () => {
    render(
      <SuggestedActionCard
        question="What would you like to do next?"
        actions={actions}
        isLive
        isSubmitting={false}
        error={null}
        onSelect={vi.fn()}
      />,
    )

    // FR-018 — the label and its description share a parent laid out as a wrapping row, which is
    // also what degrades them to a stacked layout when the panel is too narrow (FR-021). Asserted
    // structurally because jsdom computes no layout: there is no width here to measure.
    const description = screen.getByText('Look for this site in your attached documents.')
    const band = description.parentElement as HTMLElement
    expect(band).toHaveStyle({ display: 'flex', flexWrap: 'wrap' })
    expect(within(band).getByText('Search my knowledge bases')).toBeInTheDocument()
    expect(within(band).getByRole('radio', { name: 'Search my knowledge bases' })).toBeInTheDocument()
  })

  it('keeps the confirm label unwrapped', () => {
    render(
      <SuggestedActionCard
        question="What would you like to do next?"
        actions={actions}
        isLive
        isSubmitting={false}
        error={null}
        onSelect={vi.fn()}
      />,
    )

    // FR-019 — a two-line "Ch / oose" button is the failure this prevents at the narrowest width.
    expect(screen.getByText('Choose').closest('button')).toHaveStyle({ whiteSpace: 'nowrap' })
  })
})

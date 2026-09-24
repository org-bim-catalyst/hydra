import { render, screen } from '@testing-library/react'
import { MessageBubble } from './MessageBubble'
import { axe, toHaveNoViolations } from 'jest-axe'
import { describe, expect, it, vi } from 'vitest'
import type { SuggestedAction } from '../api/aiApi'
import { SuggestedActionCard } from './SuggestedActionCard'

expect.extend(toHaveNoViolations)

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
    kind: 'decline',
    capabilityKey: null,
    text: null,
    label: 'Nothing for now',
    description: '',
    arguments: null,
    isDecline: true,
  },
]

// axe's "region" rule expects all page content inside a landmark (main/nav/etc.) — correct for a
// full page, a false positive for one chat bubble rendered in isolation here (in the real app it
// already sits inside ChatPage's own landmarks). Disabled below for that reason alone; every
// other rule stays active.
const axeOptions = { rules: { region: { enabled: false } } }

describe('SuggestedActionCard accessibility (specs/045-conversational-agent-runtime, T079, constitution §10)', () => {
  it('has no automatically detectable a11y violations while live', async () => {
    const { baseElement } = render(
      <SuggestedActionCard
        question="What would you like to do next?"
        actions={actions}
        isLive
        isSubmitting={false}
        error={null}
        onSelect={vi.fn()}
      />,
    )

    expect(await axe(baseElement, axeOptions)).toHaveNoViolations()
  })

  it('exposes a radiogroup labelled by the question, with each row named by its label alone', () => {
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

    const group = screen.getByRole('radiogroup')
    expect(group).toHaveAccessibleName('What would you like to do next?')

    // The description is associated via aria-describedby, not folded into the accessible name —
    // a screen reader announces the label first, the description second.
    const row = screen.getByRole('radio', { name: 'Search my knowledge bases' })
    expect(row).toHaveAttribute('aria-describedby')
    const descriptionId = row.getAttribute('aria-describedby')!
    expect(document.getElementById(descriptionId)).toHaveTextContent(
      'Look for this site in your attached documents.',
    )
  })

  it('sets aria-busy on the card while submitting, with no violations', async () => {
    const { baseElement, getByRole } = render(
      <SuggestedActionCard
        question="What would you like to do next?"
        actions={actions}
        isLive
        isSubmitting
        error={null}
        onSelect={vi.fn()}
      />,
    )

    expect(getByRole('button', { name: /working/i }).closest('[aria-busy]')).toHaveAttribute('aria-busy', 'true')
    expect(await axe(baseElement, axeOptions)).toHaveNoViolations()
  })

  it('announces a failure via role="alert", with no violations', async () => {
    const { baseElement } = render(
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
    expect(await axe(baseElement, axeOptions)).toHaveNoViolations()
  })

  it('has no violations in inert (history) mode, with no focusable controls', async () => {
    const { baseElement } = render(
      <SuggestedActionCard
        question="What would you like to do next?"
        actions={actions}
        isLive={false}
        isSubmitting={false}
        error={null}
        onSelect={vi.fn()}
      />,
    )

    expect(screen.queryByRole('radio')).not.toBeInTheDocument()
    expect(screen.queryByRole('button')).not.toBeInTheDocument()
    expect(await axe(baseElement, axeOptions)).toHaveNoViolations()
  })
})

/**
 * specs/068 US3 T069 (FR-022/FR-025, SC-008) — the width change is presentational, and this is
 * what holds it to that. Both renderings the feature produces are checked: the card inside the
 * full-width bubble it now gets, and the card on its own.
 */
describe('SuggestedActionCard accessibility at its rendered widths (specs/068 US3)', () => {
  const message = {
    id: 'msg-offer',
    role: 'assistant' as const,
    content: 'Found Al Safa Park 2.',
    question: 'What would you like to do next?',
    suggestedActions: actions,
  }

  it('has no violations inside the full-width bubble it now renders in', async () => {
    const { baseElement } = render(
      <MessageBubble message={message} isLiveOffer onSelectAction={vi.fn()} />,
    )

    expect(await axe(baseElement, axeOptions)).toHaveNoViolations()
  })

  it('keeps every option description reachable, even though none of it is spoken', async () => {
    // FR-025 — suppressing the spoken recital takes nothing away from assistive technology: each
    // radio still names its own option and points at its own description.
    render(<MessageBubble message={message} isLiveOffer onSelectAction={vi.fn()} />)

    const radio = screen.getByRole('radio', { name: 'Search my knowledge bases' })
    const describedBy = radio.getAttribute('aria-describedby')
    expect(describedBy).toBeTruthy()
    expect(document.getElementById(describedBy!)).toHaveTextContent(
      'Look for this site in your attached documents.',
    )
  })

  it('has no violations once the offer is answered and rendered inert', async () => {
    const { baseElement } = render(
      <MessageBubble message={message} isLiveOffer={false} onSelectAction={vi.fn()} />,
    )

    expect(await axe(baseElement, axeOptions)).toHaveNoViolations()
  })
})

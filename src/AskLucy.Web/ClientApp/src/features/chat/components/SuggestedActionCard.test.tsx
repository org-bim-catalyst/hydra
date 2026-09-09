import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import type { SuggestedAction } from '../api/aiApi'
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
})

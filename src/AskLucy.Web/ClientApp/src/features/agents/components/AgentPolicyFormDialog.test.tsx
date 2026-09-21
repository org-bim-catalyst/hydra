import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { AgentPolicyFormDialog } from './AgentPolicyFormDialog'

function renderDialog(overrides: Partial<React.ComponentProps<typeof AgentPolicyFormDialog>> = {}) {
  const onSubmit = vi.fn()
  const onClose = vi.fn()
  render(
    <AgentPolicyFormDialog open isSaving={false} errorMessage={null} onClose={onClose} onSubmit={onSubmit} {...overrides} />,
  )
  return { onSubmit, onClose }
}

describe('AgentPolicyFormDialog', () => {
  it('disables Create Policy until a name and a tool name are given', () => {
    renderDialog()

    expect(screen.getByText('Create Policy')).toBeDisabled()

    fireEvent.change(screen.getByLabelText(/^Name/), { target: { value: 'Read-only fake tool' } })
    expect(screen.getByText('Create Policy')).toBeDisabled()

    fireEvent.change(screen.getByLabelText(/^Tool Name/), { target: { value: 'FakeHighRiskTool' } })
    expect(screen.getByText('Create Policy')).not.toBeDisabled()
  })

  it('submits the entered fields', () => {
    const { onSubmit } = renderDialog()

    fireEvent.change(screen.getByLabelText(/^Name/), { target: { value: 'Read-only fake tool' } })
    fireEvent.change(screen.getByLabelText(/^Tool Name/), { target: { value: 'FakeHighRiskTool' } })
    fireEvent.change(screen.getByLabelText('Description'), { target: { value: 'Read-only calls only' } })
    fireEvent.click(screen.getByText('Create Policy'))

    expect(onSubmit).toHaveBeenCalledWith({
      name: 'Read-only fake tool',
      description: 'Read-only calls only',
      toolName: 'FakeHighRiskTool',
      conditionsJson: null,
    })
  })

  it('shows the error message when the save fails', () => {
    renderDialog({ errorMessage: 'Could not create the policy. Please try again.' })

    expect(screen.getByText('Could not create the policy. Please try again.')).toBeInTheDocument()
  })

  it('calls onClose from the Cancel button', () => {
    const { onClose } = renderDialog()

    fireEvent.click(screen.getByText('Cancel'))

    expect(onClose).toHaveBeenCalled()
  })
})

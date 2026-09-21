import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { WorkflowPolicyFormDialog } from './WorkflowPolicyFormDialog'

function renderDialog(overrides: Partial<React.ComponentProps<typeof WorkflowPolicyFormDialog>> = {}) {
  const onSubmit = vi.fn()
  const onClose = vi.fn()
  render(
    <WorkflowPolicyFormDialog
      open
      isSaving={false}
      errorMessage={null}
      onClose={onClose}
      onSubmit={onSubmit}
      {...overrides}
    />,
  )
  return { onSubmit, onClose }
}

describe('WorkflowPolicyFormDialog', () => {
  it('disables Create Policy until a name and either a node type or a tool name are given', () => {
    renderDialog()

    expect(screen.getByText('Create Policy')).toBeDisabled()

    fireEvent.change(screen.getByLabelText(/^Name/), { target: { value: 'Public knowledge search' } })
    expect(screen.getByText('Create Policy')).toBeDisabled()

    fireEvent.change(screen.getByLabelText(/Underlying Tool Name/), { target: { value: 'KnowledgeSearchTool' } })
    expect(screen.getByText('Create Policy')).not.toBeDisabled()
  })

  it('submits the entered fields', () => {
    const { onSubmit } = renderDialog()

    fireEvent.change(screen.getByLabelText(/^Name/), { target: { value: 'Public knowledge search' } })
    fireEvent.change(screen.getByLabelText(/Underlying Tool Name/), { target: { value: 'KnowledgeSearchTool' } })
    fireEvent.change(screen.getByLabelText('Description'), { target: { value: 'Auto-approve public lookups' } })
    fireEvent.click(screen.getByText('Create Policy'))

    expect(onSubmit).toHaveBeenCalledWith({
      name: 'Public knowledge search',
      description: 'Auto-approve public lookups',
      workflowNodeType: null,
      underlyingToolName: 'KnowledgeSearchTool',
      conditionsJson: null,
    })
  })

  it('lets the node type be chosen instead of the tool name', async () => {
    const { onSubmit } = renderDialog()

    fireEvent.change(screen.getByLabelText(/^Name/), { target: { value: 'RAG search nodes' } })
    fireEvent.mouseDown(screen.getByLabelText('Node Type (optional)'))
    // MUI opens the menu through a Popover transition, and jsdom's font-size resolver throws
    // "object null is not iterable" for role-based queries against the portalled menu — a jsdom
    // bug, not a problem with the component.
    await waitFor(() => expect(document.querySelector('ul[role="listbox"]')).not.toBeNull())
    fireEvent.click(within(document.querySelector('ul[role="listbox"]') as HTMLElement).getByText('RagSearch'))
    fireEvent.click(screen.getByText('Create Policy'))

    expect(onSubmit).toHaveBeenCalledWith(expect.objectContaining({ workflowNodeType: 'RagSearch', underlyingToolName: null }))
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

import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type * as workflowPoliciesApiModule from '../api/workflowPoliciesApi'
import type { WorkflowPolicy } from '../api/workflowPoliciesApi'
import * as workflowPoliciesApi from '../api/workflowPoliciesApi'
import { WorkflowPolicyAdminPanel } from './WorkflowPolicyAdminPanel'

vi.mock('../api/workflowPoliciesApi', async () => {
  const actual = await vi.importActual<typeof workflowPoliciesApiModule>('../api/workflowPoliciesApi')
  return {
    ...actual,
    listWorkflowPolicies: vi.fn(),
    createWorkflowPolicy: vi.fn(),
    updateWorkflowPolicy: vi.fn(),
    deleteWorkflowPolicy: vi.fn(),
  }
})

function makePolicy(overrides: Partial<WorkflowPolicy>): WorkflowPolicy {
  return {
    id: 'policy-1',
    name: 'Public knowledge search',
    description: null,
    workflowNodeType: 'RagSearch',
    underlyingToolName: null,
    conditionsJson: null,
    createdByUserId: 'user-1',
    isEnabled: true,
    createdAtUtc: '2026-07-20T00:00:00Z',
    modifiedAtUtc: null,
    ...overrides,
  }
}

function renderPanel(policies: WorkflowPolicy[] = []) {
  vi.mocked(workflowPoliciesApi.listWorkflowPolicies).mockResolvedValue(policies)
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={queryClient}>
      <WorkflowPolicyAdminPanel />
    </QueryClientProvider>,
  )
}

beforeEach(() => vi.clearAllMocks())

describe('WorkflowPolicyAdminPanel', () => {
  it('renders existing policies in the table', async () => {
    renderPanel([makePolicy({})])

    expect(await screen.findByText('Public knowledge search')).toBeInTheDocument()
  })

  it('has no inline "New Policy" form — creation happens through a modal opened by a button', async () => {
    renderPanel([])

    await screen.findByText('No policies configured yet.')

    expect(screen.queryByText('New Policy')).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'New policy' })).toBeInTheDocument()
  })

  it('creates a policy through the modal and closes it on success', async () => {
    vi.mocked(workflowPoliciesApi.createWorkflowPolicy).mockResolvedValue(makePolicy({}))
    renderPanel([])
    await screen.findByText('No policies configured yet.')

    fireEvent.click(screen.getByRole('button', { name: 'New policy' }))
    fireEvent.change(screen.getByLabelText(/^Name/), { target: { value: 'Public knowledge search' } })
    fireEvent.change(screen.getByLabelText(/Underlying Tool Name/), { target: { value: 'KnowledgeSearchTool' } })
    fireEvent.click(screen.getByText('Create Policy'))

    await waitFor(() => expect(workflowPoliciesApi.createWorkflowPolicy).toHaveBeenCalled())
    await waitFor(() => expect(screen.queryByLabelText('Name')).not.toBeInTheDocument())
  })

  it('surfaces a failed creation to the user rather than only the console', async () => {
    vi.mocked(workflowPoliciesApi.createWorkflowPolicy).mockRejectedValue(new Error('boom'))
    renderPanel([])
    await screen.findByText('No policies configured yet.')

    fireEvent.click(screen.getByRole('button', { name: 'New policy' }))
    fireEvent.change(screen.getByLabelText(/^Name/), { target: { value: 'Public knowledge search' } })
    fireEvent.change(screen.getByLabelText(/Underlying Tool Name/), { target: { value: 'KnowledgeSearchTool' } })
    fireEvent.click(screen.getByText('Create Policy'))

    expect(await screen.findAllByText('boom')).not.toHaveLength(0)
  })

  it('toggles a policy enabled state', async () => {
    vi.mocked(workflowPoliciesApi.updateWorkflowPolicy).mockResolvedValue(makePolicy({}))
    renderPanel([makePolicy({})])
    await screen.findByText('Public knowledge search')

    fireEvent.click(screen.getByRole('switch'))

    await waitFor(() => expect(workflowPoliciesApi.updateWorkflowPolicy).toHaveBeenCalledWith('policy-1', expect.objectContaining({ isEnabled: false })))
  })

  it('deletes a policy', async () => {
    vi.mocked(workflowPoliciesApi.deleteWorkflowPolicy).mockResolvedValue(undefined)
    renderPanel([makePolicy({})])
    await screen.findByText('Public knowledge search')

    fireEvent.click(screen.getByLabelText('Delete Public knowledge search'))

    await waitFor(() => expect(workflowPoliciesApi.deleteWorkflowPolicy).toHaveBeenCalledWith('policy-1'))
  })
})

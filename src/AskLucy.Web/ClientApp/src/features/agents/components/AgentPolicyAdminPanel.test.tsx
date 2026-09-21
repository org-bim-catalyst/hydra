import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type * as agentPoliciesApiModule from '../api/agentPoliciesApi'
import type { AgentPolicy } from '../api/agentPoliciesApi'
import * as agentPoliciesApi from '../api/agentPoliciesApi'
import { AgentPolicyAdminPanel } from './AgentPolicyAdminPanel'

vi.mock('../api/agentPoliciesApi', async () => {
  const actual = await vi.importActual<typeof agentPoliciesApiModule>('../api/agentPoliciesApi')
  return {
    ...actual,
    listAgentPolicies: vi.fn(),
    createAgentPolicy: vi.fn(),
    updateAgentPolicy: vi.fn(),
    deleteAgentPolicy: vi.fn(),
  }
})

function makePolicy(overrides: Partial<AgentPolicy>): AgentPolicy {
  return {
    id: 'policy-1',
    name: 'Read-only fake tool',
    description: null,
    toolName: 'FakeHighRiskTool',
    conditionsJson: null,
    createdByUserId: 'user-1',
    isEnabled: true,
    createdAtUtc: '2026-07-20T00:00:00Z',
    modifiedAtUtc: null,
    ...overrides,
  }
}

function renderPanel(policies: AgentPolicy[] = []) {
  vi.mocked(agentPoliciesApi.listAgentPolicies).mockResolvedValue(policies)
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={queryClient}>
      <AgentPolicyAdminPanel />
    </QueryClientProvider>,
  )
}

beforeEach(() => vi.clearAllMocks())

describe('AgentPolicyAdminPanel', () => {
  it('renders existing policies in the table', async () => {
    renderPanel([makePolicy({})])

    expect(await screen.findByText('Read-only fake tool')).toBeInTheDocument()
  })

  it('has no inline "New Policy" form — creation happens through a modal opened by a button', async () => {
    renderPanel([])

    await screen.findByText('No policies configured yet.')

    expect(screen.queryByText('New Policy')).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'New policy' })).toBeInTheDocument()
  })

  it('creates a policy through the modal and closes it on success', async () => {
    vi.mocked(agentPoliciesApi.createAgentPolicy).mockResolvedValue(makePolicy({}))
    renderPanel([])
    await screen.findByText('No policies configured yet.')

    fireEvent.click(screen.getByRole('button', { name: 'New policy' }))
    fireEvent.change(screen.getByLabelText(/^Name/), { target: { value: 'Read-only fake tool' } })
    fireEvent.change(screen.getByLabelText(/^Tool Name/), { target: { value: 'FakeHighRiskTool' } })
    fireEvent.click(screen.getByText('Create Policy'))

    await waitFor(() => expect(agentPoliciesApi.createAgentPolicy).toHaveBeenCalled())
    await waitFor(() => expect(screen.queryByLabelText('Name')).not.toBeInTheDocument())
  })

  it('surfaces a failed creation to the user rather than only the console', async () => {
    vi.mocked(agentPoliciesApi.createAgentPolicy).mockRejectedValue(new Error('boom'))
    renderPanel([])
    await screen.findByText('No policies configured yet.')

    fireEvent.click(screen.getByRole('button', { name: 'New policy' }))
    fireEvent.change(screen.getByLabelText(/^Name/), { target: { value: 'Read-only fake tool' } })
    fireEvent.change(screen.getByLabelText(/^Tool Name/), { target: { value: 'FakeHighRiskTool' } })
    fireEvent.click(screen.getByText('Create Policy'))

    expect(await screen.findAllByText('boom')).not.toHaveLength(0)
  })

  it('toggles a policy enabled state', async () => {
    vi.mocked(agentPoliciesApi.updateAgentPolicy).mockResolvedValue(makePolicy({}))
    renderPanel([makePolicy({})])
    await screen.findByText('Read-only fake tool')

    fireEvent.click(screen.getByRole('switch'))

    await waitFor(() => expect(agentPoliciesApi.updateAgentPolicy).toHaveBeenCalledWith('policy-1', expect.objectContaining({ isEnabled: false })))
  })

  it('deletes a policy', async () => {
    vi.mocked(agentPoliciesApi.deleteAgentPolicy).mockResolvedValue(undefined)
    renderPanel([makePolicy({})])
    await screen.findByText('Read-only fake tool')

    fireEvent.click(screen.getByLabelText('Delete Read-only fake tool'))

    await waitFor(() => expect(agentPoliciesApi.deleteAgentPolicy).toHaveBeenCalledWith('policy-1'))
  })
})

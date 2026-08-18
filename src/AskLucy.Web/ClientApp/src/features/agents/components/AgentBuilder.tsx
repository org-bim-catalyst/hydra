import { Alert, Box, Button, MenuItem, Paper, Snackbar, Stack, TextField, Typography } from '@mui/material'
import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { useNavigate } from 'react-router'
import { useQuery } from '@tanstack/react-query'
import { getAllModels, getEnabledProviders } from '../../chat/api/aiProvidersApi'
import type { AgentDetail, AgentOutputFormat, AgentType, SaveAgentInput } from '../api/agentsApi'
import { EMPTY_EXECUTION_POLICY } from '../api/agentsApi'
import { useCreateAgent, useUpdateAgent } from '../hooks/useAgentMutations'

const AGENT_TYPES: AgentType[] = ['Conversational', 'Research', 'Document', 'Knowledge', 'Task']
const OUTPUT_FORMATS: AgentOutputFormat[] = ['PlainText', 'Markdown', 'Json', 'StructuredOutput', 'Files']

interface AgentFormValues {
  name: string
  description: string
  agentType: AgentType
  systemInstructions: string
  objectives: string
  constraints: string
  behavioralRules: string
  outputRequirements: string
  toolUsageRules: string
  safetyRules: string
  modelProviderId: string
  modelId: string
  outputFormat: AgentOutputFormat
}

interface AgentBuilderProps {
  /** Present when editing an existing agent's draft; absent when creating a new one (spec.md User Story 1). */
  agent?: AgentDetail
}

/**
 * Agent identity/instructions/model/output-format editor (spec.md FR-001-FR-006). Tool/Knowledge
 * Base/memory-policy configuration is added in User Story 2 — this covers exactly the fields
 * User Story 1's Independent Test exercises ("an agent with only instructions and a model").
 */
export function AgentBuilder({ agent }: AgentBuilderProps) {
  const navigate = useNavigate()
  const isEditing = agent !== undefined
  const createAgent = useCreateAgent()
  const updateAgent = useUpdateAgent()
  const [errorMessage, setErrorMessage] = useState<string | null>(null)

  const { data: providers } = useQuery({ queryKey: ['ai-providers'], queryFn: getEnabledProviders })
  const { data: models } = useQuery({ queryKey: ['ai-models'], queryFn: getAllModels })

  const { register, handleSubmit, watch } = useForm<AgentFormValues>({
    values: {
      name: agent?.name ?? '',
      description: agent?.description ?? '',
      agentType: agent?.agentType ?? 'Task',
      systemInstructions: agent?.instructions.systemInstructions ?? '',
      objectives: agent?.instructions.objectives ?? '',
      constraints: agent?.instructions.constraints ?? '',
      behavioralRules: agent?.instructions.behavioralRules ?? '',
      outputRequirements: agent?.instructions.outputRequirements ?? '',
      toolUsageRules: agent?.instructions.toolUsageRules ?? '',
      safetyRules: agent?.instructions.safetyRules ?? '',
      modelProviderId: agent?.modelProviderId ?? '',
      modelId: agent?.modelId ?? '',
      outputFormat: agent?.outputFormat ?? 'PlainText',
    },
  })

  const selectedProviderId = watch('modelProviderId')
  const modelsForProvider = (models ?? []).filter((m) => m.providerId === selectedProviderId)
  const submitting = createAgent.isPending || updateAgent.isPending

  const onSubmit = handleSubmit((values) => {
    const input: SaveAgentInput = {
      name: values.name,
      description: values.description || null,
      agentType: values.agentType,
      instructions: {
        systemInstructions: values.systemInstructions || null,
        objectives: values.objectives || null,
        constraints: values.constraints || null,
        behavioralRules: values.behavioralRules || null,
        outputRequirements: values.outputRequirements || null,
        toolUsageRules: values.toolUsageRules || null,
        safetyRules: values.safetyRules || null,
      },
      modelProviderId: values.modelProviderId || null,
      modelId: values.modelId || null,
      outputFormat: values.outputFormat,
      executionPolicy: agent?.executionPolicy ?? EMPTY_EXECUTION_POLICY,
    }

    const onError = (err: unknown) =>
      setErrorMessage(err instanceof Error ? err.message : 'Could not save the agent. Please try again.')

    if (isEditing) {
      updateAgent.mutate({ id: agent.id, input }, { onSuccess: () => navigate(`/agents/${agent.id}`), onError })
    } else {
      createAgent.mutate(input, { onSuccess: (created) => navigate(`/agents/${created.id}`), onError })
    }
  })

  return (
    <Box component="form" onSubmit={onSubmit} sx={{ maxWidth: 900, mx: 'auto', p: 3 }}>
      <Typography variant="h5" sx={{ mb: 3 }}>
        {isEditing ? 'Edit Agent' : 'New Agent'}
      </Typography>

      <Paper sx={{ p: 3, mb: 3 }}>
        <Stack spacing={2}>
          <TextField label="Name" required {...register('name', { required: true })} />
          <TextField label="Description" multiline minRows={2} {...register('description')} />
          <TextField label="Agent Type" select {...register('agentType')}>
            {AGENT_TYPES.map((type) => (
              <MenuItem key={type} value={type}>
                {type}
              </MenuItem>
            ))}
          </TextField>
        </Stack>
      </Paper>

      <Paper sx={{ p: 3, mb: 3 }}>
        <Typography variant="subtitle1" sx={{ mb: 2 }}>
          Instructions
        </Typography>
        <Stack spacing={2}>
          <TextField label="System Instructions" required multiline minRows={3} {...register('systemInstructions', { required: true })} />
          <TextField label="Objectives" multiline minRows={2} {...register('objectives')} />
          <TextField label="Constraints" multiline minRows={2} {...register('constraints')} />
          <TextField label="Behavioral Rules" multiline minRows={2} {...register('behavioralRules')} />
          <TextField label="Output Requirements" multiline minRows={2} {...register('outputRequirements')} />
          <TextField label="Tool Usage Rules" multiline minRows={2} {...register('toolUsageRules')} />
          <TextField label="Safety Rules" multiline minRows={2} {...register('safetyRules')} />
        </Stack>
      </Paper>

      <Paper sx={{ p: 3, mb: 3 }}>
        <Typography variant="subtitle1" sx={{ mb: 2 }}>
          Model &amp; Output
        </Typography>
        <Stack spacing={2}>
          <TextField label="AI Provider" required select {...register('modelProviderId', { required: true })}>
            {(providers ?? []).map((p) => (
              <MenuItem key={p.id} value={p.id}>
                {p.displayName}
              </MenuItem>
            ))}
          </TextField>
          <TextField label="Model" required select disabled={!selectedProviderId} {...register('modelId', { required: true })}>
            {modelsForProvider.map((m) => (
              <MenuItem key={m.id} value={m.id}>
                {m.displayName}
              </MenuItem>
            ))}
          </TextField>
          <TextField label="Output Format" select {...register('outputFormat')}>
            {OUTPUT_FORMATS.map((format) => (
              <MenuItem key={format} value={format}>
                {format}
              </MenuItem>
            ))}
          </TextField>
        </Stack>
      </Paper>

      <Button type="submit" variant="contained" disabled={submitting}>
        {isEditing ? 'Save Changes' : 'Create Agent'}
      </Button>

      <Snackbar open={errorMessage !== null} autoHideDuration={6000} onClose={() => setErrorMessage(null)}>
        <Alert severity="error" onClose={() => setErrorMessage(null)}>
          {errorMessage}
        </Alert>
      </Snackbar>
    </Box>
  )
}

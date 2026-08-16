import { Alert, MenuItem, Stack, TextField, Typography } from '@mui/material'
import { useMemo } from 'react'
import { useSearchKnowledgeBases } from '../../knowledge-base/hooks/useSearchKnowledgeBases'

type EventType = 'DocumentUploaded' | 'DocumentProcessed' | 'KnowledgeBaseUpdated'

interface EventTriggerConfig {
  eventType: EventType | ''
  knowledgeBaseId: string | null
}

interface EventTriggerConfigPanelProps {
  eventTriggerConfigurationJson: string | null
  onChange: (eventTriggerConfigurationJson: string) => void
}

const EVENT_TYPE_LABELS: Record<EventType, string> = {
  DocumentUploaded: 'Document uploaded',
  DocumentProcessed: 'Document processed',
  KnowledgeBaseUpdated: 'Knowledge base updated',
}

function parseConfig(json: string | null): EventTriggerConfig {
  if (!json) return { eventType: '', knowledgeBaseId: null }
  try {
    const parsed = JSON.parse(json) as Partial<EventTriggerConfig>
    return { eventType: parsed.eventType ?? '', knowledgeBaseId: parsed.knowledgeBaseId ?? null }
  } catch {
    return { eventType: '', knowledgeBaseId: null }
  }
}

/**
 * spec.md User Story 9 (FR-063/FR-064) — configures which application event starts this workflow
 * and its scope (a specific knowledge base, or any). Only meaningful on an Event-Driven workflow;
 * the caller is responsible for only rendering this when `workflow.workflowType === 'EventDriven'`.
 */
export function EventTriggerConfigPanel({ eventTriggerConfigurationJson, onChange }: EventTriggerConfigPanelProps) {
  const config = useMemo(() => parseConfig(eventTriggerConfigurationJson), [eventTriggerConfigurationJson])
  const { data } = useSearchKnowledgeBases({ pageSize: 100 })
  const knowledgeBases = useMemo(() => data?.pages.flatMap((page) => page.items) ?? [], [data])

  const supportsKnowledgeBaseScope = config.eventType === 'DocumentUploaded' || config.eventType === 'KnowledgeBaseUpdated'

  const emit = (next: EventTriggerConfig) => onChange(JSON.stringify({ eventType: next.eventType || null, knowledgeBaseId: next.knowledgeBaseId }))

  return (
    <Stack spacing={2} data-testid="event-trigger-config-panel">
      <Typography variant="subtitle2">Event Trigger</Typography>
      <TextField
        select
        label="Starts when"
        fullWidth
        value={config.eventType}
        onChange={(e) => emit({ eventType: e.target.value as EventType, knowledgeBaseId: config.knowledgeBaseId })}
      >
        <MenuItem value="">Not configured</MenuItem>
        {(Object.entries(EVENT_TYPE_LABELS) as [EventType, string][]).map(([value, label]) => (
          <MenuItem key={value} value={value}>
            {label}
          </MenuItem>
        ))}
      </TextField>

      {supportsKnowledgeBaseScope && (
        <TextField
          select
          label="Knowledge base"
          fullWidth
          value={config.knowledgeBaseId ?? ''}
          onChange={(e) => emit({ eventType: config.eventType, knowledgeBaseId: e.target.value || null })}
        >
          <MenuItem value="">Any knowledge base</MenuItem>
          {knowledgeBases.map((kb) => (
            <MenuItem key={kb.id} value={kb.id}>
              {kb.name}
            </MenuItem>
          ))}
        </TextField>
      )}

      {config.eventType === '' && (
        <Alert severity="info">This workflow will never start automatically until an event and scope are configured.</Alert>
      )}
    </Stack>
  )
}

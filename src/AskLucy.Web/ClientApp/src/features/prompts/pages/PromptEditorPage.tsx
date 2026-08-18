import { Box, Tab, Tabs } from '@mui/material'
import { useState } from 'react'
import { useParams } from 'react-router'
import { AppShell } from '../../../components/AppShell'
import { PromptEditor } from '../components/PromptEditor'
import { PromptTestingConsole } from '../components/PromptTestingConsole'
import { TestCaseList } from '../components/TestCaseList'
import { VersionHistory } from '../components/VersionHistory'
import { usePrompt } from '../hooks/usePrompts'

/**
 * Create (`/prompts/new`) or edit/test (`/prompts/:id`) a prompt — the same `PromptEditor`
 * handles create/edit (spec.md User Story 1); once a prompt exists, "Test" (User Story 2) and
 * "Test cases" tabs become available alongside it.
 */
export function PromptEditorPage() {
  const { id } = useParams<{ id: string }>()
  const isCreating = id === undefined || id === 'new'
  const { data: prompt, isLoading } = usePrompt(isCreating ? null : (id ?? null))
  const [tab, setTab] = useState(0)

  if (!isCreating && isLoading) {
    return (
      <AppShell title="Loading…">
        <div />
      </AppShell>
    )
  }

  return (
    <AppShell title={isCreating ? 'New Prompt' : (prompt?.name ?? 'Prompt')}>
      {isCreating ? (
        <PromptEditor />
      ) : (
        <Box sx={{ maxWidth: 1100, mx: 'auto', p: 3 }}>
          <Tabs value={tab} onChange={(_, value) => setTab(value)} sx={{ mb: 3 }}>
            <Tab label="Edit" />
            <Tab label="Test" />
            <Tab label="Test cases" />
            <Tab label="Versions" />
          </Tabs>

          {tab === 0 && prompt && <PromptEditor prompt={prompt} />}
          {tab === 1 && prompt && <PromptTestingConsole prompt={prompt} />}
          {tab === 2 && prompt && <TestCaseList promptId={prompt.id} />}
          {tab === 3 && prompt && <VersionHistory promptId={prompt.id} />}
        </Box>
      )}
    </AppShell>
  )
}

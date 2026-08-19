import { useState } from 'react'
import { Divider, Stack } from '@mui/material'
import { AppShell } from '../../../components/AppShell'
import { McpToolPicker } from '../components/McpToolPicker'
import { McpResourcesAndPromptsPanel } from '../components/McpResourcesAndPromptsPanel'

/** spec.md User Stories 4-5 — browse and enable MCP tools, resources, and prompts available to the current user. */
export function McpCatalogPage() {
  const [selectedToolNames, setSelectedToolNames] = useState<string[]>([])

  return (
    <AppShell title="MCP catalog" subtitle="Tools, resources, and prompts exposed by connected Model Context Protocol servers">
      <Stack spacing={3}>
        <McpToolPicker selectedToolNames={selectedToolNames} onChange={setSelectedToolNames} />
        <Divider />
        <McpResourcesAndPromptsPanel />
      </Stack>
    </AppShell>
  )
}

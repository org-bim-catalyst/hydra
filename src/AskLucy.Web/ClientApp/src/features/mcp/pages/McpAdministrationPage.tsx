import { useState } from 'react'
import { Divider, Stack, Typography } from '@mui/material'
import { AppShell } from '../../../components/AppShell'
import { McpServerList } from '../components/McpServerList'
import { McpHealthBadge } from '../components/McpHealthBadge'
import { McpToolActivationPanel } from '../components/McpToolActivationPanel'
import { McpAuditLogTable } from '../components/McpAuditLogTable'
import { useMcpServer, useMcpServerHealth } from '../hooks/useMcpServers'

/** spec.md User Story 1 — MCP Administration workspace (register/enable/test/discover/activate tools/audit). */
export function McpAdministrationPage() {
  const [selectedServerId, setSelectedServerId] = useState<string | null>(null)
  const { data: selectedServer } = useMcpServer(selectedServerId)
  const { data: health } = useMcpServerHealth(selectedServerId)

  return (
    <AppShell title="MCP servers" subtitle="Register, monitor, and review tools exposed by Model Context Protocol servers">
      <McpServerList selectedServerId={selectedServerId} onSelectServer={setSelectedServerId} />

      {selectedServerId && (
        <>
          <Divider sx={{ my: 3 }} />
          <Stack direction="row" spacing={2} sx={{ alignItems: 'center', mb: 2 }}>
            <Typography variant="h6">{selectedServer?.name ?? 'Selected server'}</Typography>
            <McpHealthBadge health={health} />
          </Stack>
          <Stack spacing={3}>
            <McpToolActivationPanel serverId={selectedServerId} />
            <McpAuditLogTable serverId={selectedServerId} />
          </Stack>
        </>
      )}
    </AppShell>
  )
}

import { useState } from 'react'
import { Box, Divider, Stack, Typography } from '@mui/material'
import { AdminShell } from '../../admin/components/AdminShell'
import { useOuterT } from '../../admin/hooks/useOuterT'
import { McpServerList } from '../components/McpServerList'
import { McpHealthBadge } from '../components/McpHealthBadge'
import { McpToolActivationPanel } from '../components/McpToolActivationPanel'
import { McpAuditLogTable } from '../components/McpAuditLogTable'
import { useMcpServer, useMcpServerHealth } from '../hooks/useMcpServers'

/** spec.md User Story 1 — MCP Administration workspace (register/enable/test/discover/activate tools/audit). */
export function McpAdministrationPage() {
  // The shell takes its title as a prop, so it is resolved here, outside the shell's own language surface.
  const t = useOuterT('admin.mcpServers')
  const [selectedServerId, setSelectedServerId] = useState<string | null>(null)
  const { data: selectedServer } = useMcpServer(selectedServerId)
  const { data: health } = useMcpServerHealth(selectedServerId)

  return (
    <AdminShell title={t('page.title')} subtitle={t('page.subtitle')}>
      <Box sx={{ flex: 1, minHeight: 0, overflow: 'auto', display: 'flex', flexDirection: 'column' }}>
        <McpServerList
          selectedServerId={selectedServerId}
          onSelectServer={setSelectedServerId}
          fillHeight={selectedServerId === null}
        />

        {selectedServerId && (
          <>
            <Divider sx={{ my: 3 }} />
            <Stack direction="row" spacing={2} sx={{ alignItems: 'center', mb: 2 }}>
              <Typography variant="h6">{selectedServer?.name ?? t('page.selectedServer')}</Typography>
              <McpHealthBadge health={health} />
            </Stack>
            <Stack spacing={3}>
              <McpToolActivationPanel serverId={selectedServerId} />
              <McpAuditLogTable serverId={selectedServerId} />
            </Stack>
          </>
        )}
      </Box>
    </AdminShell>
  )
}

import { useEffect, useState } from 'react'
import type { ReactNode } from 'react'
import { Box, Paper, Tab, Tabs } from '@mui/material'
import { useLocation } from 'react-router'
import { AppShell } from '../../../components/AppShell'
import { CHAT_SETTINGS_TAB_INDEX } from '../chatSettingsTabs'
import { ChatConfigurationTab } from './ChatConfigurationTab'
import { ChatHistoryTab } from './ChatHistoryTab'
import { VoiceTab } from './SettingsPage'

function TabPanel({ value, index, children }: { value: number; index: number; children: ReactNode }) {
  if (value !== index) return null
  return <Box sx={{ pt: 3 }}>{children}</Box>
}

/**
 * Everything about how a conversation behaves, on one page: how Lucy speaks and listens, which
 * model and knowledge a conversation uses, and the conversations themselves. These were three
 * separate tabs inside general Settings, sitting beside password changes and cookie preferences
 * — related to each other and to nothing around them.
 */
export function ChatSettingsPage() {
  const location = useLocation()
  const [tab, setTab] = useState<number>(
    () => (location.state as { tab?: number } | null)?.tab ?? CHAT_SETTINGS_TAB_INDEX.Voice,
  )

  useEffect(() => {
    const requestedTab = (location.state as { tab?: number } | null)?.tab
    if (requestedTab !== undefined) {
      // Same reasoning as SettingsPage: this reacts to location.key, an external navigation
      // event rather than a value derived from render, and the update is deferred so a
      // same-pathname navigation switches tabs without a cascading render.
      queueMicrotask(() => setTab(requestedTab))
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [location.key])

  return (
    <AppShell title="Chat settings">
      <Paper elevation={1} sx={{ maxWidth: 720 }}>
        <Tabs
          value={tab}
          onChange={(_, value: number) => setTab(value)}
          sx={{ px: 2, borderBottom: 1, borderColor: 'divider' }}
        >
          <Tab label="Voice" value={CHAT_SETTINGS_TAB_INDEX.Voice} />
          <Tab label="Chat Configuration" value={CHAT_SETTINGS_TAB_INDEX.ChatConfiguration} />
          <Tab label="Chat History" value={CHAT_SETTINGS_TAB_INDEX.ChatHistory} />
        </Tabs>
        <Box sx={{ p: 3 }}>
          <TabPanel value={tab} index={CHAT_SETTINGS_TAB_INDEX.Voice}>
            <VoiceTab />
          </TabPanel>
          <TabPanel value={tab} index={CHAT_SETTINGS_TAB_INDEX.ChatConfiguration}>
            <ChatConfigurationTab />
          </TabPanel>
          <TabPanel value={tab} index={CHAT_SETTINGS_TAB_INDEX.ChatHistory}>
            <ChatHistoryTab />
          </TabPanel>
        </Box>
      </Paper>
    </AppShell>
  )
}

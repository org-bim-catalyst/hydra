import { useEffect, useState } from 'react'
import type { ReactNode } from 'react'
import { Box, Paper, Tab, Tabs } from '@mui/material'
import { useLocation } from 'react-router'
import { AppShell } from '../../../components/AppShell'
import { CHAT_SETTINGS_TAB_INDEX } from '../chatSettingsTabs'
import { ChatConfigurationTab } from './ChatConfigurationTab'
import { ChatHistoryTab } from './ChatHistoryTab'
import { TabContentContainer, VoiceTab } from './SettingsPage'
import { ViewerTab } from './ViewerTab'

function TabPanel({ value, index, children }: { value: number; index: number; children: ReactNode }) {
  if (value !== index) return null
  return <Box sx={{ pt: 3 }}>{children}</Box>
}

/**
 * Everything about how a conversation behaves, on one page: how Lucy speaks and listens, which
 * model and knowledge a conversation uses, the conversations themselves, and the floating-panel
 * viewer preferences that moved here from Settings. These describe how a conversation behaves
 * and belong together, not beside password changes and cookie preferences.
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
    <AppShell title="Application settings">
      <Paper elevation={1} sx={{ width: '100%', flex: 1, minHeight: 0, display: 'flex', flexDirection: 'column' }}>
        <Tabs
          value={tab}
          onChange={(_, value: number) => setTab(value)}
          variant="fullWidth"
          sx={{ borderBottom: 1, borderColor: 'divider' }}
        >
          <Tab label="Voice" value={CHAT_SETTINGS_TAB_INDEX.Voice} />
          <Tab label="Chat Configuration" value={CHAT_SETTINGS_TAB_INDEX.ChatConfiguration} />
          <Tab label="Chat History" value={CHAT_SETTINGS_TAB_INDEX.ChatHistory} />
          <Tab label="Viewer" value={CHAT_SETTINGS_TAB_INDEX.Viewer} />
        </Tabs>
        <Box sx={{ flex: 1, minHeight: 0, overflow: 'auto', p: 3 }}>
          <TabPanel value={tab} index={CHAT_SETTINGS_TAB_INDEX.Voice}>
            <TabContentContainer>
              <VoiceTab />
            </TabContentContainer>
          </TabPanel>
          <TabPanel value={tab} index={CHAT_SETTINGS_TAB_INDEX.ChatConfiguration}>
            <TabContentContainer>
              <ChatConfigurationTab />
            </TabContentContainer>
          </TabPanel>
          <TabPanel value={tab} index={CHAT_SETTINGS_TAB_INDEX.ChatHistory}>
            <TabContentContainer>
              <ChatHistoryTab />
            </TabContentContainer>
          </TabPanel>
          <TabPanel value={tab} index={CHAT_SETTINGS_TAB_INDEX.Viewer}>
            <TabContentContainer>
              <ViewerTab />
            </TabContentContainer>
          </TabPanel>
        </Box>
      </Paper>
    </AppShell>
  )
}

import { Alert, Box, Button, CircularProgress, Divider, Stack, Typography } from '@mui/material'
import { useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { useNavigate } from 'react-router'
import { EmptyState } from '../../../components/EmptyState'
import { ErrorState } from '../../../components/ErrorState'
import { useActiveConversationStore } from '../../chat/activeConversationStore'
import { updateChatModelSelection } from '../../chat/api/chatsApi'
import { ProviderModelSelector } from '../../chat/components/ProviderModelSelector'
import { useAiProviders } from '../../chat/hooks/useAiCatalog'
import { useChatDetail } from '../../chat/hooks/useChats'
import { SETTINGS_TAB_INDEX } from '../settingsTabs'

/**
 * specs/025-chat-configuration-settings — a hub, not an embedded-controls tab (Clarifications
 * Q1): hosts only the one genuinely new control (changing the model of the conversation
 * currently open, FR-004), and links out to the unmodified "AI Providers" and "Voice" tabs
 * (FR-002/FR-003) rather than duplicating their controls inline (FR-012).
 */
export function ChatConfigurationTab() {
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const activeChatId = useActiveConversationStore((s) => s.activeChatId)
  const { data: providers, isPending: isProvidersPending } = useAiProviders()
  const {
    data: chatDetail,
    isPending: isChatDetailPending,
    isError: isChatDetailError,
    refetch: refetchChatDetail,
  } = useChatDetail(activeChatId)
  const [saveError, setSaveError] = useState<string | null>(null)

  const handleSelect = (providerId: string, modelId: string) => {
    if (!activeChatId) return
    updateChatModelSelection(activeChatId, providerId, modelId)
      .then(() => queryClient.invalidateQueries({ queryKey: ['chats', activeChatId, 'detail'] }))
      .catch((err: unknown) => {
        setSaveError(err instanceof Error ? err.message : 'Failed to save the model selection.')
      })
  }

  const goToSettingsTab = (tab: number) => navigate('/settings', { state: { tab } })

  const renderCurrentConversationControl = () => {
    // FR-005/spec.md Edge Cases: reuses AiProvidersTab's exact "no providers configured"
    // empty state — ProviderModelSelector itself renders nothing in this situation, which
    // would otherwise read as a silent failure here (constitution §2.VIII).
    if (!isProvidersPending && providers && providers.length === 0) {
      return (
        <EmptyState
          title="No AI providers are enabled yet"
          description="An administrator needs to configure one first."
        />
      )
    }

    if (activeChatId === null) {
      return (
        <EmptyState
          title="No conversation is currently open"
          description="Open a conversation in the workspace to change its model here."
        />
      )
    }

    if (isChatDetailPending) {
      return <CircularProgress size={24} aria-label="Loading conversation…" />
    }

    if (isChatDetailError) {
      return (
        <ErrorState
          title="Couldn't load this conversation"
          description="Please try again."
          onRetry={() => void refetchChatDetail()}
        />
      )
    }

    return (
      <Stack direction="row" spacing={2} sx={{ maxWidth: 480 }}>
        <ProviderModelSelector
          providerId={chatDetail?.providerId ?? null}
          modelId={chatDetail?.modelId ?? null}
          onSelect={handleSelect}
        />
      </Stack>
    )
  }

  return (
    <Stack spacing={4}>
      <Box>
        <Typography variant="h6" sx={{ mb: 1 }}>
          Current conversation model
        </Typography>
        <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
          Changes the AI model for the conversation you currently have open — applies
          immediately, and does not change your default for new conversations.
        </Typography>
        {saveError && (
          <Alert severity="error" sx={{ mb: 2, maxWidth: 480 }} onClose={() => setSaveError(null)}>
            {saveError}
          </Alert>
        )}
        {renderCurrentConversationControl()}
      </Box>

      <Divider />

      <Box>
        <Typography variant="h6" sx={{ mb: 1 }}>
          AI model &amp; provider
        </Typography>
        <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
          Set your default AI provider and model, used for every new conversation you start.
        </Typography>
        <Button variant="outlined" onClick={() => goToSettingsTab(SETTINGS_TAB_INDEX.AiProviders)}>
          Go to AI Providers
        </Button>
      </Box>

      <Divider />

      <Box>
        <Typography variant="h6" sx={{ mb: 1 }}>
          Voice, speech-to-text &amp; text-to-speech
        </Typography>
        <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
          Manage voice conversation mode, mute, selected voice, speed, style, and
          microphone/speaker devices.
        </Typography>
        <Button variant="outlined" onClick={() => goToSettingsTab(SETTINGS_TAB_INDEX.Voice)}>
          Go to Voice
        </Button>
      </Box>
    </Stack>
  )
}

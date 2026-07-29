import BrightnessMediumIcon from '@mui/icons-material/Brightness4'
import ImageIcon from '@mui/icons-material/Image'
import MenuIcon from '@mui/icons-material/Menu'
import TranslateIcon from '@mui/icons-material/Translate'
import {
  Alert,
  AppBar,
  Box,
  Drawer,
  IconButton,
  Snackbar,
  Stack,
  Toolbar,
  Typography,
  useMediaQuery,
  useTheme,
} from '@mui/material'
import { useQueryClient } from '@tanstack/react-query'
import { useEffect, useRef, useState } from 'react'
import { ChatComposer } from '../components/ChatComposer'
import { ChatSidebar } from '../components/ChatSidebar'
import { LanguageSelector } from '../components/LanguageSelector'
import { MessageBubble } from '../components/MessageBubble'
import { useChatMessages } from '../hooks/useChats'
import { useChatStream } from '../hooks/useChatStream'
import { BrandMark } from '../../../components/BrandMark'
import { UserMenu } from '../../../components/UserMenu'
import { useThemeStore } from '../../../store/themeStore'
import { useTextToSpeech } from '../voice/useTextToSpeech'

/**
 * Owns which chat is selected (2026-07-28 ChatGPT-style history decision). `ConversationView`
 * below is remounted (via `key`) only on an *explicit* navigation — picking a different
 * sidebar chat, or starting a new one — never when a chat is auto-created mid-send (see
 * `handleChatCreated`), so an in-flight stream is never interrupted by its own id arriving.
 */
export function ChatPage() {
  const [selectedChatId, setSelectedChatId] = useState<string | null>(null)
  const [viewKey, setViewKey] = useState(0)
  const [language, setLanguage] = useState('en')
  const theme = useTheme()
  const isMobile = useMediaQuery(theme.breakpoints.down('sm'))
  const [mobileSidebarOpen, setMobileSidebarOpen] = useState(false)
  const queryClient = useQueryClient()

  const handleSelectChat = (id: string) => {
    setSelectedChatId(id)
    setViewKey((k) => k + 1)
    setMobileSidebarOpen(false)
  }

  const handleNewChat = () => {
    setSelectedChatId(null)
    setViewKey((k) => k + 1)
    setMobileSidebarOpen(false)
  }

  const handleChatCreated = (id: string) => {
    setSelectedChatId(id)
    void queryClient.invalidateQueries({ queryKey: ['chats'] })
  }

  const sidebar = <ChatSidebar selectedChatId={selectedChatId} onSelectChat={handleSelectChat} onNewChat={handleNewChat} />

  return (
    <Box sx={{ display: 'flex', height: '100vh' }}>
      {isMobile ? (
        <Drawer open={mobileSidebarOpen} onClose={() => setMobileSidebarOpen(false)}>
          {sidebar}
        </Drawer>
      ) : (
        sidebar
      )}
      <ConversationView
        key={viewKey}
        chatId={selectedChatId}
        language={language}
        onLanguageChange={setLanguage}
        onChatCreated={handleChatCreated}
        isMobile={isMobile}
        onOpenSidebar={() => setMobileSidebarOpen(true)}
      />
    </Box>
  )
}

interface ConversationViewProps {
  chatId: string | null
  language: string
  onLanguageChange: (language: string) => void
  onChatCreated: (id: string) => void
  isMobile: boolean
  onOpenSidebar: () => void
}

function ConversationView({ chatId, language, onLanguageChange, onChatCreated, isMobile, onOpenSidebar }: ConversationViewProps) {
  const { data: persistedMessages } = useChatMessages(chatId)
  const { messages, isStreaming, error, clearError, send, sendImage, sendTranslation } = useChatStream(
    chatId,
    persistedMessages,
    onChatCreated,
  )
  const theme = useTheme()
  const toggleTheme = useThemeStore((s) => s.toggle)
  const tts = useTextToSpeech()
  const scrollRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    scrollRef.current?.scrollIntoView({ behavior: 'smooth' })
  }, [messages])

  // Restores the legacy app's behavior of speaking every AI reply aloud as soon as it
  // finishes streaming (FR-006) — the React migration had only kept the Translate button's
  // on-demand read-aloud, dropping the automatic one entirely.
  const wasStreamingRef = useRef(false)
  useEffect(() => {
    if (wasStreamingRef.current && !isStreaming) {
      const last = messages[messages.length - 1]
      if (last?.role === 'assistant' && last.content) {
        tts.speak(last.content, language)
      }
    }
    wasStreamingRef.current = isStreaming
  }, [isStreaming, messages, language, tts])

  const handleTranslateLast = async () => {
    const lastAssistant = [...messages].reverse().find((m) => m.role === 'assistant')
    if (!lastAssistant) return
    const plain = await sendTranslation(lastAssistant.content, language)
    tts.speak(plain, language)
  }

  const handleGenerateImage = async () => {
    const prompt = window.prompt('Describe the image to generate:')
    if (!prompt) return
    await sendImage(prompt)
  }

  return (
    <Box sx={{ flex: 1, display: 'flex', flexDirection: 'column', minWidth: 0 }}>
      <AppBar position="static" color="default" elevation={0}>
        <Toolbar>
          {isMobile && (
            <IconButton onClick={onOpenSidebar} aria-label="Open chat list" sx={{ mr: 1 }}>
              <MenuIcon />
            </IconButton>
          )}
          <Stack direction="row" spacing={1.5} sx={{ alignItems: 'center', flex: 1 }}>
            <BrandMark size={28} color={theme.palette.primary.main} />
            <Typography variant="h6">Ask Lucy</Typography>
          </Stack>
          <Stack direction="row" spacing={1} sx={{ alignItems: 'center' }}>
            <LanguageSelector value={language} onChange={onLanguageChange} />
            <IconButton onClick={handleTranslateLast} aria-label="Translate last response">
              <TranslateIcon />
            </IconButton>
            <IconButton onClick={handleGenerateImage} aria-label="Generate image">
              <ImageIcon />
            </IconButton>
            <IconButton onClick={toggleTheme} aria-label="Toggle theme">
              <BrightnessMediumIcon />
            </IconButton>
            <UserMenu />
          </Stack>
        </Toolbar>
      </AppBar>

      <Box sx={{ flex: 1, overflow: 'auto', p: 2, bgcolor: 'background.default' }}>
        <Box sx={{ maxWidth: 800, mx: 'auto' }}>
          {messages.length === 0 && (
            <Typography variant="body2" color="text.secondary" sx={{ textAlign: 'center', mt: 8 }}>
              Start a conversation with Ask Lucy.
            </Typography>
          )}
          {messages.map((message, index) => (
            <MessageBubble key={index} message={message} />
          ))}
          <div ref={scrollRef} />
        </Box>
      </Box>

      <ChatComposer onSend={send} disabled={isStreaming} />
      <Snackbar open={Boolean(error)} autoHideDuration={5000} onClose={clearError}>
        <Alert severity="error" variant="filled" onClose={clearError}>
          {error}
        </Alert>
      </Snackbar>
    </Box>
  )
}

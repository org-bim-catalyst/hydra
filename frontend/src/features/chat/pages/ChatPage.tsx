import BrightnessMediumIcon from '@mui/icons-material/Brightness4'
import ImageIcon from '@mui/icons-material/Image'
import MenuIcon from '@mui/icons-material/Menu'
import TranslateIcon from '@mui/icons-material/Translate'
import {
  AppBar,
  Avatar,
  Box,
  Drawer,
  IconButton,
  Stack,
  Toolbar,
  Typography,
  useMediaQuery,
  useTheme,
} from '@mui/material'
import { useEffect, useRef, useState } from 'react'
import { generateImage, translate } from '../api/aiApi'
import { ChatComposer } from '../components/ChatComposer'
import { ChatSidebar } from '../components/ChatSidebar'
import { LanguageSelector } from '../components/LanguageSelector'
import { MessageBubble } from '../components/MessageBubble'
import { useChatStream } from '../hooks/useChatStream'
import { UserMenu } from '../../../components/UserMenu'
import { useThemeStore } from '../../../store/themeStore'
import { useTextToSpeech } from '../voice/useTextToSpeech'

export function ChatPage() {
  const { messages, isStreaming, send } = useChatStream()
  const [language, setLanguage] = useState('en')
  const toggleTheme = useThemeStore((s) => s.toggle)
  const tts = useTextToSpeech()
  const scrollRef = useRef<HTMLDivElement>(null)
  const theme = useTheme()
  const isMobile = useMediaQuery(theme.breakpoints.down('sm'))
  const [mobileSidebarOpen, setMobileSidebarOpen] = useState(false)

  useEffect(() => {
    scrollRef.current?.scrollIntoView({ behavior: 'smooth' })
  }, [messages])

  const handleTranslateLast = async () => {
    const lastAssistant = [...messages].reverse().find((m) => m.role === 'assistant')
    if (!lastAssistant) return
    const html = await translate(lastAssistant.content, language)
    const container = document.createElement('div')
    container.innerHTML = html
    tts.speak(container.textContent ?? '', language)
  }

  const handleGenerateImage = async () => {
    const prompt = window.prompt('Describe the image to generate:')
    if (!prompt) return
    const url = await generateImage(prompt)
    window.open(url, '_blank', 'noopener,noreferrer')
  }

  return (
    <Box sx={{ display: 'flex', height: '100vh' }}>
      {isMobile ? (
        <Drawer open={mobileSidebarOpen} onClose={() => setMobileSidebarOpen(false)}>
          <ChatSidebar />
        </Drawer>
      ) : (
        <ChatSidebar />
      )}
      <Box sx={{ flex: 1, display: 'flex', flexDirection: 'column', minWidth: 0 }}>
        <AppBar position="static" color="default" elevation={0}>
          <Toolbar>
            {isMobile && (
              <IconButton onClick={() => setMobileSidebarOpen(true)} aria-label="Open chat list" sx={{ mr: 1 }}>
                <MenuIcon />
              </IconButton>
            )}
            <Stack direction="row" spacing={1.5} sx={{ alignItems: 'center', flex: 1 }}>
              <Avatar src="/lucy.png" alt="Ask Lucy" sx={{ width: 32, height: 32 }} />
              <Typography variant="h6">Ask Lucy</Typography>
            </Stack>
            <Stack direction="row" spacing={1} sx={{ alignItems: 'center' }}>
              <LanguageSelector value={language} onChange={setLanguage} />
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

        <ChatComposer onSend={send} disabled={isStreaming} language={language} />
      </Box>
    </Box>
  )
}

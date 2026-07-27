import { useCallback, useRef, useState } from 'react'
import { streamChat, type ChatMessage } from '../api/aiApi'

export function useChatStream() {
  const [messages, setMessages] = useState<ChatMessage[]>([])
  const [isStreaming, setIsStreaming] = useState(false)
  const abortRef = useRef<AbortController | null>(null)

  const send = useCallback(
    async (content: string) => {
      const userMessage: ChatMessage = { role: 'user', content }
      const history = [...messages, userMessage]
      setMessages([...history, { role: 'assistant', content: '' }])
      setIsStreaming(true)

      const controller = new AbortController()
      abortRef.current = controller

      try {
        let assistantContent = ''
        for await (const chunk of streamChat(history, controller.signal)) {
          assistantContent += chunk
          setMessages([...history, { role: 'assistant', content: assistantContent }])
        }
      } finally {
        setIsStreaming(false)
        abortRef.current = null
      }
    },
    [messages],
  )

  const stop = useCallback(() => abortRef.current?.abort(), [])

  return { messages, isStreaming, send, stop }
}

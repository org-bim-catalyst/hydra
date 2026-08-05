import { apiFetch } from '../../../api/httpClient'

/** contracts/conversation-retrieval-api.md */
export const getConversationKnowledgeBases = (chatId: string) =>
  apiFetch<{ knowledgeBaseIds: string[] }>(`/chats/${chatId}/knowledge-bases`)

/** Full-replace attach/detach (specs/016-rag-semantic-search US1) — an empty array detaches every knowledge base. */
export const updateConversationKnowledgeBases = (chatId: string, knowledgeBaseIds: string[]) =>
  apiFetch<void>(`/chats/${chatId}/knowledge-bases`, {
    method: 'PUT',
    body: JSON.stringify({ knowledgeBaseIds }),
  })

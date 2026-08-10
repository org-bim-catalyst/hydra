import { API_BASE_URL, apiFetch } from '../../../api/httpClient'
import { useAuthStore } from '../../../store/authStore'

export type PromptExecutionOrigin = 'TestingWorkspace' | 'ConversationInsertion'
export type PromptExecutionOutcome = 'Success' | 'Failed'
export type PromptRatingValue = 'Good' | 'NeedsImprovement' | 'Failed'

export interface PromptExecutionSummary {
  id: string
  versionNumber: number
  origin: PromptExecutionOrigin
  providerKey: string
  modelKey: string
  outcome: PromptExecutionOutcome
  latencyMs: number | null
  estimatedCostUsd: number | null
  createdAtUtc: string
}

export interface PromptExecutionDetail {
  id: string
  promptId: string
  versionNumber: number
  origin: PromptExecutionOrigin
  providerKey: string
  modelKey: string
  temperature: number | null
  maxOutputTokens: number | null
  resolvedVariableValuesJson: string
  outcome: PromptExecutionOutcome
  errorDetail: string | null
  latencyMs: number | null
  outputText: string | null
  inputTokenCount: number | null
  outputTokenCount: number | null
  estimatedCostUsd: number | null
  ragCitationsJson: string | null
  memoryReferencesJson: string | null
  rating: PromptRatingValue | null
  createdAtUtc: string
}

export interface PromptTestCase {
  id: string
  name: string
  variableValuesJson: string
  expectedOutput: string | null
  evaluationCriteria: string | null
  providerKey: string
  modelKey: string
  sourceExecutionId: string | null
  createdAtUtc: string
}

export interface PagedResult<T> {
  items: T[]
  nextCursor: string | null
}

export interface GenerationParameters {
  temperature?: number | null
  maxTokens?: number | null
  jsonMode?: boolean | null
}

export interface ExecutePromptInput {
  versionNumber?: number | null
  variableValues: Record<string, string | null>
  providerId: string
  modelId: string
  generationParameters?: GenerationParameters | null
  useRagContext?: boolean
  knowledgeBaseIds?: string[] | null
  useMemoryContext?: boolean
}

export type PromptExecutionStreamEvent =
  | { type: 'content'; content: string }
  | { type: 'error'; errorType: string; title: string; detail: string }
  | { type: 'done'; executionId: string }

/**
 * Streams a prompt test execution via SSE (spec.md FR-041, contracts/prompt-execution-api.md).
 * Uses `fetch` + a `ReadableStream` reader rather than the browser's native `EventSource`
 * (mirrors `aiApi.ts`'s `streamChat`) — `EventSource` cannot send a custom `Authorization` header.
 * Every event is JSON-encoded (unlike chat's raw-text content deltas), so parsing is a plain
 * `JSON.parse` per `data:` line, no special-prefix handling needed.
 */
export async function* executePromptStream(
  promptId: string,
  input: ExecutePromptInput,
  signal?: AbortSignal,
): AsyncGenerator<PromptExecutionStreamEvent> {
  const accessToken = useAuthStore.getState().accessToken

  const response = await fetch(`${API_BASE_URL}/prompts/${promptId}/executions`, {
    method: 'POST',
    signal,
    headers: {
      'Content-Type': 'application/json',
      ...(accessToken ? { Authorization: `Bearer ${accessToken}` } : {}),
    },
    body: JSON.stringify(input),
  })

  if (!response.ok || !response.body) {
    const problem = await response.json().catch(() => undefined)
    throw new Error(problem?.detail ?? problem?.title ?? `Execution request failed with ${response.status}`)
  }

  const reader = response.body.getReader()
  const decoder = new TextDecoder()
  let buffer = ''

  while (true) {
    const { done, value } = await reader.read()
    if (done) return

    buffer += decoder.decode(value, { stream: true })
    const lines = buffer.split('\n\n')
    buffer = lines.pop() ?? ''

    for (const line of lines) {
      if (!line.startsWith('data:')) continue
      const data = line.slice('data:'.length).replace(/^ /, '')
      yield JSON.parse(data) as PromptExecutionStreamEvent
    }
  }
}

export const listExecutions = (promptId: string, cursor?: string | null, pageSize = 50) => {
  const query = new URLSearchParams({ pageSize: String(pageSize) })
  if (cursor) query.set('cursor', cursor)
  return apiFetch<PagedResult<PromptExecutionSummary>>(`/prompts/${promptId}/executions?${query.toString()}`)
}

export const getExecution = (executionId: string) => apiFetch<PromptExecutionDetail>(`/prompt-executions/${executionId}`)

export const compareExecutions = (executionIds: string[]) => {
  const query = new URLSearchParams()
  for (const id of executionIds) query.append('executionIds', id)
  return apiFetch<PromptExecutionDetail[]>(`/prompt-executions/compare?${query.toString()}`)
}

export const rateExecution = (executionId: string, value: PromptRatingValue) =>
  apiFetch<void>(`/prompt-executions/${executionId}/rating`, { method: 'PUT', body: JSON.stringify({ value }) })

export interface SaveTestCaseInput {
  name: string
  variableValuesJson: string
  expectedOutput?: string | null
  evaluationCriteria?: string | null
  providerKey: string
  modelKey: string
  sourceExecutionId?: string | null
}

export const saveTestCase = (promptId: string, input: SaveTestCaseInput) =>
  apiFetch<PromptTestCase>(`/prompts/${promptId}/test-cases`, { method: 'POST', body: JSON.stringify(input) })

export const listTestCases = (promptId: string) => apiFetch<PromptTestCase[]>(`/prompts/${promptId}/test-cases`)

export const deleteTestCase = (promptId: string, testCaseId: string) =>
  apiFetch<void>(`/prompts/${promptId}/test-cases/${testCaseId}`, { method: 'DELETE' })

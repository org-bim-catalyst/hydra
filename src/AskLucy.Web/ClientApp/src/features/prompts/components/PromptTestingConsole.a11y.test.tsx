import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest'
import type { PromptDetail } from '../api/promptsApi'
import { NO_REQUIRED_CAPABILITIES } from '../api/promptsApi'
import { PromptTestingConsole } from './PromptTestingConsole'

expect.extend(toHaveNoViolations)

const PROMPT_ID = '11111111-1111-1111-1111-111111111111'

const prompt: PromptDetail = {
  id: PROMPT_ID,
  name: 'Summarize a document',
  description: 'Summarizes a document.',
  promptType: 'Summarization',
  status: 'Active',
  systemInstructions: 'You are a summarizer.',
  developerInstructions: null,
  userInstructions: 'Summarize {{document}}.',
  contextText: null,
  examplesText: null,
  outputInstructions: null,
  constraints: null,
  categoryId: null,
  folderId: null,
  isFavorite: false,
  isPinned: false,
  requiredCapabilities: NO_REQUIRED_CAPABILITIES,
  preferredModelKey: null,
  currentVersion: { id: '22222222-2222-2222-2222-222222222222', versionNumber: 1 },
  variables: [
    {
      name: 'document',
      description: 'Source document',
      type: 'File',
      isRequired: true,
      defaultValue: null,
      exampleValue: null,
      validationRulesJson: null,
      orderIndex: 0,
    },
  ],
  tags: [],
  usageCount: 0,
  lastSuccessfulUseAtUtc: null,
  createdAtUtc: '2026-08-01T00:00:00Z',
  modifiedAtUtc: null,
}

const server = setupServer(
  http.get('*/api/v1/ai/providers', () => HttpResponse.json([{ id: '33333333-3333-3333-3333-333333333333', providerKey: 'openai', displayName: 'OpenAI', healthStatus: 'Healthy', healthStatusCheckedAtUtc: null }])),
  http.get('*/api/v1/ai/providers/:id/models', () => HttpResponse.json([])),
  http.get(`*/api/v1/prompts/${PROMPT_ID}/executions`, () => HttpResponse.json({ items: [], nextCursor: null })),
  http.get('*/api/v1/knowledge-bases*', () => HttpResponse.json({ items: [], nextCursor: null })),
)

beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

describe('PromptTestingConsole accessibility (spec.md "Prompt Testing UI")', () => {
  it('has no automatically detectable a11y violations (constitution §10)', async () => {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const { container, findByLabelText } = render(
      <QueryClientProvider client={queryClient}>
        <PromptTestingConsole prompt={prompt} />
      </QueryClientProvider>,
    )

    await findByLabelText('document', { exact: false })

    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })
})

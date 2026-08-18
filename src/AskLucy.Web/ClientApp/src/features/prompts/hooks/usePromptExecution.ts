import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useCallback, useRef, useState } from 'react'
import * as executionApi from '../api/promptExecutionApi'
import type { ExecutePromptInput } from '../api/promptExecutionApi'

export const PROMPT_EXECUTIONS_QUERY_KEY = (promptId: string) => ['prompts', promptId, 'executions']

/**
 * Drives one streamed execution (spec.md FR-041). Exposes accumulated output text, in-flight
 * state, and any error — repeated execution without leaving the workspace is just calling
 * `run` again (spec.md "Prompt Testing UI").
 */
export function useExecutePromptStream(promptId: string) {
  const queryClient = useQueryClient()
  const [output, setOutput] = useState('')
  const [isRunning, setIsRunning] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [executionId, setExecutionId] = useState<string | null>(null)
  const abortRef = useRef<AbortController | null>(null)

  const run = useCallback(
    async (input: ExecutePromptInput) => {
      abortRef.current?.abort()
      const controller = new AbortController()
      abortRef.current = controller

      setOutput('')
      setError(null)
      setExecutionId(null)
      setIsRunning(true)

      try {
        for await (const event of executionApi.executePromptStream(promptId, input, controller.signal)) {
          if (event.type === 'content') {
            setOutput((prev) => prev + event.content)
          } else if (event.type === 'error') {
            setError(event.detail)
          } else if (event.type === 'done') {
            setExecutionId(event.executionId)
          }
        }
      } catch (err) {
        if (!(err instanceof DOMException && err.name === 'AbortError')) {
          setError(err instanceof Error ? err.message : 'The prompt execution failed. Please try again.')
        }
      } finally {
        setIsRunning(false)
        queryClient.invalidateQueries({ queryKey: PROMPT_EXECUTIONS_QUERY_KEY(promptId) })
      }
    },
    [promptId, queryClient],
  )

  const cancel = useCallback(() => abortRef.current?.abort(), [])

  return { output, isRunning, error, executionId, run, cancel }
}

export function useExecutions(promptId: string) {
  return useQuery({
    queryKey: PROMPT_EXECUTIONS_QUERY_KEY(promptId),
    queryFn: () => executionApi.listExecutions(promptId),
  })
}

export function useExecution(executionId: string | null) {
  return useQuery({
    queryKey: ['prompt-executions', executionId],
    queryFn: () => executionApi.getExecution(executionId!),
    enabled: executionId !== null,
  })
}

export function useCompareExecutions(executionIds: string[]) {
  return useQuery({
    queryKey: ['prompt-executions', 'compare', ...executionIds],
    queryFn: () => executionApi.compareExecutions(executionIds),
    enabled: executionIds.length >= 2,
  })
}

export function useRateExecution() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ executionId, value }: { executionId: string; value: executionApi.PromptRatingValue }) =>
      executionApi.rateExecution(executionId, value),
    onSuccess: (_data, variables) => queryClient.invalidateQueries({ queryKey: ['prompt-executions', variables.executionId] }),
  })
}

export function useTestCases(promptId: string) {
  return useQuery({
    queryKey: ['prompts', promptId, 'test-cases'],
    queryFn: () => executionApi.listTestCases(promptId),
  })
}

export function useSaveTestCase(promptId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (input: executionApi.SaveTestCaseInput) => executionApi.saveTestCase(promptId, input),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['prompts', promptId, 'test-cases'] }),
  })
}

export function useDeleteTestCase(promptId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (testCaseId: string) => executionApi.deleteTestCase(promptId, testCaseId),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['prompts', promptId, 'test-cases'] }),
  })
}

export interface ExecutionContextOptionsState {
  useRagContext: boolean
  knowledgeBaseIds: string[]
  useMemoryContext: boolean
}

export const DEFAULT_EXECUTION_CONTEXT_OPTIONS: ExecutionContextOptionsState = {
  useRagContext: false,
  knowledgeBaseIds: [],
  useMemoryContext: false,
}

import { apiFetch } from '../../../api/httpClient'
import type { PromptVariable } from './promptsApi'

export interface PromptVersionSummary {
  id: string
  versionNumber: number
  changeDescription: string | null
  createdBy: string
  createdAtUtc: string
}

export interface PromptVersionDetail {
  id: string
  versionNumber: number
  systemInstructions: string | null
  developerInstructions: string | null
  userInstructions: string
  contextText: string | null
  examplesText: string | null
  outputInstructions: string | null
  constraints: string | null
  providerKey: string | null
  modelKey: string | null
  temperature: number | null
  maxOutputTokens: number | null
  structuredOutputRequested: boolean
  variables: PromptVariable[]
  changeDescription: string | null
  createdBy: string
  createdAtUtc: string
}

export interface PromptVersionFieldDiff {
  fieldName: string
  fromValue: string | null
  toValue: string | null
}

export interface PromptVersionComparison {
  from: PromptVersionSummary
  to: PromptVersionSummary
  differences: PromptVersionFieldDiff[]
}

export const listVersions = (promptId: string) => apiFetch<PromptVersionSummary[]>(`/prompts/${promptId}/versions`)

export const getVersion = (promptId: string, versionNumber: number) =>
  apiFetch<PromptVersionDetail>(`/prompts/${promptId}/versions/${versionNumber}`)

export const compareVersions = (promptId: string, from: number, to: number) =>
  apiFetch<PromptVersionComparison>(`/prompts/${promptId}/versions/compare?from=${from}&to=${to}`)

export const restoreVersion = (promptId: string, versionNumber: number) =>
  apiFetch(`/prompts/${promptId}/versions/${versionNumber}/actions/restore`, { method: 'POST' })

export const duplicateVersion = (promptId: string, versionNumber: number) =>
  apiFetch(`/prompts/${promptId}/versions/${versionNumber}/actions/duplicate`, { method: 'POST' })

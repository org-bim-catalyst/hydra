import { apiFetch } from '../../../api/httpClient'
import type { NotificationCategory, NotificationChannel } from './adminNotificationsApi'

// specs/067 US7, contracts/admin-notifications-api.md template endpoints. Enums arrive as strings.

export type TemplateVersionStatus = 'Draft' | 'Published' | 'Archived'

export interface NotificationTemplateSummary {
  templateId: string
  type: string
  category: NotificationCategory
  channel: NotificationChannel
  language: string
  name: string
  publishedVersion: { id: string; versionNumber: number; publishedAtUtc: string | null } | null
  hasDraft: boolean
}

export interface TemplateVariable {
  name: string
  sample: string
  fallback: string
  isStandard: boolean
}

export interface TemplateVersionSummary {
  id: string
  versionNumber: number
  status: TemplateVersionStatus
  createdAtUtc: string
  createdBy: string | null
  publishedAtUtc: string | null
  archivedAtUtc: string | null
}

export interface NotificationTemplateDetail {
  templateId: string
  type: string
  category: NotificationCategory
  channel: NotificationChannel
  language: string
  name: string
  publishedVersionId: string | null
  isShippedDefault: boolean
  versions: TemplateVersionSummary[]
  declaredVariables: TemplateVariable[]
}

/** One version's fields. The email fields are null for an in-app template and the other way round. `rowVersion` goes back as `If-Match`. */
export interface TemplateVersion {
  id: string
  templateId: string
  versionNumber: number
  status: TemplateVersionStatus
  rowVersion: string
  subject: string | null
  preheader: string | null
  greeting: string | null
  heading: string | null
  bodyParagraphs: string[]
  actionLabel: string | null
  safetyNote: string | null
  footerNote: string | null
  title: string | null
  message: string | null
  usedVariables: string[]
  createdAtUtc: string
  publishedAtUtc: string | null
  archivedAtUtc: string | null
}

export type TemplateVersionInput = Partial<
  Pick<
    TemplateVersion,
    | 'subject'
    | 'preheader'
    | 'greeting'
    | 'heading'
    | 'bodyParagraphs'
    | 'actionLabel'
    | 'safetyNote'
    | 'footerNote'
    | 'title'
    | 'message'
  >
> & { copyFromVersionId?: string }

/** The HTML is for a sandboxed frame only (`sandbox=""`), never for the page's own DOM. */
export interface TemplatePreview {
  subject: string | null
  html: string | null
  text: string | null
  title: string | null
  message: string | null
  actionLabel: string | null
  language: string
  direction: 'ltr' | 'rtl'
}

export interface TemplateListFilters {
  category?: NotificationCategory
  channel?: NotificationChannel
  language?: string
  type?: string
}

export const ADMIN_TEMPLATES_QUERY_KEY = ['admin', 'notification-templates']

const BASE = '/admin/notifications/templates'

function query(params: Record<string, string | undefined>): string {
  const search = new URLSearchParams()
  for (const [key, value] of Object.entries(params)) {
    if (value) search.set(key, value)
  }
  const text = search.toString()
  return text ? `?${text}` : ''
}

const ifMatch = (rowVersion: string) => ({ 'If-Match': rowVersion })

export const getNotificationTemplates = (filters: TemplateListFilters = {}) =>
  apiFetch<NotificationTemplateSummary[]>(`${BASE}${query({ ...filters })}`)

export const getNotificationTemplate = (templateId: string) =>
  apiFetch<NotificationTemplateDetail>(`${BASE}/${templateId}`)

export const getTemplateVersion = (templateId: string, versionId: string) =>
  apiFetch<TemplateVersion>(`${BASE}/${templateId}/versions/${versionId}`)

export const createTemplateDraft = (templateId: string, input: TemplateVersionInput) =>
  apiFetch<TemplateVersion>(`${BASE}/${templateId}/versions`, {
    method: 'POST',
    body: JSON.stringify(input),
  })

export const updateTemplateDraft = (
  templateId: string,
  versionId: string,
  rowVersion: string,
  input: TemplateVersionInput,
) =>
  apiFetch<TemplateVersion>(`${BASE}/${templateId}/versions/${versionId}`, {
    method: 'PUT',
    headers: ifMatch(rowVersion),
    body: JSON.stringify(input),
  })

export const previewTemplateVersion = (
  templateId: string,
  versionId: string,
  variables?: Record<string, string>,
) =>
  apiFetch<TemplatePreview>(`${BASE}/${templateId}/versions/${versionId}/actions/preview`, {
    method: 'POST',
    body: JSON.stringify({ variables }),
  })

export const sendTemplateTest = (templateId: string, versionId: string) =>
  apiFetch<{ sentTo: string }>(`${BASE}/${templateId}/versions/${versionId}/actions/send-test`, {
    method: 'POST',
  })

export const publishTemplateVersion = (templateId: string, versionId: string, rowVersion: string) =>
  apiFetch<TemplateVersion>(`${BASE}/${templateId}/versions/${versionId}/actions/publish`, {
    method: 'POST',
    headers: ifMatch(rowVersion),
  })

export const archiveTemplateVersion = (templateId: string, versionId: string, rowVersion: string) =>
  apiFetch<TemplateVersion>(`${BASE}/${templateId}/versions/${versionId}/actions/archive`, {
    method: 'POST',
    headers: ifMatch(rowVersion),
  })

import { lazy } from 'react'
import type { ComponentType } from 'react'

/**
 * The account destinations, lazily loaded the same way the router loads them so opening one as
 * a modal costs no more up front than navigating to it would.
 *
 * Keyed by the path in `useAccountMenuItems`. A destination missing from here simply navigates
 * instead of opening a modal, which is why `UserMenu` checks membership rather than assuming.
 */
/**
 * `size` mirrors the two the reference exposes — `max-w-3xl` and `max-w-6xl`. Forms take the
 * narrow one; anything with a table, a chart or a card grid takes the wide one, because at
 * 768px those reflow into something worse than the full page they came from.
 */
export const MODAL_PAGES: Record<string, { title: string; size?: 'default' | 'large'; Component: ComponentType }> = {
  '/profile': {
    title: 'Profile',
    Component: lazy(() => import('../../features/profile/pages/ProfilePage').then((m) => ({ default: m.ProfilePage }))),
  },
  '/settings': {
    title: 'Settings',
    Component: lazy(() =>
      import('../../features/settings/pages/SettingsPage').then((m) => ({ default: m.SettingsPage })),
    ),
  },
  '/chat-settings': {
    title: 'Chat settings',
    Component: lazy(() =>
      import('../../features/settings/pages/ChatSettingsPage').then((m) => ({ default: m.ChatSettingsPage })),
    ),
  },
  '/documents': {
    title: 'Documents',
    size: 'large',
    Component: lazy(() =>
      import('../../features/documents/pages/DocumentWorkspacePage').then((m) => ({
        default: m.DocumentWorkspacePage,
      })),
    ),
  },
  '/knowledge-bases': {
    title: 'Knowledge Bases',
    size: 'large',
    Component: lazy(() =>
      import('../../features/knowledge-base/pages/KnowledgeBaseDashboardPage').then((m) => ({
        default: m.KnowledgeBaseDashboardPage,
      })),
    ),
  },
  '/memory': {
    title: 'Memory Center',
    size: 'large',
    Component: lazy(() =>
      import('../../features/memory/pages/MemoryCenterPage').then((m) => ({ default: m.MemoryCenterPage })),
    ),
  },
  '/prompts': {
    title: 'Prompts',
    size: 'large',
    Component: lazy(() =>
      import('../../features/prompts/pages/PromptLibraryPage').then((m) => ({ default: m.PromptLibraryPage })),
    ),
  },
  '/agents': {
    title: 'Agents',
    size: 'large',
    Component: lazy(() =>
      import('../../features/agents/pages/AgentLibraryPage').then((m) => ({ default: m.AgentLibraryPage })),
    ),
  },
  '/workflows': {
    title: 'Workflows',
    size: 'large',
    Component: lazy(() =>
      import('../../features/workflows/pages/WorkflowLibraryPage').then((m) => ({
        default: m.WorkflowLibraryPage,
      })),
    ),
  },
  '/admin/dashboard': {
    title: 'Admin Dashboard',
    size: 'large',
    Component: lazy(() =>
      import('../../features/admin/pages/AdminDashboardPage').then((m) => ({ default: m.AdminDashboardPage })),
    ),
  },
  '/privacy': {
    title: 'Privacy Policy',
    Component: lazy(() => import('../../features/privacy/pages/PrivacyPage').then((m) => ({ default: m.PrivacyPage }))),
  },
}

export function isAccountModalPath(path: string) {
  return path in MODAL_PAGES
}

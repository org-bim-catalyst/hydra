import { lazy } from 'react'
import type { ComponentType } from 'react'

/**
 * The account destinations, lazily loaded the same way the router loads them so opening one as
 * a modal costs no more up front than navigating to it would.
 *
 * Keyed by the path in `useAccountMenuItems`. A destination missing from here simply navigates
 * instead of opening a modal, which is why `UserMenu` checks membership rather than assuming.
 */
export const MODAL_PAGES: Record<string, { title: string; Component: ComponentType }> = {
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
    Component: lazy(() =>
      import('../../features/documents/pages/DocumentWorkspacePage').then((m) => ({
        default: m.DocumentWorkspacePage,
      })),
    ),
  },
  '/knowledge-bases': {
    title: 'Knowledge Bases',
    Component: lazy(() =>
      import('../../features/knowledge-base/pages/KnowledgeBaseDashboardPage').then((m) => ({
        default: m.KnowledgeBaseDashboardPage,
      })),
    ),
  },
  '/memory': {
    title: 'Memory Center',
    Component: lazy(() =>
      import('../../features/memory/pages/MemoryCenterPage').then((m) => ({ default: m.MemoryCenterPage })),
    ),
  },
  '/prompts': {
    title: 'Prompts',
    Component: lazy(() =>
      import('../../features/prompts/pages/PromptLibraryPage').then((m) => ({ default: m.PromptLibraryPage })),
    ),
  },
  '/agents': {
    title: 'Agents',
    Component: lazy(() =>
      import('../../features/agents/pages/AgentLibraryPage').then((m) => ({ default: m.AgentLibraryPage })),
    ),
  },
  '/workflows': {
    title: 'Workflows',
    Component: lazy(() =>
      import('../../features/workflows/pages/WorkflowLibraryPage').then((m) => ({
        default: m.WorkflowLibraryPage,
      })),
    ),
  },
  '/admin/dashboard': {
    title: 'Admin Dashboard',
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

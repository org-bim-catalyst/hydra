import { Fade } from '@mui/material'
import { lazy, Suspense } from 'react'
import { createBrowserRouter, Navigate, RouterProvider } from 'react-router'
import { ErrorPage } from '../components/ErrorPage'
import { AdminRoute } from './AdminRoute'
import { ProtectedRoute } from './ProtectedRoute'
import { PublicOnlyRoute } from './PublicOnlyRoute'

const LandingPage = lazy(() => import('../features/landing/pages/LandingPage').then((m) => ({ default: m.LandingPage })))
const LoginPage = lazy(() => import('../features/auth/pages/LoginPage').then((m) => ({ default: m.LoginPage })))
const RegisterPage = lazy(() =>
  import('../features/auth/pages/RegisterPage').then((m) => ({ default: m.RegisterPage })),
)
const ConfirmEmailPage = lazy(() =>
  import('../features/auth/pages/ConfirmEmailPage').then((m) => ({ default: m.ConfirmEmailPage })),
)
const ConfirmEmailChangePage = lazy(() =>
  import('../features/auth/pages/ConfirmEmailChangePage').then((m) => ({ default: m.ConfirmEmailChangePage })),
)
const ExternalLoginCompletePage = lazy(() =>
  import('../features/auth/pages/ExternalLoginCompletePage').then((m) => ({ default: m.ExternalLoginCompletePage })),
)
const ChatPage = lazy(() => import('../features/chat/pages/ChatPage').then((m) => ({ default: m.ChatPage })))
const DocumentWorkspacePage = lazy(() =>
  import('../features/documents/pages/DocumentWorkspacePage').then((m) => ({ default: m.DocumentWorkspacePage })),
)
const KnowledgeBaseDashboardPage = lazy(() =>
  import('../features/knowledge-base/pages/KnowledgeBaseDashboardPage').then((m) => ({ default: m.KnowledgeBaseDashboardPage })),
)
const KnowledgeBaseDetailPage = lazy(() =>
  import('../features/knowledge-base/pages/KnowledgeBaseDetailPage').then((m) => ({ default: m.KnowledgeBaseDetailPage })),
)
const MemoryCenterPage = lazy(() =>
  import('../features/memory/pages/MemoryCenterPage').then((m) => ({ default: m.MemoryCenterPage })),
)
const PromptLibraryPage = lazy(() =>
  import('../features/prompts/pages/PromptLibraryPage').then((m) => ({ default: m.PromptLibraryPage })),
)
const PromptEditorPage = lazy(() =>
  import('../features/prompts/pages/PromptEditorPage').then((m) => ({ default: m.PromptEditorPage })),
)
const AgentLibraryPage = lazy(() =>
  import('../features/agents/pages/AgentLibraryPage').then((m) => ({ default: m.AgentLibraryPage })),
)
const AgentBuilderPage = lazy(() =>
  import('../features/agents/pages/AgentBuilderPage').then((m) => ({ default: m.AgentBuilderPage })),
)
const AgentExecutionPage = lazy(() =>
  import('../features/agents/pages/AgentExecutionPage').then((m) => ({ default: m.AgentExecutionPage })),
)
const PrivacyPage = lazy(() =>
  import('../features/privacy/pages/PrivacyPage').then((m) => ({ default: m.PrivacyPage })),
)
const ProfilePage = lazy(() =>
  import('../features/profile/pages/ProfilePage').then((m) => ({ default: m.ProfilePage })),
)
const SettingsPage = lazy(() =>
  import('../features/settings/pages/SettingsPage').then((m) => ({ default: m.SettingsPage })),
)
const AdminUsersPage = lazy(() =>
  import('../features/admin/pages/AdminUsersPage').then((m) => ({ default: m.AdminUsersPage })),
)
const AdminDashboardPage = lazy(() =>
  import('../features/admin/pages/AdminDashboardPage').then((m) => ({ default: m.AdminDashboardPage })),
)
const AdminAiProvidersPage = lazy(() =>
  import('../features/admin/pages/AdminAiProvidersPage').then((m) => ({ default: m.AdminAiProvidersPage })),
)
const AdminAiCapabilitiesPage = lazy(() =>
  import('../features/admin/pages/AdminAiCapabilitiesPage').then((m) => ({ default: m.AdminAiCapabilitiesPage })),
)
const AgentPoliciesAdminPage = lazy(() =>
  import('../features/agents/pages/AgentPoliciesAdminPage').then((m) => ({ default: m.AgentPoliciesAdminPage })),
)
const McpAdministrationPage = lazy(() =>
  import('../features/mcp/pages/McpAdministrationPage').then((m) => ({ default: m.McpAdministrationPage })),
)
const McpCatalogPage = lazy(() => import('../features/mcp/pages/McpCatalogPage').then((m) => ({ default: m.McpCatalogPage })))
const WorkflowLibraryPage = lazy(() =>
  import('../features/workflows/pages/WorkflowLibraryPage').then((m) => ({ default: m.WorkflowLibraryPage })),
)
const WorkflowDesignerPage = lazy(() =>
  import('../features/workflows/pages/WorkflowDesignerPage').then((m) => ({ default: m.WorkflowDesignerPage })),
)
const WorkflowExecutionPage = lazy(() =>
  import('../features/workflows/pages/WorkflowExecutionPage').then((m) => ({ default: m.WorkflowExecutionPage })),
)
const WorkflowPoliciesAdminPage = lazy(() =>
  import('../features/workflows/pages/WorkflowPoliciesAdminPage').then((m) => ({ default: m.WorkflowPoliciesAdminPage })),
)

// Each route mounts a fresh <Lazy> instance, so Fade's default `in`-from-mount behavior
// gives every route a consistent, theme-timed fade-in (FR-010/SC-007) — no per-route
// wiring, no external animation library, and it collapses to instant under
// prefers-reduced-motion the same way every other themed transition does (theme/index.ts).
function Lazy({ children }: { children: React.ReactNode }) {
  return (
    <Suspense fallback={null}>
      <Fade in appear>
        <div>{children}</div>
      </Fade>
    </Suspense>
  )
}

const router = createBrowserRouter([
  {
    // Public marketing landing page (spec.md FR-001/FR-015): signed-out visitors see it;
    // an already-authenticated visitor is redirected straight into /studio by PublicOnlyRoute.
    path: '/',
    element: (
      <PublicOnlyRoute>
        <Lazy>
          <LandingPage />
        </Lazy>
      </PublicOnlyRoute>
    ),
    errorElement: <ErrorPage />,
  },
  {
    path: '/login',
    element: (
      <Lazy>
        <LoginPage />
      </Lazy>
    ),
    errorElement: <ErrorPage />,
  },
  {
    path: '/register',
    element: (
      <Lazy>
        <RegisterPage />
      </Lazy>
    ),
    errorElement: <ErrorPage />,
  },
  {
    path: '/confirm-email',
    element: (
      <Lazy>
        <ConfirmEmailPage />
      </Lazy>
    ),
    errorElement: <ErrorPage />,
  },
  {
    path: '/confirm-email-change',
    element: (
      <Lazy>
        <ConfirmEmailChangePage />
      </Lazy>
    ),
    errorElement: <ErrorPage />,
  },
  {
    path: '/auth/external-complete',
    element: (
      <Lazy>
        <ExternalLoginCompletePage />
      </Lazy>
    ),
    errorElement: <ErrorPage />,
  },
  {
    // Public — reachable pre-login (spec.md FR-009/FR-010), outside ProtectedRoute.
    path: '/privacy',
    element: (
      <Lazy>
        <PrivacyPage />
      </Lazy>
    ),
    errorElement: <ErrorPage />,
  },
  {
    // SPEC-024 FR-001: the authenticated workspace, renamed from "Chat" to "Studio".
    path: '/studio',
    element: (
      <ProtectedRoute>
        <Lazy>
          <ChatPage />
        </Lazy>
      </ProtectedRoute>
    ),
    errorElement: <ErrorPage />,
  },
  {
    // SPEC-024 FR-002/SC-005: existing /chat bookmarks and shared links keep working —
    // `replace` so /chat never lingers one back-button-press away in history.
    path: '/chat',
    element: <Navigate to="/studio" replace />,
  },
  {
    path: '/documents',
    element: (
      <ProtectedRoute>
        <Lazy>
          <DocumentWorkspacePage />
        </Lazy>
      </ProtectedRoute>
    ),
    errorElement: <ErrorPage />,
  },
  {
    path: '/knowledge-bases',
    element: (
      <ProtectedRoute>
        <Lazy>
          <KnowledgeBaseDashboardPage />
        </Lazy>
      </ProtectedRoute>
    ),
    errorElement: <ErrorPage />,
  },
  {
    path: '/knowledge-bases/:id',
    element: (
      <ProtectedRoute>
        <Lazy>
          <KnowledgeBaseDetailPage />
        </Lazy>
      </ProtectedRoute>
    ),
    errorElement: <ErrorPage />,
  },
  {
    path: '/memory',
    element: (
      <ProtectedRoute>
        <Lazy>
          <MemoryCenterPage />
        </Lazy>
      </ProtectedRoute>
    ),
    errorElement: <ErrorPage />,
  },
  {
    path: '/prompts',
    element: (
      <ProtectedRoute>
        <Lazy>
          <PromptLibraryPage />
        </Lazy>
      </ProtectedRoute>
    ),
    errorElement: <ErrorPage />,
  },
  {
    path: '/prompts/new',
    element: (
      <ProtectedRoute>
        <Lazy>
          <PromptEditorPage />
        </Lazy>
      </ProtectedRoute>
    ),
    errorElement: <ErrorPage />,
  },
  {
    path: '/prompts/:id',
    element: (
      <ProtectedRoute>
        <Lazy>
          <PromptEditorPage />
        </Lazy>
      </ProtectedRoute>
    ),
    errorElement: <ErrorPage />,
  },
  {
    path: '/agents',
    element: (
      <ProtectedRoute>
        <Lazy>
          <AgentLibraryPage />
        </Lazy>
      </ProtectedRoute>
    ),
    errorElement: <ErrorPage />,
  },
  {
    path: '/agents/new',
    element: (
      <ProtectedRoute>
        <Lazy>
          <AgentBuilderPage />
        </Lazy>
      </ProtectedRoute>
    ),
    errorElement: <ErrorPage />,
  },
  {
    path: '/agents/:id',
    element: (
      <ProtectedRoute>
        <Lazy>
          <AgentBuilderPage />
        </Lazy>
      </ProtectedRoute>
    ),
    errorElement: <ErrorPage />,
  },
  {
    path: '/agents/:agentId/executions/:executionId',
    element: (
      <ProtectedRoute>
        <Lazy>
          <AgentExecutionPage />
        </Lazy>
      </ProtectedRoute>
    ),
    errorElement: <ErrorPage />,
  },
  {
    path: '/workflows',
    element: (
      <ProtectedRoute>
        <Lazy>
          <WorkflowLibraryPage />
        </Lazy>
      </ProtectedRoute>
    ),
    errorElement: <ErrorPage />,
  },
  {
    path: '/workflows/:id',
    element: (
      <ProtectedRoute>
        <Lazy>
          <WorkflowDesignerPage />
        </Lazy>
      </ProtectedRoute>
    ),
    errorElement: <ErrorPage />,
  },
  {
    path: '/workflows/:workflowId/executions/:executionId',
    element: (
      <ProtectedRoute>
        <Lazy>
          <WorkflowExecutionPage />
        </Lazy>
      </ProtectedRoute>
    ),
    errorElement: <ErrorPage />,
  },
  {
    path: '/profile',
    element: (
      <ProtectedRoute>
        <Lazy>
          <ProfilePage />
        </Lazy>
      </ProtectedRoute>
    ),
    errorElement: <ErrorPage />,
  },
  {
    path: '/settings',
    element: (
      <ProtectedRoute>
        <Lazy>
          <SettingsPage />
        </Lazy>
      </ProtectedRoute>
    ),
    errorElement: <ErrorPage />,
  },
  {
    path: '/admin/dashboard',
    element: (
      <ProtectedRoute>
        <AdminRoute>
          <Lazy>
            <AdminDashboardPage />
          </Lazy>
        </AdminRoute>
      </ProtectedRoute>
    ),
    errorElement: <ErrorPage />,
  },
  {
    path: '/admin/users',
    element: (
      <ProtectedRoute>
        <AdminRoute>
          <Lazy>
            <AdminUsersPage />
          </Lazy>
        </AdminRoute>
      </ProtectedRoute>
    ),
    errorElement: <ErrorPage />,
  },
  {
    path: '/admin/ai-providers',
    element: (
      <ProtectedRoute>
        <AdminRoute>
          <Lazy>
            <AdminAiProvidersPage />
          </Lazy>
        </AdminRoute>
      </ProtectedRoute>
    ),
    errorElement: <ErrorPage />,
  },
  {
    path: '/admin/ai-capabilities',
    element: (
      <ProtectedRoute>
        <AdminRoute>
          <Lazy>
            <AdminAiCapabilitiesPage />
          </Lazy>
        </AdminRoute>
      </ProtectedRoute>
    ),
    errorElement: <ErrorPage />,
  },
  {
    path: '/admin/agent-policies',
    element: (
      <ProtectedRoute>
        <AdminRoute>
          <Lazy>
            <AgentPoliciesAdminPage />
          </Lazy>
        </AdminRoute>
      </ProtectedRoute>
    ),
    errorElement: <ErrorPage />,
  },
  {
    path: '/admin/workflow-policies',
    element: (
      <ProtectedRoute>
        <AdminRoute>
          <Lazy>
            <WorkflowPoliciesAdminPage />
          </Lazy>
        </AdminRoute>
      </ProtectedRoute>
    ),
    errorElement: <ErrorPage />,
  },
  {
    path: '/admin/mcp-servers',
    element: (
      <ProtectedRoute>
        <AdminRoute>
          <Lazy>
            <McpAdministrationPage />
          </Lazy>
        </AdminRoute>
      </ProtectedRoute>
    ),
    errorElement: <ErrorPage />,
  },
  {
    path: '/mcp/catalog',
    element: (
      <ProtectedRoute>
        <Lazy>
          <McpCatalogPage />
        </Lazy>
      </ProtectedRoute>
    ),
    errorElement: <ErrorPage />,
  },
  { path: '*', element: <ErrorPage /> },
])

export function AppRouter() {
  return <RouterProvider router={router} />
}

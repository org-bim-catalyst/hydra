import { lazy, Suspense } from 'react'
import { createBrowserRouter, Navigate, RouterProvider } from 'react-router'
import { ErrorPage } from '../components/ErrorPage'
import { AdminRoute } from './AdminRoute'
import { ProtectedRoute } from './ProtectedRoute'

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

function Lazy({ children }: { children: React.ReactNode }) {
  return <Suspense fallback={null}>{children}</Suspense>
}

const router = createBrowserRouter([
  { path: '/', element: <Navigate to="/chat" replace />, errorElement: <ErrorPage /> },
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
    path: '/chat',
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
  { path: '*', element: <ErrorPage /> },
])

export function AppRouter() {
  return <RouterProvider router={router} />
}

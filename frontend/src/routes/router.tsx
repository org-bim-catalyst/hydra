import { lazy, Suspense } from 'react'
import { createBrowserRouter, Navigate, RouterProvider } from 'react-router'
import { AdminRoute } from './AdminRoute'
import { ProtectedRoute } from './ProtectedRoute'

const LoginPage = lazy(() => import('../features/auth/pages/LoginPage').then((m) => ({ default: m.LoginPage })))
const RegisterPage = lazy(() =>
  import('../features/auth/pages/RegisterPage').then((m) => ({ default: m.RegisterPage })),
)
const ChatPage = lazy(() => import('../features/chat/pages/ChatPage').then((m) => ({ default: m.ChatPage })))
const ProfilePage = lazy(() =>
  import('../features/profile/pages/ProfilePage').then((m) => ({ default: m.ProfilePage })),
)
const AdminUsersPage = lazy(() =>
  import('../features/admin/pages/AdminUsersPage').then((m) => ({ default: m.AdminUsersPage })),
)

function Lazy({ children }: { children: React.ReactNode }) {
  return <Suspense fallback={null}>{children}</Suspense>
}

const router = createBrowserRouter([
  { path: '/', element: <Navigate to="/chat" replace /> },
  {
    path: '/login',
    element: (
      <Lazy>
        <LoginPage />
      </Lazy>
    ),
  },
  {
    path: '/register',
    element: (
      <Lazy>
        <RegisterPage />
      </Lazy>
    ),
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
  },
])

export function AppRouter() {
  return <RouterProvider router={router} />
}

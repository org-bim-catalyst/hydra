import { useQuery } from '@tanstack/react-query'
import * as adminApi from '../api/adminApi'

export function useAdminDashboard() {
  return useQuery({ queryKey: ['admin', 'dashboard', 'summary'], queryFn: adminApi.getDashboardSummary })
}

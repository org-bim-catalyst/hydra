import { useQuery } from '@tanstack/react-query'
import { getCookiePolicy } from '../api/privacyApi'

export function useCookiePolicy() {
  return useQuery({ queryKey: ['cookie-policy'], queryFn: getCookiePolicy })
}

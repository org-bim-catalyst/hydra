import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import * as profileApi from '../api/profileApi'

const PROFILE_QUERY_KEY = ['profile', 'me']

export function useMyProfile() {
  return useQuery({ queryKey: PROFILE_QUERY_KEY, queryFn: profileApi.getMyProfile })
}

export function useUpdateProfile() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ firstName, lastName }: { firstName?: string; lastName?: string }) =>
      profileApi.updateMyProfile(firstName, lastName),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: PROFILE_QUERY_KEY }),
  })
}

export function useUploadAvatar() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (file: File) => profileApi.uploadAvatar(file),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: PROFILE_QUERY_KEY }),
  })
}

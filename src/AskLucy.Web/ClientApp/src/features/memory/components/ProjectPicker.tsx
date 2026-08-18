import { MenuItem, TextField } from '@mui/material'
import { useMemo } from 'react'
import { useProjects } from '../hooks/useProjects'
import { useAssignChatToProject } from '../hooks/useProjectMutations'

const GENERAL_VALUE = ''

interface ProjectPickerProps {
  chatId: string | null
  projectId: string | null
  onAssigned: (projectId: string | null) => void
}

/**
 * spec.md FR-002a, User Story 5 — assigns the current conversation to at most one Project (or
 * back to general scope). Disabled until a conversation actually exists (a brand-new,
 * not-yet-created chat has nothing to assign yet), mirroring `ProviderModelSelector`'s
 * `TextField select` convention.
 */
export function ProjectPicker({ chatId, projectId, onAssigned }: ProjectPickerProps) {
  const { data, isLoading } = useProjects()
  const assignChatToProject = useAssignChatToProject()

  const projects = useMemo(() => data?.pages.flatMap((page) => page.items) ?? [], [data])

  if (isLoading || projects.length === 0) {
    return null
  }

  return (
    <TextField
      select
      size="small"
      label="Project"
      aria-label="Project"
      value={projectId ?? GENERAL_VALUE}
      disabled={!chatId}
      onChange={(e) => {
        if (!chatId) return
        const newProjectId = e.target.value === GENERAL_VALUE ? null : e.target.value
        assignChatToProject.mutate({ chatId, projectId: newProjectId }, { onSuccess: () => onAssigned(newProjectId) })
      }}
      sx={{ minWidth: 160 }}
    >
      <MenuItem value={GENERAL_VALUE}>General (no Project)</MenuItem>
      {projects.map((project) => (
        <MenuItem key={project.id} value={project.id}>
          {project.name}
        </MenuItem>
      ))}
    </TextField>
  )
}

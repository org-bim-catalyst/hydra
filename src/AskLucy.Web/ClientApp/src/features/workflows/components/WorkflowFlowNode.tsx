import { Handle, Position, type NodeProps } from '@xyflow/react'
import { Box, Paper, Stack, Typography, useTheme } from '@mui/material'
import { createElement } from 'react'
import { getNodeCatalogEntry } from '../nodeCatalog'
import type { WorkflowCanvasNode } from '../store/workflowCanvasStore'
import { getCategoryIcon } from './nodeCategoryIcon'

/** Custom `@xyflow/react` node renderer registered as `nodeTypes={{ workflowNode: WorkflowFlowNode }}` on {@link WorkflowCanvas}. */
export function WorkflowFlowNode({ data, selected }: NodeProps<WorkflowCanvasNode>) {
  const theme = useTheme()
  const entry = getNodeCatalogEntry(data.nodeType)
  // createElement rather than a `<Icon />` JSX tag — the looked-up component reference is stable
  // (a plain Record lookup), but the react-hooks/static-components rule can't prove that from a
  // capitalized local variable alone.
  const icon = createElement(getCategoryIcon(entry.category), { fontSize: 'small' })
  const hasInput = data.nodeType !== 'Start'
  const hasOutput = data.nodeType !== 'End'

  return (
    <Paper
      variant="outlined"
      sx={{
        minWidth: 180,
        px: 1.5,
        py: 1,
        borderColor: selected ? 'primary.main' : 'divider',
        borderWidth: selected ? 2 : 1,
        boxShadow: selected ? theme.shadows[3] : theme.shadows[1],
      }}
    >
      {hasInput && <Handle type="target" position={Position.Left} style={{ width: 10, height: 10 }} />}
      <Stack direction="row" spacing={1} sx={{ alignItems: 'center' }}>
        <Box sx={{ display: 'flex', color: 'primary.main' }}>{icon}</Box>
        <Box sx={{ minWidth: 0 }}>
          <Typography variant="body2" noWrap sx={{ fontWeight: 600 }}>
            {data.name}
          </Typography>
          <Typography variant="caption" color="text.secondary" noWrap>
            {entry.label}
          </Typography>
        </Box>
      </Stack>
      {hasOutput && <Handle type="source" position={Position.Right} style={{ width: 10, height: 10 }} />}
    </Paper>
  )
}

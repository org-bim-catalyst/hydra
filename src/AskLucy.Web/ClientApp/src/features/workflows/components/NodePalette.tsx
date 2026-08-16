import {
  Accordion,
  AccordionDetails,
  AccordionSummary,
  Box,
  InputAdornment,
  List,
  ListItem,
  ListItemButton,
  ListItemIcon,
  ListItemText,
  TextField,
  Typography,
} from '@mui/material'
import ExpandMoreIcon from '@mui/icons-material/ExpandMore'
import SearchIcon from '@mui/icons-material/Search'
import { useMemo, useState } from 'react'
import { WORKFLOW_NODE_CATALOG, WORKFLOW_NODE_CATEGORIES, type WorkflowNodeCatalogEntry } from '../nodeCatalog'
import { useWorkflowCanvasStore } from '../store/workflowCanvasStore'
import { getCategoryIcon } from './nodeCategoryIcon'
import { DRAG_DATA_TYPE } from './WorkflowCanvas'

/** spec.md User Story 2 — searchable, categorized node palette. Drag onto the canvas, or click an entry to add it at a default position (keyboard-/screen-reader-accessible alternative to drag-and-drop, per spec.md's "Accessible controls" requirement). */
export function NodePalette() {
  const [search, setSearch] = useState('')
  const addNode = useWorkflowCanvasStore((s) => s.addNode)
  const nodeCount = useWorkflowCanvasStore((s) => s.nodes.length)

  const filtered = useMemo(() => {
    const query = search.trim().toLowerCase()
    if (!query) return WORKFLOW_NODE_CATALOG
    return WORKFLOW_NODE_CATALOG.filter(
      (entry) => entry.label.toLowerCase().includes(query) || entry.description.toLowerCase().includes(query) || entry.category.toLowerCase().includes(query),
    )
  }, [search])

  const byCategory = useMemo(() => {
    const map = new Map<string, WorkflowNodeCatalogEntry[]>()
    for (const category of WORKFLOW_NODE_CATEGORIES) map.set(category, [])
    for (const entry of filtered) map.get(entry.category)?.push(entry)
    return map
  }, [filtered])

  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', height: '100%', minWidth: 0 }}>
      <Box sx={{ p: 1.5 }}>
        <TextField
          size="small"
          fullWidth
          placeholder="Search nodes…"
          aria-label="Search node palette"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          slotProps={{ input: { startAdornment: <InputAdornment position="start"><SearchIcon fontSize="small" /></InputAdornment> } }}
        />
      </Box>
      <Box sx={{ flex: 1, overflowY: 'auto' }}>
        {WORKFLOW_NODE_CATEGORIES.map((category) => {
          const entries = byCategory.get(category) ?? []
          if (entries.length === 0) return null
          const CategoryIcon = getCategoryIcon(category)
          // aria-controls/aria-labelledby are ID references — IDs can't contain spaces.
          const categorySlug = category.toLowerCase().replace(/\s+/g, '-')

          return (
            <Accordion key={category} disableGutters defaultExpanded={Boolean(search) || nodeCount === 0} elevation={0} square>
              <AccordionSummary
                expandIcon={<ExpandMoreIcon />}
                id={`node-palette-panel-${categorySlug}-header`}
                aria-controls={`node-palette-panel-${categorySlug}-content`}
              >
                <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
                  <CategoryIcon fontSize="small" />
                  {/* An accordion toggle label, not a document-outline heading — subtitle2's
                      default <h6> mapping would otherwise create a heading-order violation. */}
                  <Typography variant="subtitle2" component="span">{category}</Typography>
                </Box>
              </AccordionSummary>
              <AccordionDetails
                id={`node-palette-panel-${categorySlug}-content`}
                aria-labelledby={`node-palette-panel-${categorySlug}-header`}
                sx={{ p: 0 }}
              >
                <List dense disablePadding>
                  {entries.map((entry) => (
                    <ListItem key={entry.nodeType} disablePadding>
                      <ListItemButton
                        draggable
                        onDragStart={(e) => {
                          e.dataTransfer.setData(DRAG_DATA_TYPE, entry.nodeType)
                          e.dataTransfer.effectAllowed = 'move'
                        }}
                        onClick={() => addNode(entry.nodeType, { x: 80 + (nodeCount % 5) * 20, y: 80 + (nodeCount % 5) * 20 })}
                        aria-label={`Add ${entry.label} node`}
                        title={entry.description}
                      >
                        <ListItemIcon sx={{ minWidth: 32 }}>
                          <CategoryIcon fontSize="small" />
                        </ListItemIcon>
                        <ListItemText primary={entry.label} secondary={entry.description} slotProps={{ secondary: { noWrap: true } }} />
                      </ListItemButton>
                    </ListItem>
                  ))}
                </List>
              </AccordionDetails>
            </Accordion>
          )
        })}
        {filtered.length === 0 && (
          <Typography variant="body2" color="text.secondary" sx={{ p: 2 }}>
            No nodes match “{search}”.
          </Typography>
        )}
      </Box>
    </Box>
  )
}

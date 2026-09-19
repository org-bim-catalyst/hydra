import PushPinIcon from '@mui/icons-material/PushPin'
import PushPinOutlinedIcon from '@mui/icons-material/PushPinOutlined'
import StarIcon from '@mui/icons-material/Star'
import StarBorderIcon from '@mui/icons-material/StarBorder'
import { Box, Card, CardActionArea, CardContent, Chip, IconButton, Stack, Typography } from '@mui/material'
import { useVirtualizer } from '@tanstack/react-virtual'
import { useRef } from 'react'
import type { PromptListItem } from '../api/promptsApi'

const ROW_HEIGHT = 108

interface VirtualizedPromptListProps {
  prompts: PromptListItem[]
  isFetchingNextPage: boolean
  hasNextPage: boolean | undefined
  onFetchNextPage: () => void
  onOpen: (prompt: PromptListItem) => void
  onToggleFavorite: (prompt: PromptListItem) => void
  onTogglePinned: (prompt: PromptListItem) => void
}

/**
 * Owns `useVirtualizer()` and everything downstream of it, isolated from `PromptLibraryPage` so
 * that page keeps its React Compiler memoization. TanStack Virtual's hook returns functions that
 * cannot be memoized safely, which makes the compiler skip memoizing whichever component calls it
 * (react-hooks/incompatible-library) — confining that to this small, presentation-only leaf means
 * the much larger page around it is unaffected.
 */
export function VirtualizedPromptList({
  prompts,
  isFetchingNextPage,
  hasNextPage,
  onFetchNextPage,
  onOpen,
  onToggleFavorite,
  onTogglePinned,
}: VirtualizedPromptListProps) {
  const listParentRef = useRef<HTMLDivElement>(null)
  // TanStack Virtual's useVirtualizer() returns functions React Compiler cannot memoize
  // safely; isolating it to this leaf (rather than suppressing globally) is the point of
  // this extraction, so the notice here is expected and permanently accepted.
  // eslint-disable-next-line react-hooks/incompatible-library
  const virtualizer = useVirtualizer({
    count: prompts.length,
    getScrollElement: () => listParentRef.current,
    estimateSize: () => ROW_HEIGHT,
    overscan: 8,
  })

  const handleScroll = () => {
    const el = listParentRef.current
    if (!el || isFetchingNextPage || !hasNextPage) return
    if (el.scrollTop + el.clientHeight >= el.scrollHeight - 300) {
      onFetchNextPage()
    }
  }

  return (
    <Box ref={listParentRef} onScroll={handleScroll} data-testid="prompt-list" sx={{ flex: 1, minHeight: 0, overflowY: 'auto' }}>
      <Box sx={{ position: 'relative', height: virtualizer.getTotalSize() }}>
        {virtualizer.getVirtualItems().map((virtualItem) => {
          const prompt = prompts[virtualItem.index]
          return (
            <Box
              key={virtualItem.key}
              data-index={virtualItem.index}
              ref={virtualizer.measureElement}
              sx={{ position: 'absolute', top: 0, left: 0, right: 0, transform: `translateY(${virtualItem.start}px)`, pb: 1.5 }}
            >
              <PromptCard
                prompt={prompt}
                onOpen={() => onOpen(prompt)}
                onToggleFavorite={() => onToggleFavorite(prompt)}
                onTogglePinned={() => onTogglePinned(prompt)}
              />
            </Box>
          )
        })}
      </Box>
    </Box>
  )
}

interface PromptCardProps {
  prompt: PromptListItem
  onOpen: () => void
  onToggleFavorite: () => void
  onTogglePinned: () => void
}

function PromptCard({ prompt, onOpen, onToggleFavorite, onTogglePinned }: PromptCardProps) {
  return (
    <Card data-testid="prompt-card" variant="outlined">
      <Box sx={{ position: 'relative' }}>
        <CardActionArea onClick={onOpen}>
          <CardContent sx={{ pr: 9 }}>
            <Stack direction="row" sx={{ alignItems: 'center', justifyContent: 'space-between' }}>
              <Typography variant="subtitle1">{prompt.name}</Typography>
              <Chip label={prompt.promptType} size="small" />
            </Stack>
            {prompt.description && (
              <Typography variant="body2" color="text.secondary">
                {prompt.description}
              </Typography>
            )}
            {prompt.tags.length > 0 && (
              <Stack direction="row" spacing={1} sx={{ mt: 1 }}>
                {prompt.tags.map((tag) => (
                  <Chip key={tag} label={tag} size="small" variant="outlined" />
                ))}
              </Stack>
            )}
          </CardContent>
        </CardActionArea>

        <Stack direction="row" sx={{ position: 'absolute', top: 4, right: 4 }}>
          <IconButton
            size="small"
            aria-label={prompt.isPinned ? 'Unpin prompt' : 'Pin prompt'}
            onClick={(e) => {
              e.stopPropagation()
              onTogglePinned()
            }}
          >
            {prompt.isPinned ? <PushPinIcon fontSize="small" color="primary" /> : <PushPinOutlinedIcon fontSize="small" />}
          </IconButton>
          <IconButton
            size="small"
            aria-label={prompt.isFavorite ? 'Unfavorite prompt' : 'Favorite prompt'}
            onClick={(e) => {
              e.stopPropagation()
              onToggleFavorite()
            }}
          >
            {prompt.isFavorite ? <StarIcon fontSize="small" color="warning" /> : <StarBorderIcon fontSize="small" />}
          </IconButton>
        </Stack>
      </Box>
    </Card>
  )
}

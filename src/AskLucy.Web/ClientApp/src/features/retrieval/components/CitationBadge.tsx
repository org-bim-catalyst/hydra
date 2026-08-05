import { useState } from 'react'
import LinkIcon from '@mui/icons-material/Link'
import DescriptionIcon from '@mui/icons-material/Description'
import { Chip } from '@mui/material'
import type { Citation } from '../../chat/api/aiApi'
import { CitationViewer } from './CitationViewer'

/**
 * specs/016-rag-semantic-search US1 T056 — a RAG-grounded citation (has `documentChunkId`) opens
 * a {@link CitationViewer} showing the source page/section and passage; a plain, non-RAG
 * citation keeps its original link-out behavior.
 */
export function CitationBadge({ citation }: { citation: Citation }) {
  const [open, setOpen] = useState(false)
  const isRagCitation = citation.documentChunkId != null

  if (!isRagCitation) {
    return (
      <Chip
        size="small"
        icon={<LinkIcon />}
        label={citation.sourceLabel}
        component={citation.sourceReference ? 'a' : 'div'}
        href={citation.sourceReference ?? undefined}
        target={citation.sourceReference ? '_blank' : undefined}
        rel={citation.sourceReference ? 'noopener noreferrer' : undefined}
        clickable={Boolean(citation.sourceReference)}
      />
    )
  }

  const label = citation.pageNumber != null ? `${citation.sourceLabel} · p. ${citation.pageNumber}` : citation.sourceLabel

  return (
    <>
      <Chip size="small" icon={<DescriptionIcon />} label={label} onClick={() => setOpen(true)} clickable />
      <CitationViewer citation={citation} open={open} onClose={() => setOpen(false)} />
    </>
  )
}

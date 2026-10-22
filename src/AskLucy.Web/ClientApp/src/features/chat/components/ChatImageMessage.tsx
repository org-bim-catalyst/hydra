import DownloadIcon from '@mui/icons-material/Download'
import OpenInFullIcon from '@mui/icons-material/OpenInFull'
import { Alert, Box, ButtonBase, IconButton, Snackbar, Tooltip, alpha, type Theme } from '@mui/material'
import { useState } from 'react'
import { apiFetch } from '../../../api/httpClient'
import { resolveSignedUrl } from '../../documents/api/documentsApi'
import { DocumentImage } from '../../documents/components/DocumentImage'
import { CONTENT_VOCABULARY_VERSION } from '../../../viewer/panels/content/blocks'
import { useFloatingPanelStore } from '../../../viewer/panels/store/floatingPanelStore'

/** A picture in a chat bubble: click it to open it in a floating panel, or take it to disk. */
interface ChatImageMessageProps {
  documentId: string
  /** The place (or subject) the picture shows — also the panel's title and the download's name. */
  alt: string
}

/** The panel opens at a size the 1252x1252 snapshots actually read well in. */
const PANEL_SIZE = { width: 620, height: 680 }

/**
 * The two overlaid controls share one look: a small translucent plate, so they stay readable
 * over a pale satellite image as well as over a dark one.
 */
const overlayButtonSx = {
  position: 'absolute' as const,
  top: 6,
  zIndex: 1,
  bgcolor: (theme: Theme) => alpha(theme.palette.background.paper, 0.85),
  color: 'text.primary',
  border: 1,
  borderColor: 'divider',
  '&:hover': { bgcolor: 'background.paper' },
  '&:focus-visible': (theme: Theme) => ({
    outline: `2px solid ${theme.palette.primary.main}`,
    outlineOffset: 2,
  }),
}

/**
 * A name the operating system will accept. The alt text is a place name, and a place name can
 * contain a slash, a colon, or a quote — all of which are illegal (or merely awkward) in a
 * filename on at least one platform.
 */
function safeFileName(base: string): string {
  const cleaned = base.replace(/[\\/:*?"<>|]/g, '-').trim()
  return cleaned.length > 0 ? cleaned : 'image'
}

/** Keeps the server's own extension (`.png` for a snapshot, `.jpg` for a photo) and only replaces the stem. */
function downloadFileName(alt: string, serverFileName: string | undefined): string {
  const extension = serverFileName?.match(/\.[A-Za-z0-9]{1,5}$/)?.[0] ?? '.png'
  return `${safeFileName(alt)}${extension}`
}

/**
 * The picture in a chat bubble, with the two things a user can do with it.
 *
 * <p>
 * <b>Click to open.</b> The picture opens in a floating panel over the viewer, using the platform's
 * existing content-panel vocabulary: an `image` block naming the document, so the panel renders it
 * through the same entitlement-checked download endpoint the bubble itself uses. The panel's id is
 * derived from the document, so clicking the same picture twice refreshes that one panel instead of
 * stacking a second copy of it.
 * </p>
 *
 * <p>
 * <b>Download.</b> The signed URL is its own authorization, so the bytes are fetched directly and
 * saved under a name derived from the place the picture shows rather than the server's internal
 * document name.
 * </p>
 */
export function ChatImageMessage({ documentId, alt }: ChatImageMessageProps) {
  const [error, setError] = useState<string | null>(null)
  const [isDownloading, setIsDownloading] = useState(false)

  const openInPanel = () => {
    useFloatingPanelStore.getState().openPanel({
      requestId: `image-${documentId}`,
      kind: 'content',
      title: alt,
      content: {
        version: CONTENT_VOCABULARY_VERSION,
        blocks: [{ kind: 'image', fileId: documentId, alt }],
      },
      chrome: { defaultSize: PANEL_SIZE },
    })
  }

  const download = async () => {
    setIsDownloading(true)
    try {
      const { url, fileName } = await apiFetch<{ url: string; fileName: string }>(
        `/documents/${documentId}/download`,
      )

      const response = await fetch(resolveSignedUrl(url))
      if (!response.ok) {
        throw new Error(`the download failed with ${response.status}`)
      }

      const blob = await response.blob()
      const objectUrl = URL.createObjectURL(blob)
      const link = document.createElement('a')
      link.href = objectUrl
      link.download = downloadFileName(alt, fileName)
      link.click()
      URL.revokeObjectURL(objectUrl)
    } catch {
      // Never a silent failure: the user asked for a file and did not get one, so say so.
      setError('The image could not be downloaded.')
    } finally {
      setIsDownloading(false)
    }
  }

  return (
    <Box
      sx={{
        position: 'relative',
        display: 'inline-block',
        maxWidth: '100%',
        // The affordance is drawn on hover of the whole picture, not of the overlay itself —
        // the overlay sits above the picture and takes no pointer events, so it is never the
        // element being hovered.
        '&:hover .chat-image-affordance': { opacity: 1 },
      }}
    >
      <ButtonBase
        onClick={openInPanel}
        aria-label={alt}
        title={`Open ${alt}`}
        sx={{
          display: 'block',
          maxWidth: '100%',
          borderRadius: 1,
          overflow: 'hidden',
          cursor: 'pointer',
          '&:focus-visible': (theme) => ({
            outline: `2px solid ${theme.palette.primary.main}`,
            outlineOffset: 2,
          }),
        }}
      >
        <DocumentImage documentId={documentId} alt={alt} />
      </ButtonBase>

      {/*
        Siblings of the button, never children of it: a control inside a control is not valid
        markup, and nesting an IconButton inside a ButtonBase would make one click ambiguous
        between "open" and "download". Both are always visible rather than revealed on hover —
        a picture that opens something must say so, or the click is a guess.
      */}
      <Tooltip title={`Open ${alt}`}>
        <IconButton
          onClick={openInPanel}
          aria-label={`Open ${alt}`}
          size="small"
          sx={{ ...overlayButtonSx, left: 6 }}
        >
          <OpenInFullIcon fontSize="small" />
        </IconButton>
      </Tooltip>

      <Tooltip title={`Download ${alt}`}>
        <IconButton
          onClick={() => void download()}
          disabled={isDownloading}
          aria-label={`Download ${alt}`}
          size="small"
          sx={{ ...overlayButtonSx, right: 6 }}
        >
          <DownloadIcon fontSize="small" />
        </IconButton>
      </Tooltip>

      {/*
        The picture's own hover cue — a soft darkening, so the whole thumbnail reads as
        interactive and not just the two buttons on it.
      */}
      <Box
        aria-hidden
        className="chat-image-affordance"
        sx={{
          position: 'absolute',
          inset: 0,
          opacity: 0,
          transition: (theme) => theme.transitions.create('opacity'),
          pointerEvents: 'none',
          background: 'linear-gradient(180deg, rgba(0,0,0,0.05) 0%, rgba(0,0,0,0.35) 100%)',
        }}
      />

      <Snackbar open={Boolean(error)} autoHideDuration={5000} onClose={() => setError(null)}>
        <Alert severity="error" variant="filled" onClose={() => setError(null)}>
          {error}
        </Alert>
      </Snackbar>
    </Box>
  )
}

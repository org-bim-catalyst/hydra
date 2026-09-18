import { DocumentImage } from '../../../../features/documents/components/DocumentImage'
import type { ImageBlock as ImageBlockData } from '../blocks'

/** contracts/content-vocabulary.md "image" block. `fileId` resolves only through the platform's
 * existing file-access mechanism and its entitlement checks (research D10) — the schema already
 * rejects an external address outright, so this component never receives one to render; it only
 * has to handle "cannot be reached" and "not entitled", which the download endpoint's own
 * authorization surfaces as an ordinary failed request. */
export function ImageBlockRenderer({ block }: { block: ImageBlockData }) {
  return <DocumentImage documentId={block.fileId} alt={block.alt} />
}

import { TableCell, TableRow, Typography } from '@mui/material'

interface TableEmptyRowProps {
  colSpan: number
  /** Names what is absent, e.g. "No policies found." — never a bare "No data". */
  message: string
}

/**
 * Shared "nothing to show" row, the settled counterpart to {@link TableLoadingRow}. A table that
 * renders headers over empty space reads as broken; once the query has resolved, say so.
 *
 * Give the enclosing `<Table>` `height: '100%'` while this row is the only one in the body: the
 * row then stretches over the full container and `vertical-align: middle` centres the message in
 * it, instead of leaving it stranded at the top of an otherwise blank page.
 */
export function TableEmptyRow({ colSpan, message }: TableEmptyRowProps) {
  return (
    <TableRow>
      <TableCell colSpan={colSpan} sx={{ border: 0, py: 4, textAlign: 'center', verticalAlign: 'middle' }}>
        <Typography variant="body2" color="text.secondary">
          {message}
        </Typography>
      </TableCell>
    </TableRow>
  )
}

import DeleteIcon from '@mui/icons-material/Delete'
import { IconButton, List, ListItem, ListItemText, Typography } from '@mui/material'
import { useDeleteTestCase, useTestCases } from '../hooks/usePromptExecution'

interface TestCaseListProps {
  promptId: string
}

/** Saved, reusable test scenarios for a prompt (spec.md FR-043). */
export function TestCaseList({ promptId }: TestCaseListProps) {
  const { data: testCases } = useTestCases(promptId)
  const deleteTestCase = useDeleteTestCase(promptId)

  if (!testCases || testCases.length === 0) {
    return (
      <Typography variant="body2" color="text.secondary">
        No test cases yet — save one from a completed execution in the Test tab.
      </Typography>
    )
  }

  return (
    <List>
      {testCases.map((testCase) => (
        <ListItem
          key={testCase.id}
          secondaryAction={
            <IconButton aria-label={`Delete ${testCase.name}`} onClick={() => deleteTestCase.mutate(testCase.id)}>
              <DeleteIcon fontSize="small" />
            </IconButton>
          }
        >
          <ListItemText primary={testCase.name} secondary={`${testCase.providerKey} · ${testCase.modelKey}`} />
        </ListItem>
      ))}
    </List>
  )
}

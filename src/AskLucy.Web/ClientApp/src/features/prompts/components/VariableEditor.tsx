import DeleteIcon from '@mui/icons-material/Delete'
import { Box, Checkbox, FormControlLabel, IconButton, MenuItem, Stack, TextField, Typography } from '@mui/material'
import type { PromptVariable, PromptVariableType } from '../api/promptsApi'

const VARIABLE_TYPES: PromptVariableType[] = [
  'String',
  'Number',
  'Boolean',
  'Date',
  'Json',
  'Text',
  'File',
  'Conversation',
  'KnowledgeBase',
]

interface VariableEditorProps {
  variables: PromptVariable[]
  onChange: (variables: PromptVariable[]) => void
}

/**
 * Add/edit/remove variable definitions (spec.md FR-010–FR-012). Variables are auto-detected
 * from `{{name}}` placeholders server-side on save (FR-010) — this editor lets the user attach
 * type/required/default/example/validation metadata to each one before saving.
 */
export function VariableEditor({ variables, onChange }: VariableEditorProps) {
  const updateVariable = (index: number, patch: Partial<PromptVariable>) => {
    onChange(variables.map((v, i) => (i === index ? { ...v, ...patch } : v)))
  }

  const removeVariable = (index: number) => {
    onChange(variables.filter((_, i) => i !== index))
  }

  if (variables.length === 0) {
    return (
      <Typography variant="body2" color="text.secondary">
        No variables detected yet — reference a placeholder like <code>{'{{document}}'}</code> in the prompt content
        above and save to auto-detect it here.
      </Typography>
    )
  }

  return (
    <Stack spacing={2}>
      {variables.map((variable, index) => (
        <Box
          key={variable.name}
          data-testid="detected-variable-chip"
          sx={{ border: 1, borderColor: 'divider', borderRadius: 1, p: 2 }}
        >
          <Stack direction="row" spacing={2} sx={{ alignItems: 'flex-start' }}>
            <Stack spacing={1.5} sx={{ flexGrow: 1 }}>
              <Typography variant="subtitle2">{variable.name}</Typography>
              <Stack direction="row" spacing={2}>
                <TextField
                  select
                  label="Type"
                  size="small"
                  value={variable.type}
                  onChange={(e) => updateVariable(index, { type: e.target.value as PromptVariableType })}
                  sx={{ minWidth: 160 }}
                >
                  {VARIABLE_TYPES.map((type) => (
                    <MenuItem key={type} value={type}>
                      {type}
                    </MenuItem>
                  ))}
                </TextField>
                <FormControlLabel
                  control={
                    <Checkbox
                      checked={variable.isRequired}
                      onChange={(e) => updateVariable(index, { isRequired: e.target.checked })}
                    />
                  }
                  label="Required"
                />
              </Stack>
              <TextField
                label="Description"
                size="small"
                fullWidth
                value={variable.description ?? ''}
                onChange={(e) => updateVariable(index, { description: e.target.value || null })}
              />
              <Stack direction="row" spacing={2}>
                <TextField
                  label="Default value"
                  size="small"
                  fullWidth
                  value={variable.defaultValue ?? ''}
                  onChange={(e) => updateVariable(index, { defaultValue: e.target.value || null })}
                />
                <TextField
                  label="Example value"
                  size="small"
                  fullWidth
                  value={variable.exampleValue ?? ''}
                  onChange={(e) => updateVariable(index, { exampleValue: e.target.value || null })}
                />
              </Stack>
            </Stack>
            <IconButton aria-label={`Remove variable ${variable.name}`} onClick={() => removeVariable(index)} size="small">
              <DeleteIcon fontSize="small" />
            </IconButton>
          </Stack>
        </Box>
      ))}
    </Stack>
  )
}

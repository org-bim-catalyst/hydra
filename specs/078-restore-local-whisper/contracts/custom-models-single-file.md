# API Contract Change: Custom Models Single-File Deployment

Amends [specs/072 contracts](../../072-custom-model-deploy/contracts/) and ADR 0016. Research: D11.

## Source preview — `POST /api/v1/admin/custom-models/source-preview`

The response field `ignoredFilePath` is **renamed `filePath`**. Its meaning changes from "named in
the URL but ignored" to "the only file that will be deployed".

```json
{
  "isValid": true,
  "error": null,
  "repositoryId": "ggerganov/whisper.cpp",
  "revision": "main",
  "filePath": "ggml-small.bin",
  "derivedName": "whisper.cpp",
  "nameAvailable": true
}
```

## Submit — `POST /api/v1/admin/custom-models`

The request is unchanged (the source URL carries the file). The response `SubmittedCustomModelDto`
renames `ignoredFilePath` → `filePath`, and the summary gains `sourceFilePath`.

## Deployment job behavior

| Source URL | Deployed |
|---|---|
| `https://huggingface.co/owner/repo` or `…/tree/<rev>` | Every file in the repository (unchanged). |
| `…/resolve/<rev>/<path>` or `…/blob/<rev>/<path>` | Only `<path>`, placed at `<destination>/<path>`. Size cap, reserved-name check and overwrite report cover that one file. |
| The named path is not in the repository at that revision | The deployment ends **Failed** with "The file `<path>` is not in `<repo>` at `<revision>`." |

## List / get

`CustomModelSummaryDto` gains:
- `sourceFilePath: string | null`
- `selectedForLocalWhisper: boolean` — the Custom Models list shows a "Local Whisper" chip, and the
  Remove button is disabled with a tooltip (FR-009b).

## Frontend

`AddCustomModelDialog` changes its notice from "the whole repository is deployed" to "Only
`<filePath>` will be deployed" whenever `filePath` is present.

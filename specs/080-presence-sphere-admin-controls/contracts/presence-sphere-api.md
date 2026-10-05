# Contract: Presence Sphere Settings API

Base: `/api/v1/appearance/presence-sphere`. JSON; Problem Details for errors.

## GET

- **Auth**: any signed-in user.
- **200**:

```json
{ "dotSizeMultiplier": 1.0, "cardFillPercent": 75, "zoomEnabled": false,
  "modifiedBy": null, "modifiedAtUtc": null, "isDefault": true }
```

  `isDefault` is true when nothing has ever been saved. `modifiedBy` is a display name, never an e-mail or id the caller could not otherwise see.
- **401**: not signed in.

## PUT

- **Auth**: permission `admin.appearance.manage`.
- **Body**: `{ "dotSizeMultiplier": 0.8, "cardFillPercent": 70, "zoomEnabled": true }`. All three are required.
- **200**: the saved settings, in the same shape as GET.
- **400**: Problem Details naming the field and the allowed range, for example `dotSizeMultiplier must be between 0.25 and 2.0`. Nothing is saved.
- **401 / 403**: not signed in / lacks the permission.

## Permissions (added to the catalogue)

| Key | Level | Grants |
|---|---|---|
| `admin.appearance.view` | View | Open the Appearance page and see the values and preview (controls read-only) |
| `admin.appearance.manage` | Manage | Save changes |

## Client behaviour contract
- A settings load failure is treated as the defaults above; the failure is captured and logged, not shown.
- A save failure shows an error and leaves the form values untouched.

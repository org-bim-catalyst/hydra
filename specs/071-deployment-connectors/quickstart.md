# Quickstart / Validation: Deployment Connectors (Admin)

Run these in order. Contract: [contracts/admin-connectors.md](contracts/admin-connectors.md).
Entities: [data-model.md](data-model.md).

## 0. Prerequisites

- The migration has been applied:
  `dotnet ef database update -p src/AskLucy.Persistence -s src/AskLucy.Web`
- `PERSISTENCE_TESTS_CONNECTION_STRING` is set for Web.Tests (see the memory note on Web.Tests
  needing a database connection environment variable).
- A reachable FTP target with a writable directory, for the manual checks in §3. The
  production deployment target is already configured in the untracked
  `src/AskLucy.Web/appsettings.Production.json` `Ftp` section — its host, username and
  password are the values to use.

## 1. Automated gates

```bash
dotnet build "Ask Lucy.sln" -warnaserror
dotnet test tests/AskLucy.Domain.Tests  --filter "FullyQualifiedName~Connectors"
dotnet test tests/AskLucy.Application.Tests --filter "FullyQualifiedName~Connectors"
dotnet test tests/AskLucy.Infrastructure.Tests --filter "FullyQualifiedName~Connectors|FullyQualifiedName~CustomModels"
dotnet test tests/AskLucy.Web.Tests --filter "FullyQualifiedName~Connectors|FullyQualifiedName~CustomModels"
dotnet test tests/AskLucy.Domain.Tests  --filter "FullyQualifiedName~Authorization"
dotnet format --verify-no-changes
cd src/AskLucy.Web/ClientApp && npx tsc -b --noEmit && npx eslint . && npx vitest run
```

Expected: everything passes. The **full** vitest suite is run, not only the touched files
(see the memory note on page-level tests missing component changes). The required test cases
are listed in plan.md → Testing strategy.

## 2. The permission area exists

An administrator sees **Connectors** in the admin sidebar, and the Roles screen lists
**View connectors** / **Manage connectors** under a **Connectors** area.

| Check | Expected |
|---|---|
| `AdminPermissionCatalog.All` has 29 entries (27 + 2) | The domain test asserts the count |
| The English and Arabic role catalogues name both permissions and the area | `usePermissionText.test.ts` compares the client catalogue against both catalogues |
| A role holding only `admin.connectors.view` can list connectors but not create one | `PermissionEnforcementMatrixTests` |
| `admin.ai-providers.manage` alone does **not** grant access to connectors | The areas are separate (research D10) |

## 3. End-to-end: a connector that works

1. Open **Admin → Connectors**. The list is empty, with an **Add connector** button.
2. Press **Add connector**. The form shows: name, type, host, port, username, credential,
   remote base path, and the FTP-specific fields (explicit TLS, passive mode) because the
   type declares them.
3. Leave the type as **Ftp**. Fill in the host, port 21, the username and the password from
   the `Ftp` configuration section, and the remote base path (`/hydra`).
4. Press **Test connection**.
   - The result is a success, or a failure with a reason.
   - **A failure names the stage**: authentication rejected, the server does not accept an
     encrypted connection, the host is unreachable, the remote path does not exist, or the
     path cannot be written to. The message never contains the host, the username or the
     password.
5. Press **Save**. The connector appears in the list with its type, its host and a credential
   hint. **The full credential is not shown anywhere.**
6. Change the type to **Ftps** and save. The connection is re-tested before the change is
   stored.

## 4. End-to-end: a connector that fails

1. Add a connector with a **wrong password** and press **Test connection**: the result
   reports that authentication was rejected.
2. Press **Save** anyway: the save is refused, and **the connector is not added to the
   list**. The server tests before it persists — the browser's own test is a convenience,
   not the guarantee.
3. Add a connector pointing at a host that does not exist: the result reports that the host
   is unreachable, and the save is refused the same way.
4. Add a connector with a remote base path that does not exist on the target: the result
   reports that the path is missing.

## 5. The remote target is not disturbed

After the checks in §3 and §4, look at the remote base path.

| Check | Expected |
|---|---|
| The directory exists as before | The test never creates a remote directory |
| No marker file is left behind | The probe writes one small marker and removes it |

## 6. Types are managed from the form

1. In the **Add connector** form, press **+** next to the type dropdown. The dialog offers
   the protocols this server implements (`ftp`, `ftps`) and asks for a name.
2. Add a type named `Staging FTPS` and press **Save**. It appears in the dropdown.
3. Select **Staging FTPS** in a connector and save. The connector's fields are the same —
   the type names a protocol, and the protocol declares the schema.
4. Press **−** next to the dropdown and select **Staging FTPS**: a confirmation appears.
   Confirm, and the type is removed from the dropdown.
5. Create a connector that uses **Staging FTPS**, then try to remove that type again: the
   removal is **refused**, and the reason says the type is still in use.

## 7. The credential is written only through its own endpoint

1. Open a saved connector for editing. **The credential field is not part of the edit
   form** and the stored credential is never returned — only the hint and the rotation
   timestamp.
2. Use the connector's **Set credential** action to rotate it. The new credential is tested
   before it is stored; a wrong one is refused and the previous credential stays.
3. After a successful rotation, the hint changes and the rotation timestamp moves.

## 8. Two administrators editing at once

1. Open a connector for editing in two browser windows.
2. Save it in the first window.
3. Save it in the second window: the save is **refused** with a conflict message, and the
   form shows the connector's **current server-side values** so the change can be reapplied.

## 9. The names are shared and unique

1. Try to save a second connector named like the first: the save is refused, and the
   message names the field.
2. Try to add a second type named like the first: refused the same way.
3. Delete a connector. Its name becomes available again.

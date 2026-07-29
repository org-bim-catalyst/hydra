---
name: "speckit-wrapup"
description: "Commit outstanding work, push to GitHub, close GitHub issues for completed tasks, and delete branches that have merged successfully."
argument-hint: "Optional: a custom commit message, or 'no-cleanup' to skip branch deletion"
compatibility: "Requires a git repository; GitHub-specific steps require a github.com remote and an authenticated gh CLI"
metadata:
  author: "Mustafa Salaheldin"
  source: "custom"
user-invocable: true
disable-model-invocation: false
---


## User Input

```text
$ARGUMENTS
```

You **MUST** consider the user input before proceeding (if not empty). Treat it as either a
commit message override, a scope restriction (e.g. "only push, don't touch branches"), or the
literal flag `no-cleanup` (skip Step 5 entirely).

## Pre-Execution Checks

**Check for extension hooks (before wrapup)**:
- Check if `.specify/extensions.yml` exists in the project root.
- If it exists, read it and look for entries under the `hooks.before_wrapup` key
- If the YAML cannot be parsed or is invalid, skip hook checking silently and continue normally
- Filter out hooks where `enabled` is explicitly `false`. Treat hooks without an `enabled` field as enabled by default.
- For each remaining hook, do **not** attempt to interpret or evaluate hook `condition` expressions:
  - If the hook has no `condition` field, or it is null/empty, treat the hook as executable
  - If the hook defines a non-empty `condition`, skip the hook and leave condition evaluation to the HookExecutor implementation
- When constructing slash commands from hook command names, replace dots (`.`) with hyphens (`-`). For example, `speckit.git.commit` → `/speckit-git-commit`.
- For each executable hook, output the following based on its `optional` flag:
  - **Optional hook** (`optional: true`):
    ```text
    ## Extension Hooks

    **Optional Pre-Hook**: {extension}
    Command: `/{command}`
    Description: {description}

    Prompt: {prompt}
    To execute: `/{command}`
    ```
  - **Mandatory hook** (`optional: false`):
    ```text
    ## Extension Hooks

    **Automatic Pre-Hook**: {extension}
    Executing: `/{command}`
    EXECUTE_COMMAND: {command}

    Wait for the result of the hook command before proceeding to Step 1.
    ```
    After emitting the block above you MUST actually invoke the hook and wait for it to finish before continuing.
- If no hooks are registered or `.specify/extensions.yml` does not exist, skip silently

**Verify this is a git repository**: run `git rev-parse --git-dir`. If it fails, STOP and tell the
user this command requires a git repository.

## Goal

Act as the closing step of a feature's lifecycle, run after `/speckit-implement` (and optionally
`/speckit-converge`) have finished: persist the remaining work, publish it, reconcile GitHub issues
that track finished tasks, and remove branches that have already been fully merged. Invoking this
command **is** the user's explicit authorization to commit and push — do not re-ask for permission
to perform those two actions specifically. Deleting branches is still gated by an explicit
confirmation in Step 5, because it is harder to reverse and can affect state other collaborators see.

## Outline

### 1. Gather repository context

Run in parallel:
- `git status --porcelain=v1` and `git status` (staged/unstaged/untracked files)
- `git diff` and `git diff --staged`
- `git log --oneline -10` (commit message style)
- `git branch --show-current` (current branch)
- `git config --get remote.origin.url` (remote URL — determines if GitHub steps apply)
- `.specify/scripts/powershell/check-prerequisites.ps1 -Json -PathsOnly` from repo root, to get FEATURE_DIR and the path to tasks.md, if this branch corresponds to a spec-kit feature. Treat a failure or missing tasks.md as "no feature context" rather than an error — Steps 4 and parts of Step 5 simply have less to do.

A GitHub remote is any `remote.origin.url` containing `github.com`. Steps 4 (issue closing) only
run against a GitHub remote; Steps 2, 3, and 5 are GitHub-agnostic except where noted.

### 2. Commit outstanding work

If `git status --porcelain` shows no changes, skip to Step 3.

Otherwise, follow the repository's normal commit discipline:
- Never use `git add -A` or `git add .`. Stage the specific files that belong to this feature's work by name.
- Before staging, scan the changed/untracked file list for anything that looks like a secret (`.env`, `appsettings.*.Development.json` with real credentials, `*.pfx`, `credentials.json`, private keys, connection strings with embedded passwords). If found, exclude it from staging and warn the user instead of silently committing it.
- Draft a concise commit message describing *why*, matching the style seen in `git log --oneline -10`. If `$ARGUMENTS` supplied a message, use it verbatim instead of drafting one.
- Commit using a HEREDOC so formatting is preserved, ending with:
  ```
  Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
  ```
- Run `git status` again after committing to confirm the working tree is clean (aside from anything intentionally excluded above).
- If a pre-commit hook fails, fix the underlying issue, re-stage, and create a **new** commit — never `--amend` and never `--no-verify`.

### 3. Push to GitHub

- Determine the upstream: `git rev-parse --abbrev-ref --symbolic-full-name @{u}` (may fail if the branch has never been pushed).
- If there is no upstream, push with `git push -u origin <branch>`. Otherwise `git push`.
- If the push is rejected as non-fast-forward (remote has diverged), **STOP** and report this to the user — do not force-push. Ask whether they want to pull/rebase first.
- Never pass `--force` or `--force-with-lease` unless the user explicitly asks for it in this conversation.

### 4. Close GitHub issues for completed tasks

Skip this entire step if the remote is not GitHub, or if no `tasks.md` was found in Step 1.

1. Parse `tasks.md` for every task line. A completed task matches `- [X]` or `- [x]`; strip the
   checkbox and any `[P]` / `[US#]` markers to recover the task ID (`T` + 3 digits) and description.
2. Confirm `gh auth status` succeeds. If not authenticated, report this and skip the step rather than failing the whole command.
3. Derive `owner/repo` from the remote URL in Step 1 (never operate on any other repository).
4. List candidate issues: `gh issue list --repo <owner/repo> --state open --search "in:title" --json number,title --limit 200`. Match each issue's title against `\bT\d{3}\b` (these issues are created by `/speckit-taskstoissues` with titles like `T001: <description>`).
5. For every **completed** task ID that has a matching **open** issue, close it:
   ```bash
   gh issue close <number> --repo <owner/repo> --comment "Completed by $(git rev-parse --short HEAD) on branch <branch>."
   ```
6. Leave issues open for any task that is not yet marked `[X]`. Report which issues were closed and which task IDs had no matching issue (nothing to do for those — they were never converted via `/speckit-taskstoissues`).

> [!CAUTION]
> UNDER NO CIRCUMSTANCES CLOSE ISSUES IN A REPOSITORY THAT DOES NOT MATCH THE GIT REMOTE.

### 5. Delete branches that have merged successfully

Skip this entire step if `$ARGUMENTS` contains `no-cleanup`.

1. `git fetch --prune origin` so local remote-tracking refs reflect reality (deleted remote branches disappear from `origin/*`).
2. Determine the repository's default branch: `gh repo view --json defaultBranchRef -q .defaultBranchRef.name` if the remote is GitHub, otherwise fall back to `git remote show origin | grep 'HEAD branch'`.
3. Build the **candidate list** — branches that are safe to delete:
   - The current feature branch, **if** it is not the default branch **and** it has a merged pull request: check with `gh pr list --repo <owner/repo> --head <branch> --state merged --json number,mergedAt` (GitHub remotes only).
   - Any other **local** branch fully merged into the default branch: `git branch --merged origin/<default-branch>`, excluding the default branch itself and any of `main`, `master`, `develop`, `dev`, and the branch currently checked out.
4. Never include the default branch or the currently checked-out branch in the candidate list (git also refuses to delete the current branch).
5. **Present the full candidate list to the user and stop for explicit confirmation** before deleting anything — this is a destructive, hard-to-reverse action visible to collaborators if remote branches are involved. Do not proceed on an assumed "yes."
6. On confirmation, for each approved branch:
   - If it's the current branch, `git checkout <default-branch>` first (after pulling latest).
   - Delete locally with `git branch -d <branch>` (the safe form — it refuses if not fully merged, which is an intentional second safety net; do not fall back to `-D` if it refuses, surface the refusal instead).
   - If a same-named branch exists on the remote (`git ls-remote --heads origin <branch>`), delete it with `git push origin --delete <branch>`.
7. Report exactly which branches were deleted (local/remote) and which were skipped or declined.

> [!CAUTION]
> NEVER DELETE THE DEFAULT BRANCH, THE CURRENTLY CHECKED-OUT BRANCH, OR ANY BRANCH THE USER DID NOT APPROVE IN STEP 5.5.

### 6. Check for extension hooks (after wrapup)

Check if `.specify/extensions.yml` exists in the project root.
- If it exists, read it and look for entries under the `hooks.after_wrapup` key
- If the YAML cannot be parsed or is invalid, skip hook checking silently and continue normally
- Filter out hooks where `enabled` is explicitly `false`. Treat hooks without an `enabled` field as enabled by default.
- For each remaining hook, do **not** attempt to interpret or evaluate hook `condition` expressions:
  - If the hook has no `condition` field, or it is null/empty, treat the hook as executable
  - If the hook defines a non-empty `condition`, skip the hook and leave condition evaluation to the HookExecutor implementation
- When constructing slash commands from hook command names, replace dots (`.`) with hyphens (`-`).
- For each executable hook, output the same Optional/Mandatory blocks described in Pre-Execution Checks, and actually invoke mandatory hooks before finishing.
- If no hooks are registered or `.specify/extensions.yml` does not exist, skip silently

## Completion Report

Summarize, in order: what was committed (or "nothing to commit"), whether the push succeeded (and to which branch), how many issues were closed (with their numbers) or that this was skipped and why, and which branches were deleted, skipped, or left pending user confirmation.

## Done When

- [ ] Working tree changes are committed (or confirmed already clean)
- [ ] Current branch is pushed to `origin`, or a divergence was reported without force-pushing
- [ ] Completed tasks' GitHub issues are closed, or the step was cleanly skipped with a stated reason
- [ ] Merged branches are deleted **only** after explicit user confirmation of the candidate list
- [ ] Extension hooks dispatched or skipped according to the rules above
- [ ] Completion reported to the user with a per-step summary

---
name: "speckit-cicd"
description: "Perform a complete Git/GitHub CI/CD workflow including repository synchronization, branch management, intelligent commits, pull request creation, CI monitoring, issue management, merge, release preparation, and repository cleanup."
argument-hint: >
  Optional:
    - Custom commit message
    - Branch name
    - Merge strategy (merge|squash|rebase)
    - no-release
    - no-cleanup
    - no-pr
    - no-merge
    - no-deploy
compatibility: "Requires a git repository. GitHub-specific features require an authenticated GitHub CLI (gh) and a github.com remote."
metadata:
  author: "Mustafa Salaheldin"
  version: "2.0"
  source: "custom"
user-invocable: true
disable-model-invocation: false
---

# Enterprise CI/CD Workflow

This command performs the **entire delivery lifecycle** for a feature.

Unlike `/speckit-wrapup`, this command is responsible for:

- Synchronizing the repository
- Creating feature branches (if required)
- Reviewing repository status
- Creating intelligent commits
- Pushing changes
- Creating Pull Requests
- Monitoring CI
- Fixing CI failures when possible
- Merging approved Pull Requests
- Closing completed Issues
- Creating releases
- Cleaning local and remote branches
- Returning repository to a clean state

This command is intended to be the final command executed after:

```
/speckit-specify
/speckit-plan
/speckit-tasks
/speckit-implement
```

---

# User Input

```text
$ARGUMENTS
```

Arguments may contain:

• custom commit message

• branch name

• merge strategy

• release version

• workflow options

Examples

```
/speckit-cicd

/speckit-cicd "Improve authentication"

 /speckit-cicd squash

 /speckit-cicd no-release

 /speckit-cicd no-cleanup

 /speckit-cicd feature/T032-authentication
```

---

# Configuration

The workflow should automatically detect repository configuration.

Configuration precedence:

1. Repository configuration

```
.specify/config.yml
```

2. Extension configuration

```
.specify/extensions.yml
```

3. GitHub repository settings

4. Built-in defaults

Supported repository policies:

- GitHub Flow
- Git Flow
- Trunk Based Development
- Feature Branch Workflow

Supported merge strategies

- Squash Merge
- Merge Commit
- Rebase Merge

Supported release types

- None
- Semantic Version
- Calendar Version
- Manual

---

# Core Principles

Always follow these principles.

## Safety First

Never execute destructive Git commands unless explicitly authorized.

Never use:

```
git push --force

git push --force-with-lease

git reset --hard

git clean -fd

git branch -D
```

unless the user specifically requests them.

---

## Protected Branches

Protected branches include:

- main
- master
- develop
- dev
- production
- release

Never commit directly to protected branches unless repository policy explicitly allows it.

---

## Repository Integrity

Never leave the repository in an inconsistent state.

At completion:

✓ no unfinished merges

✓ no unresolved conflicts

✓ clean working tree

✓ synchronized with origin

✓ correct upstream

✓ no detached HEAD

---

# Phase 0 — Extension Hooks

Before beginning any workflow:

Check for

```
.specify/extensions.yml
```

If present

Execute enabled

```
hooks.before_cicd
```

using the same hook execution rules defined by Spec Kit.

Ignore invalid YAML.

Ignore disabled hooks.

Skip silently if none exist.

Mandatory hooks must complete successfully before continuing.

Optional hooks should be offered to the user.

---

# Phase 1 — Repository Validation

Stop immediately if:

- current directory is not a Git repository
- Git executable is unavailable
- repository is corrupt

Run

```
git rev-parse --git-dir
git fsck --no-progress
```

Verify

✓ Git repository

✓ HEAD exists

✓ current branch valid

✓ repository healthy

---

# Phase 2 — Repository Synchronization

Run in parallel:

```
git fetch --all --prune

git remote update

git status

git branch --show-current

git remote -v

git remote show origin

git log --oneline -20
```

Determine

- current branch

- default branch

- upstream branch

- ahead/behind status

- remote provider

Supported providers

- GitHub

- Azure DevOps

- GitLab

- Bitbucket

GitHub-specific features should only execute when origin points to github.com.

---

# Phase 3 — Synchronize with Default Branch

Determine the repository default branch.

Preferred order

1.

```
gh repo view --json defaultBranchRef
```

2.

```
git remote show origin
```

3.

Fallback

```
main
```

Fetch latest default branch.

If current branch is behind:

Present options

- Merge
- Rebase
- Continue without syncing

Never automatically rebase without repository policy allowing it.

If merge conflicts occur:

Pause workflow.

Assist user until conflicts are resolved.

Resume from this phase.
---
# Phase 4 — Feature Branch Management

Determine whether the current branch is appropriate for development.

Protected branches include

- main
- master
- develop
- dev
- production
- release

If currently on a protected branch:

1. Determine whether the repository uses Feature Branch Workflow.

2. Attempt to determine feature identifier from:

- current spec-kit feature
- tasks.md
- issue number
- user arguments

Generate a branch name using repository conventions.

Examples

```
feature/T023-authentication

feature/user-profile

bugfix/T104-login-timeout

hotfix/payment-crash

release/v2.3.0
```

If a branch with the same name already exists:

```
git checkout <branch>
```

Otherwise

```
git checkout -b <branch>
```

Set upstream when first pushed.

Never continue implementation work directly on protected branches unless repository policy explicitly permits it.

---

# Phase 5 — Repository Analysis

Collect repository information.

Run in parallel

```
git status --porcelain=v1

git status

git diff

git diff --staged

git ls-files --others --exclude-standard

git log --oneline -20
```

Determine

- staged files

- unstaged files

- untracked files

- renamed files

- deleted files

- binary files

- generated files

Generate a repository summary.

Example

```
Modified:
  src/Auth/Login.cs

Added:
  src/Auth/JwtService.cs

Deleted:
  LegacyAuth.cs

Untracked:
  docs/auth.md
```

---

# Phase 6 — Secret Detection

Before staging any file

Scan every changed file for potential secrets.

Examples

```
.env

.env.*

*.pfx

*.pem

*.key

credentials.json

secrets.json

appsettings.Development.json

appsettings.Local.json

id_rsa

id_ed25519
```

Also inspect modified files for

- API keys

- OAuth secrets

- JWT secrets

- Azure connection strings

- AWS credentials

- GCP credentials

- Database passwords

- SMTP credentials

- Private certificates

If detected

DO NOT stage the file.

Warn the user.

Continue with remaining files.

Never commit secrets automatically.

---

# Phase 7 — Intelligent File Classification

Classify every changed file.

Categories

## Source

Application code.

## Tests

Unit tests

Integration tests

Playwright

Benchmark

## Documentation

Markdown

Specifications

Architecture

README

CHANGELOG

## Configuration

YAML

JSON

Docker

GitHub Actions

Terraform

## Assets

Images

Icons

SVG

Static resources

## Generated

Build output

Coverage

node_modules

bin

obj

dist

out

Ignore generated files unless repository policy explicitly includes them.

---

# Phase 8 — Intelligent Staging

Never execute

```
git add .

git add -A
```

Instead

Stage only relevant files.

Group logically related files together.

Example

Commit 1

```
Authentication implementation

AuthService.cs

JwtService.cs

LoginController.cs
```

Commit 2

```
Documentation

README.md

docs/authentication.md
```

Commit 3

```
Tests

AuthTests.cs

JwtTests.cs
```

If unrelated work exists

Offer the user

- create multiple commits

- stash unrelated work

- continue with single commit

---

# Phase 9 — Commit Message Generation

If the user supplied a commit message

Use it exactly.

Otherwise

Generate an intelligent Conventional Commit.

Supported prefixes

```
feat

fix

docs

style

refactor

perf

test

build

ci

chore

revert
```

Examples

```
feat(auth): add JWT authentication

fix(api): resolve null reference in login endpoint

docs(spec): update authentication workflow

refactor(core): simplify dependency injection

test(auth): add integration tests
```

The subject should

- be imperative

- under 72 characters

- explain WHY rather than WHAT

Body should summarize

- major implementation

- breaking changes

- migration notes

Footer

```
Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
```

Include issue references whenever possible

```
Closes #41

Refs #52
```

---

# Phase 10 — Commit Validation

Before committing

Run repository validation.

Examples

```
dotnet format

npm run lint

pnpm lint

cargo fmt

black

ruff

prettier
```

Execute only tools that exist in the repository.

If formatting changes occur

Restage affected files.

---

# Phase 11 — Create Commit

Create commits using a HEREDOC.

Never use

```
git commit -m
```

for multi-line commits.

Preferred

```
git commit
```

with message body.

After committing

Run

```
git status

git log --oneline -5
```

Verify

✓ commit created

✓ expected files included

✓ working tree clean

If a pre-commit hook fails

Pause.

Read hook output.

Fix issues.

Restage affected files.

Create a NEW commit.

Never use

```
--amend

--no-verify
```

unless explicitly instructed by the user.
---
# Phase 12 — Push Strategy

Determine the upstream branch.

Run

```bash
git rev-parse --abbrev-ref --symbolic-full-name @{u}
```

If no upstream exists

```bash
git push -u origin <current-branch>
```

Otherwise

```bash
git push
```

Never use

```bash
git push --force
```

or

```bash
git push --force-with-lease
```

unless the user explicitly requested it.

---

## Push Failure Handling

If push fails because of

### Authentication

Verify

```
gh auth status
```

Report the authentication issue.

Pause workflow.

---

### Non Fast Forward

Do NOT overwrite remote history.

Present options

- Pull + Merge
- Pull + Rebase
- Cancel

Never choose automatically.

---

### Network Failure

Retry once.

If it still fails

Pause workflow.

---

### Protected Branch

Report repository policy.

Do not attempt to bypass protection.

---

# Phase 13 — Pull Request Management

This phase only applies to GitHub repositories.

Verify

```
gh auth status
```

Determine whether an open Pull Request already exists.

```
gh pr list \
--head <branch> \
--state open
```

If an open Pull Request already exists

Reuse it.

Otherwise

Create a new Pull Request.

---

## PR Title

Generate from

- Conventional Commit
- Feature name
- Issue number

Examples

```
feat(auth): add JWT authentication

fix(api): prevent duplicate login

refactor(core): simplify dependency injection
```

---

## PR Description

Automatically generate

```
## Summary

Describe the implemented feature.

## Changes

•

•

•

## Testing

•

•

## Breaking Changes

None

## Checklist

[x] Tests passed

[x] Documentation updated

[x] Lint passed

[x] Build passed

## Related Issues

Closes #41

Refs #52
```

---

Create PR

```
gh pr create
```

using repository defaults.

Store

- PR number

- URL

- target branch

for later workflow phases.

---

# Phase 14 — Pull Request Validation

Retrieve PR information.

```
gh pr view
```

Collect

- reviewers

- labels

- draft status

- mergeability

- review decision

- status checks

Verify

✓ mergeable

✓ no conflicts

✓ repository policy satisfied

---

# Phase 15 — CI Monitoring

If repository contains GitHub Actions

```
.github/workflows
```

monitor CI automatically.

Retrieve workflow runs.

```
gh run list
```

Determine latest run for current branch.

If running

Wait.

Poll periodically.

Continue until

SUCCESS

FAILURE

or

CANCELLED

---

## Successful CI

Verify

✓ Build passed

✓ Tests passed

✓ Lint passed

✓ Security checks passed

✓ Required checks passed

---

## Failed CI

Retrieve logs.

```
gh run view
```

Determine failure category.

Examples

- Build

- Tests

- Formatting

- Lint

- Security

- Packaging

Summarize failures.

If failures are automatically fixable

Claude should

- modify files

- rerun validation

- commit fixes

- push

- continue monitoring

Repeat until

CI succeeds

or

manual intervention becomes necessary.

---

# Phase 16 — Review Management

If repository requires Pull Request reviews

Retrieve

```
gh pr review
```

and

```
gh pr view
```

Collect

- approvals

- requested changes

- comments

- unresolved conversations

---

## Requested Changes

If review requests changes

Categorize

- bug

- style

- documentation

- architecture

- testing

Implement requested changes when possible.

Commit.

Push.

Return to CI monitoring.

---

## Unresolved Conversations

Do not merge while unresolved conversations remain.

Resolve conversations after corresponding code changes.

---

# Phase 17 — Merge Strategy

Determine repository merge policy.

Supported

- Squash Merge

- Merge Commit

- Rebase Merge

If repository configuration specifies strategy

Use it.

Otherwise

Prefer

Squash Merge.

---

Before merging verify

✓ CI passed

✓ Required approvals obtained

✓ No merge conflicts

✓ Branch up to date

✓ No draft Pull Request

---

Merge using

```
gh pr merge
```

with repository policy.

Never bypass branch protection.

If merge fails

Retrieve failure reason.

Report clearly.

Pause workflow.

---

## Post Merge Verification

Confirm

✓ Pull Request merged

✓ Commit reachable from default branch

✓ Issue references recognized

✓ Merge completed successfully
---
# Phase 18 — Issue Management

This phase applies only when the repository is hosted on GitHub.

Determine the repository from

```bash
git config --get remote.origin.url
```

Never operate on any repository other than the current repository.

---

## Discover Completed Tasks

If a Spec-Kit feature exists

Locate

```
FEATURE/tasks.md
```

Parse every task.

Completed tasks include

```
- [x]

- [X]
```

Extract

- Task ID

- Description

- User Story

- Parent Feature

Example

```
T023
Implement JWT Authentication
```

---

## Locate GitHub Issues

Retrieve all open issues.

```bash
gh issue list \
--state open \
--limit 500 \
--json number,title,labels
```

Match using

Task ID

```
T023
```

Issue number

```
#45
```

Issue title

Repository conventions

---

## Close Completed Issues

For every completed task

If a matching issue exists

Close it.

Example

```bash
gh issue close <number>
```

Comment

```
Completed by

Commit:
<commit>

Branch:
<branch>

Pull Request:
<PR>

Merged into:
<default branch>
```

Never close

- incomplete tasks

- unrelated issues

- issues from another repository

---

## Cross Reference

Verify

Commit

↓

Pull Request

↓

Issue

all reference one another.

Report

Closed

Skipped

Missing

Unmatched

issues.

---

# Phase 19 — Release Management

Skip if

```
no-release
```

was supplied.

Determine repository release strategy.

Supported

Semantic Versioning

Calendar Version

Manual Version

---

## Version Detection

Determine next version.

Priority

Repository configuration

↓

Existing Git tags

↓

CHANGELOG

↓

Conventional Commits

Semantic Version rules

```
feat

→ Minor

fix

→ Patch

BREAKING CHANGE

→ Major
```

Examples

```
v2.3.1

v2.4.0

v3.0.0
```

---

## Generate Changelog

If

```
CHANGELOG.md
```

exists

Append a new section.

Otherwise

Create one.

Include

Version

Date

Summary

Features

Fixes

Breaking Changes

Contributors

Example

```
## v2.4.0

### Features

•

•

### Fixes

•

•

### Breaking Changes

None
```

---

## Create Git Tag

Create an annotated tag.

```bash
git tag -a v2.4.0
```

Push tag

```bash
git push origin v2.4.0
```

Never overwrite existing tags.

---

## GitHub Release

If GitHub is available

Create Release.

```bash
gh release create
```

Include

Title

Release Notes

Changelog

Assets (if any)

Attach generated artifacts when available.

Examples

```
MSI

ZIP

NuGet

Docker image

Executable
```

---

# Phase 20 — Deployment

Skip if

```
no-deploy
```

was supplied.

Detect deployment workflow.

Examples

GitHub Actions

Azure

Docker

Kubernetes

Railway

Render

Vercel

Netlify

---

## Staging Deployment

If repository supports staging

Deploy.

Wait until deployment completes.

Run smoke tests.

Examples

✓ Application starts

✓ Health endpoint returns OK

✓ Database migrations complete

✓ Authentication works

✓ Static assets load

---

## Production Deployment

Only if

Repository policy

AND

Workflow

allow production deployment.

Deploy.

Monitor deployment status.

If deployment fails

Attempt rollback if supported.

Otherwise

Pause workflow.

Report failure.

---

# Phase 21 — Repository Cleanup

Skip if

```
no-cleanup
```

was supplied.

Refresh repository.

```bash
git fetch --all --prune
```

Determine

Current branch

Default branch

Merged branches

Remote branches

---

## Candidate Branches

Candidates include

Current feature branch

Merged feature branches

Merged bugfix branches

Merged hotfix branches

Exclude

main

master

develop

dev

production

release

Current default branch

---

## Cleanup Confirmation

Present

complete candidate list.

Example

```
feature/T021-auth

feature/T033-profile

bugfix/T041-login
```

Require explicit confirmation before deleting.

Never assume.

---

## Delete Branches

For every approved branch

Delete locally

```bash
git branch -d
```

Delete remotely

```bash
git push origin --delete
```

Never use

```bash
git branch -D
```

unless explicitly requested.

---

## Synchronize Repository

Checkout default branch.

```bash
git checkout <default>
```

Pull latest.

```bash
git pull
```

Verify

✓ latest remote

✓ clean working tree

✓ branch synchronized
---
# Phase 22 — Repository Health Verification

Before declaring the workflow complete, perform a comprehensive repository health audit.

Run in parallel

```bash
git status

git branch

git remote -v

git log --oneline -5

git tag --sort=-creatordate | head

git fsck --no-progress
```

Verify

✓ Working tree clean

✓ No staged changes

✓ No unstaged changes

✓ No untracked files (unless intentionally ignored)

✓ HEAD attached to expected branch

✓ Upstream configured

✓ Local synchronized with origin

✓ Repository integrity valid

If any verification fails

Pause workflow.

Explain the problem.

Recommend corrective actions.

---

# Phase 23 — CI/CD Artifact Verification

If the repository produces build artifacts

Verify that expected outputs exist.

Examples

```
bin/

publish/

dist/

artifacts/

coverage/

TestResults/
```

Supported artifact types

- Executables
- MSI installers
- NuGet packages
- Docker images
- ZIP packages
- Documentation
- API specifications
- Coverage reports

Verify

✓ Artifact exists

✓ Build succeeded

✓ Version matches release

✓ No unexpected files

If artifacts are missing

Report the issue.

Do not silently continue.

---

# Phase 24 — Recovery & Rollback

If any phase fails after changes have been pushed

Determine the furthest successfully completed phase.

Never leave the repository in a partially completed state without informing the user.

Recovery strategies

### Commit Failed

Restore staged state.

Explain failure.

Retry after corrections.

---

### Push Failed

Keep local commits.

Do not create duplicate commits.

Retry push when appropriate.

---

### Pull Request Failed

Keep branch intact.

Do not create duplicate PRs.

Report failure.

---

### Merge Failed

Leave PR open.

Explain merge conflict or policy restriction.

Do not attempt force merge.

---

### Release Failed

Keep Git tag only if successfully pushed.

If release creation failed after tag creation

Report inconsistency.

Allow user to retry.

---

### Deployment Failed

If rollback exists

Execute rollback.

Otherwise

Pause workflow.

Summarize deployment failure.

---

# Phase 25 — Failure Handling Rules

Never ignore errors.

Every failure must be classified.

Categories

- Authentication

- Authorization

- Repository Policy

- Merge Conflict

- Build Failure

- Test Failure

- CI Failure

- Network

- GitHub API

- Configuration

- Unknown

Each failure report should include

Cause

Impact

Recommended Action

Can Continue (Yes/No)

---

# Phase 26 — Extension Hooks (After CI/CD)

Check for

```
.specify/extensions.yml
```

Execute enabled

```
hooks.after_cicd
```

Follow Spec Kit hook rules.

Ignore

- invalid YAML

- disabled hooks

Execute mandatory hooks automatically.

Offer optional hooks to the user.

Wait for mandatory hooks to finish before continuing.

---

# Phase 27 — Completion Report

Generate a structured report.

Example

```
Repository

✓ Valid

Synchronization

✓ Up to date

Branch

feature/T023-authentication

Commit

✓ Created

Commit SHA

abc1234

Push

✓ Successful

Pull Request

#82

Merged

✓ Squash Merge

CI

✓ Passed

Tests

245 Passed

0 Failed

Coverage

92%

Issues Closed

#41

#52

Release

v2.4.0

Deployment

✓ Staging

✓ Production

Cleanup

✓ Remote branch deleted

✓ Local branch deleted

Repository

✓ Clean

Workflow

Completed Successfully
```

---

# Phase 28 — Success Criteria

The workflow is complete only when ALL applicable conditions are satisfied.

Repository

- [ ] Valid Git repository
- [ ] Repository integrity verified
- [ ] Working tree clean

Synchronization

- [ ] Repository synchronized
- [ ] Latest default branch fetched
- [ ] No unresolved conflicts

Branch

- [ ] Feature branch created or reused
- [ ] Upstream configured

Commit

- [ ] Intelligent Conventional Commit created
- [ ] Secrets excluded
- [ ] Commit verified

Push

- [ ] Push successful
- [ ] No force push used

Pull Request

- [ ] PR exists
- [ ] PR validated
- [ ] Reviews completed

Continuous Integration

- [ ] Build passed
- [ ] Tests passed
- [ ] Lint passed
- [ ] Required checks passed

Merge

- [ ] PR merged
- [ ] Default branch updated

Issues

- [ ] Completed issues closed
- [ ] References verified

Release

- [ ] Version generated
- [ ] Changelog updated
- [ ] Tag created
- [ ] Release published

Deployment

- [ ] Staging verified
- [ ] Production verified (if enabled)

Cleanup

- [ ] Local branches cleaned
- [ ] Remote branches cleaned
- [ ] Repository synchronized

Hooks

- [ ] Before hooks completed
- [ ] After hooks completed

Completion

- [ ] Repository returned to clean state
- [ ] Summary reported
- [ ] Workflow completed successfully

---

# Operating Principles

Throughout the entire workflow, the assistant SHALL:

- Prefer safety over automation.
- Never destroy history without explicit user approval.
- Never bypass branch protection rules.
- Never expose secrets in commits, logs, or PRs.
- Follow the repository's configured branching and merge strategy.
- Reuse existing branches and pull requests when appropriate.
- Produce Conventional Commits unless the user explicitly overrides them.
- Ensure every commit, PR, issue, release, and deployment is traceable.
- Stop immediately when manual intervention is required and provide a clear recovery path.
- Leave the repository in a deterministic, reproducible, and fully synchronized state.
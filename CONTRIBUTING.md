# Contributing to Ask Lucy

## Branch protection (tasks.md T063)

`master` MUST be protected so no change reaches it without passing CI (constitution
§12/§16). Configure this once in the GitHub repository settings (Settings → Branches →
Branch protection rules → `master`), or via `gh`:

```sh
gh api repos/mustafasalahuldin/Ask-Lucy/branches/master/protection \
  --method PUT \
  --input - <<'JSON'
{
  "required_status_checks": {
    "strict": true,
    "contexts": [
      "Backend build, lint, and test (.NET 10)",
      "Frontend build, lint, and test (React 19 + Vite)"
    ]
  },
  "enforce_admins": true,
  "required_pull_request_reviews": {
    "required_approving_review_count": 1
  },
  "restrictions": null
}
JSON
```

This requires both CI jobs from `.github/workflows/ci.yml` to pass and at least one
approving review before a pull request can merge to `master` — matching constitution
§11 (code review) and §12 (no failing pipeline may be merged).

**This has not been applied to the live repository** — it's a real change to shared
GitHub settings visible to every contributor, so it needs your explicit go-ahead before
being run against `mustafasalahuldin/Ask-Lucy`.

## Required repository secrets/variables (for the `deploy` job in `ci.yml`)

| Name | Type | Purpose |
|---|---|---|
| `SITE4NOW_FTP_SERVER` | Secret | FTP host for the `site4now.net` hosting target |
| `SITE4NOW_FTP_USERNAME` | Secret | FTP username |
| `SITE4NOW_FTP_PASSWORD` | Secret | FTP password |
| `E2E_BASE_URL` | Variable | Base URL of a real deployment, for the (currently disabled) Playwright job |
| `E2E_TEST_EMAIL` / `E2E_TEST_PASSWORD` | Secrets | Credentials for a seeded E2E test account |

Configure these under Settings → Secrets and variables → Actions. Never commit them to
source control (constitution §8).

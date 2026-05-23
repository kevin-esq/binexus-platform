# GitHub Rulesets

This folder contains the **declarative** definition of every repository ruleset (the modern protection system, NOT the legacy "branch protection rules").

| File                   | Target                  | Purpose                                                                                                                            |
| ---------------------- | ----------------------- | ---------------------------------------------------------------------------------------------------------------------------------- |
| `main-protection.json` | default branch (`main`) | No deletion, no force push, linear history, PR required, code-owner review, squash-only, required status checks (CI + validators). |

Apply / update / delete with `scripts/apply-rulesets.ps1`:

```powershell
# Create or update every ruleset from disk
pwsh -File scripts/apply-rulesets.ps1 -Action apply

# List rulesets currently on the repo
pwsh -File scripts/apply-rulesets.ps1 -Action list

# Delete a ruleset by name
pwsh -File scripts/apply-rulesets.ps1 -Action delete -Name main-protection
```

The script requires `gh` (GitHub CLI) authenticated with admin permissions on the repo.

## GitHub plan caveats — important

The modern Rulesets API has two separate plan gates that hit personal accounts hard:

1. **Listing/managing rulesets on a private repo** requires GitHub Pro/Team/Enterprise. On Free, the repo must be public, or you get:

   ```txt
   Upgrade to GitHub Pro or make this repository public to enable this feature.
   ```

2. **Pattern-based rules** (`branch_name_pattern`, `tag_name_pattern`, `commit_message_pattern`, `commit_author_email_pattern`, `committer_email_pattern`) are only available on **organization** accounts with GitHub Team or Enterprise Cloud. For personal accounts (Free or Pro) GitHub returns:

   ```txt
   {"message":"Validation Failed","errors":["Invalid rule 'branch_name_pattern': "],"status":"422"}
   ```

   We work around this by moving those checks to a GitHub Actions workflow that is then declared as a **required status check** in `main-protection.json`:

   | Concern                             | Where it lives now                                                                                        |
   | ----------------------------------- | --------------------------------------------------------------------------------------------------------- |
   | Branch name convention              | [`.github/workflows/validate.yml`](../workflows/validate.yml) job `Validate branch name`                  |
   | Conventional Commits in commit list | [`.github/workflows/validate.yml`](../workflows/validate.yml) job `Validate commit messages` (commitlint) |
   | Conventional Commits in PR title    | [`.github/workflows/pr-title.yml`](../workflows/pr-title.yml) job `Conventional Commits`                  |

   These three jobs are listed as required contexts in `main-protection.json`, so a PR with a bad branch name or non-conventional commits cannot merge.

## Rationale

- **`main-protection`** is the only mandatory rule. It guarantees `main` is always green, reviewed, has a clean history, accepts only squash merges, and gates merges behind the required CI + validator checks.
- We use the modern Rulesets API (`/repos/{owner}/{repo}/rulesets`), not the legacy Branch Protection (`/branches/{branch}/protection`). Rulesets compose better, support bypass actors, and are the path forward.
- Pattern enforcement (branch names, commit messages) is done via GitHub Actions because the Rulesets API rejects those rule types for personal accounts. Migrating to an organization with GitHub Team would let us move them back into Rulesets without changing the policy intent.

## Bypass actors

`bypass_actors[].actor_id = 5` is the well-known **Repository admin** role. `actor_type = "RepositoryRole"`. Adjust if you want only specific accounts/teams to bypass.

| `bypass_mode`  | When the bypass applies                                                                                                                                                                |
| -------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `always`       | Bypass for both push and PR creation.                                                                                                                                                  |
| `pull_request` | Bypass only when the change goes through a PR. Used in `main-protection` so the admin can still help unblock through a PR without disabling protections. Direct pushes remain blocked. |

## Editing safely

1. Edit the JSON in this folder.
2. Run `pwsh -File scripts/apply-rulesets.ps1 -Action apply` to push the change.
3. Commit the JSON change in the same PR that changes the policy. The repo and the file always agree.

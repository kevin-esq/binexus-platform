# Security policy

## Supported versions

Binexus Platform is pre-1.0. Security fixes target the latest commit on `main`.

| Version  | Supported |
| -------- | --------- |
| `main`   | yes       |
| < `main` | no        |

## Reporting a vulnerability

1. Do **NOT** open a public GitHub issue.
2. Use [GitHub Security Advisories](https://github.com/kevin-esq/binexus-platform/security/advisories/new) ("Report a vulnerability"), which contacts the maintainers privately.
3. Provide a minimal reproduction, affected commit, and the impact you observe.

Expect an initial response within 5 business days.

## Hardening posture

- All commits to `main` go through CI: typecheck, lint, build, test, CodeQL.
- `main` is protected by a GitHub ruleset (linear history, code-owner review, status checks, no force push, no deletion, no direct pushes).
- Dependencies are scanned weekly by Dependabot and CodeQL.
- Secrets are never committed; `.env` is gitignored and `.env.example` is the documented template.
- The HTTP exception filter redacts authorization headers, cookies, and password-shaped fields from Pino logs.

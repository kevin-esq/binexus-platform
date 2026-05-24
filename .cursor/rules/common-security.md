---
description: 'Security: mandatory checks, secret management, response protocol'
alwaysApply: true
---

# Security Guidelines

## Mandatory Security Checks

Before ANY commit:

- [ ] No hardcoded secrets (API keys, passwords, tokens)
- [ ] All user inputs validated
- [ ] SQL injection prevention (parameterized queries)
- [ ] XSS prevention (sanitized HTML)
- [ ] CSRF protection enabled
- [ ] Authentication/authorization verified
- [ ] Rate limiting on all endpoints
- [ ] Error messages don't leak sensitive data

## Secret Management

- NEVER hardcode secrets in source code
- ALWAYS use environment variables or a secret manager
- Validate that required secrets are present at startup
- Rotate any secrets that may have been exposed

## Security Response Protocol

If a security issue is found:

1. STOP immediately and tell the user
2. Open `typescript-security.md` for stack-specific guidance, or `diagnose` skill if the issue is a live incident
3. Fix CRITICAL issues before continuing other work
4. Rotate any exposed secrets — do NOT just commit a deletion, the secret is in git history
5. Grep the rest of the codebase for the same anti-pattern

---
description: 'Testing requirements: 80% coverage, TDD workflow, test types'
alwaysApply: true
---

# Testing Requirements

## Minimum Test Coverage: 80%

Test Types (ALL required):

1. **Unit Tests** - Individual functions, utilities, components
2. **Integration Tests** - API endpoints, database operations
3. **E2E Tests** - Critical user flows (framework chosen per language)

## Test-Driven Development

MANDATORY workflow:

1. Write test first (RED)
2. Run test - it should FAIL
3. Write minimal implementation (GREEN)
4. Run test - it should PASS
5. Refactor (IMPROVE)
6. Verify coverage (80%+)

## Troubleshooting Test Failures

1. Open the `tdd` skill if behaviour is unclear, or `diagnose` skill if a previously-passing test broke
2. Check test isolation — a failure that only happens in CI or only when other tests run first is almost always shared state
3. Verify mocks at the boundary, not internal modules
4. Fix the implementation, not the test (unless the test encodes an outdated expectation — then update it deliberately and explain why in the commit)

## When to Invoke the `tdd` Skill

Use proactively for new features and every bug fix. The skill enforces write-tests-first.

---
description: 'Common patterns: repository, API response, skeleton projects'
alwaysApply: true
---

# Common Patterns

## Borrowing From Existing Solutions

When implementing new functionality:

1. Search for battle-tested libraries or reference implementations before writing from scratch
2. Evaluate candidates on: security posture, extensibility, license compatibility, maintenance activity, relevance to the domain
3. Prefer composition over forking — pull the dependency, wrap it in a thin adapter, keep your domain code free of vendor types
4. If you must copy code, attribute the source in a comment and keep the structure recognisable so updates upstream can be merged later

## Design Patterns

### Repository Pattern

Encapsulate data access behind a consistent interface:

- Define standard operations: findAll, findById, create, update, delete
- Concrete implementations handle storage details (database, API, file, etc.)
- Business logic depends on the abstract interface, not the storage mechanism
- Enables easy swapping of data sources and simplifies testing with mocks

### API Response Format

Use a consistent envelope for all API responses:

- Include a success/status indicator
- Include the data payload (nullable on error)
- Include an error message field (nullable on success)
- Include metadata for paginated responses (total, page, limit)

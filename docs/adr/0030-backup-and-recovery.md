# ADR-0030: Branch backup and recovery

| Field    | Value          |
| -------- | -------------- |
| Status   | Proposed       |
| Date     | 2026-07-12     |
| Deciders | Kevin Esquivel |

## Context

Branch Server is operational authority. Losing the Principal without backup loses unsynced sales and proofs.

## Decision

### Backup scope

| Asset               | Backup                                                                                                     |
| ------------------- | ---------------------------------------------------------------------------------------------------------- |
| PostgreSQL          | Periodic logical or volume backup via Installer/scheduled task                                             |
| Local proof objects | Include object directory or volume in backup set                                                           |
| Sync checkpoints    | Included in DB backup                                                                                      |
| Secrets             | Export/restore procedure via Credential Manager backup guidance; not plaintext in DB dump tools by default |

### Frequency and retention (design defaults)

- Daily automated backup minimum; operator may increase frequency.
- Retain at least 7 local generations unless disk policy says otherwise.
- Destination: local disk path configurable; optional copy to operator USB/NAS. Cloud backup of Branch DB is optional later, not required for v1 design.

### Restore and sync

1. Restore Postgres + objects onto same or replacement host.
2. Complete Cloud Replace if hardware changed (ADR-0017).
3. Sync Worker resumes from restored checkpoints.
4. Idempotent apply prevents re-uploading already-acked journal entries.
5. If Cloud has newer downstream data than backup, Branch pulls downstream and applies; if Branch backup has unsynced journal entries Cloud lacks, upstream pushes them.
6. If Cloud and restored Branch diverge on the same owned aggregate version, surface admin conflict (ADR-0025). Never silently drop money/stock facts.

### Out of scope for this ADR

Building the backup product UI and cloud DR. This ADR locks the protection requirements.

## Consequences

### Positive

- Explicit restore vs sync relationship.
- Avoids naive "restore then duplicate sales" failure mode.

### Negative / Trade-offs

- Operators must run backups; product must nag on health UI later.
- Replace + restore is a trained procedure.

## Alternatives considered

1. **Rely only on Cloud sync as backup** - Rejected (unsynced gap).
2. **No local object backup** - Rejected for proofs.
3. **Automatic multi-node replication** - Out of scope (ADR-0017).

## Decision outcome

Proposed. Await checkpoint approval before Accepted.

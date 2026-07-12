# ADR-0029: Proof object synchronization

| Field    | Value          |
| -------- | -------------- |
| Status   | Proposed       |
| Date     | 2026-07-12     |
| Deciders | Kevin Esquivel |

## Context

Delivery proofs (photos, signatures) may be large binaries. Delivery must remain valid without internet. MinIO/cloud object layout must not block the stop completion path.

## Decision

### Strategy

```text
Branch stores object locally (or on Branch-controlled object store)
→ sync metadata + binary to Cloud object storage when online
```

When internet is available, Branch may upload directly to Cloud object storage **after** local commit of the delivery fact and local durable copy (or write-through to a Branch-held store that survives reconnect). The delivery operation must not require Cloud object ACK to complete.

Do not require Cloud object availability to mark a stop delivered on Branch.

### Design fields

| Concern                         | Decision                                                                                      |
| ------------------------------- | --------------------------------------------------------------------------------------------- |
| Object key                      | Stable key including tenant/branch/proofId; same key upstream                                 |
| Checksum                        | Required (e.g. SHA-256) before ack                                                            |
| Size                            | Stored in metadata; enforce max size policy                                                   |
| Resume                          | Chunked upload with offset checkpoint                                                         |
| Dedup                           | Same checksum+key → ack without re-store                                                      |
| Encryption                      | TLS in transit; at-rest per store defaults; no extra promise yet                              |
| Local cleanup                   | After Cloud ack + retention window                                                            |
| Retention                       | Branch local retention >= sync retry window; Cloud per tenant policy                          |
| Web before Cloud object arrives | Show proof metadata + `ObjectPendingUpload` / `LastSyncedAt`; placeholder not silent omission |

Metadata sync rides the Sync Journal. Binary sync is a dedicated object pipeline keyed by the same proof id.

## Consequences

### Positive

- Offline delivery stays valid.
- Web can show honest pending state.

### Negative / Trade-offs

- Local disk growth on Principal.
- Two pipelines (metadata journal + binary).

## Alternatives considered

1. **Upload-only to Cloud with no local durable object** - Rejected (breaks offline).
2. **Embed binaries in Sync Journal rows** - Rejected for size.
3. **Client uploads straight to Cloud bypassing Branch** - Rejected for Branch authority.

## Decision outcome

Proposed. Await checkpoint approval before Accepted.

# Changelog

All notable changes to Binexus Platform are documented here. Format follows [Keep a Changelog](https://keepachangelog.com/) with [Conventional Commits](https://www.conventionalcommits.org/) scopes where relevant.

## [Unreleased]

### Added

- **Logistics — presigned proof uploads:** `CreateDeliveryProofUploadCommand`, `POST /logistics/delivery-route-stops/:id/proof-uploads`, `S3StorageService`, tenant-scoped object keys, prefix validation on `ConfirmDeliveryCommand`, SDK `createDeliveryProofUpload`, `/logistics` file pickers + direct MinIO PUT, MinIO CORS in docker-compose.

### Documentation

- **2026-05-28 — PR #34 title correction:** GitHub PR #34 was titled `feat(logistics): add presigned proof uploads` but merged **docs-only**. The code implementation ships in this unreleased slice (see Added above).

## [0.0.0] — product slices on `main` (2026-05-23 → 2026-05-28)

Foundation and operational vertical slices merged via PRs #15–#33. See git history and `docs/domains/` for per-context detail.

| PR      | Summary                                                         |
| ------- | --------------------------------------------------------------- |
| #15     | Orders — create order vertical slice                            |
| #18     | Outbox dispatch + audit log                                     |
| #19–#21 | Orders read UI, approve, cancel                                 |
| #22–#26 | Inventory reservation, stock visibility, adjustments, transfers |
| #27     | Warehouse picking base                                          |
| #28–#31 | Logistics planning, dispatch, confirmation, proof base          |
| #32     | Graphify knowledge graph tooling                                |
| #33     | Cursor skills catalog                                           |
| #34     | Docs — presigned proof upload scope (not implementation)        |

# Changelog

All notable changes to Binexus Platform are documented here. Format follows [Keep a Changelog](https://keepachangelog.com/) with [Conventional Commits](https://www.conventionalcommits.org/) scopes where relevant.

## [Unreleased]

### Added

- **Logistics — presigned proof uploads:** `CreateDeliveryProofUploadCommand`, `POST /logistics/delivery-route-stops/:id/proof-uploads`, `S3StorageService`, tenant-scoped object keys, prefix validation on `ConfirmDeliveryCommand`, SDK `createDeliveryProofUpload`, `/logistics` file pickers + direct MinIO PUT, MinIO CORS in docker-compose.

- **Logistics — MinIO proof hardening:** private dev bucket (no anonymous download), `HeadObject` verification on `confirm-delivery` when proof object keys are sent, S3 client timeouts (3s connect / 5s request). CORS for local browser uploads via `MINIO_API_CORS_ALLOW_ORIGIN` (Community MinIO does not support bucket-level `mc cors set`). Runbook: `docs/runbooks/object-storage.md`.

- **Logistics — failed delivery (#3):** `ReportFailedDeliveryCommand`, `POST /logistics/delivery-route-stops/:id/report-failed-delivery`, `DELIVERY_FAILED` event, stop failure metadata, terminal-stop route completion, Orders `DELIVERY_ATTEMPT_FAILED` pause state, SDK `reportFailedDelivery`, `/logistics` report-failed UI. ADR: [`docs/adr/0011-failed-delivery-order-and-route-completion.md`](docs/adr/0011-failed-delivery-order-and-route-completion.md).

- **Logistics — route liquidation (#4):** `paymentMethod` on `Order`, `LiquidateDeliveryRouteCommand`, `POST /logistics/delivery-routes/:id/liquidate`, `DELIVERY_ROUTE_LIQUIDATED`, `SettleOrderCommand`, `ORDER_SETTLED`, COD hybrid arqueo (B3), feature flag `LIQUIDATION`. ADR: [`docs/adr/0012-route-liquidation-cod-reconciliation.md`](docs/adr/0012-route-liquidation-cod-reconciliation.md).

- **Orders — failed delivery resolution (#3b):** `RequeueFailedDeliveryOrderCommand`, `POST /orders/:id/requeue-for-delivery`, cancel from `DELIVERY_ATTEMPT_FAILED`, Logistics candidate reset `ASSIGNED → READY` and cancel via `ORDER_CANCELLED` handler.

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

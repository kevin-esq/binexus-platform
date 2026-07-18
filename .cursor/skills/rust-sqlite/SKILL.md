---
name: rust-sqlite
description: SQLite in Rust desktop apps — rusqlite vs sqlx vs tauri-plugin-sql, migrations, spawn_blocking, offline outbox, scopes. Use when adding local DB, cache, sync queue, or choosing how the WebView may access SQL.
---

# rust-sqlite

## Default

**Own SQLite in Rust** with `rusqlite` (bundled) behind a repository. Run DB work in `tokio::task::spawn_blocking` or a dedicated worker.

Use `sqlx` when you want async + compile-time checked SQL and are fine with its model.

## Avoid by default

`tauri-plugin-sql` exposing execute/select to the WebView — XSS becomes SQL. Only with tight scopes and strong review.

## Always

- Parameterized queries only
- Migrations versioned and applied on startup (or explicit migrate command)
- WAL mode for desktop concurrency when appropriate
- Transactions for “write local + enqueue outbox”
- Idempotent sync keys (`commandId` / operation id)

## Never

- String-concatenated SQL
- Authoritative POS money/stock only in SQLite (Binexus: server commits)
- Holding DB locks across `.await` on the async mutex incorrectly

## Offline-first sketch

1. Mutate local rows
2. Insert outbox row same transaction
3. Background sync posts to server
4. Mark outbox acked; resolve conflicts per policy

## Binexus

Local DB = cache + draft + outbox. Not Branch Server PostgreSQL.

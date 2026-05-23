# services/

Reserved for future out-of-process microservices (e.g. a dedicated `outbox-dispatcher`, `route-optimizer`, or `sync-gateway`). Empty by design in Phase 0.

Binexus is a **modular monolith** today. A service only graduates here when it has clear independent operational requirements: different scaling profile, separate failure domain, or a different runtime (e.g. Rust for performance-critical work).

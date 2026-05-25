-- Rename OrderState.READY_FOR_ROUTE to READY_FOR_DELIVERY_ROUTE.
-- The bare `Route` name collides with framework routing concepts and the
-- Logistics domain now uses `DeliveryRoute` consistently. This keeps the
-- order state machine aligned with that vocabulary.

ALTER TYPE "OrderState" RENAME VALUE 'READY_FOR_ROUTE' TO 'READY_FOR_DELIVERY_ROUTE';

# Outbox/Inbox — algoritmo transaccional definitivo

**Estado:** Gate 2B  
**Garantía:** at-least-once + handlers idempotentes + atomicidad local PostgreSQL

---

## Estados OutboxMessage

| Estado                  | Significado                                                    |
| ----------------------- | -------------------------------------------------------------- |
| `Pending`               | Persistido; nunca reclamado o lock expirado sin completar      |
| `Processing`            | Worker tiene lock; deliveries en curso                         |
| `Completed`             | Todas las deliveries `Processed` (o sin handlers al snapshot)  |
| `CompletedWithFailures` | Sin trabajo pendiente; al menos una delivery `FailedPermanent` |
| `FailedTransient`       | Error transitorio al **claim** (DB), no por handler individual |
| `FailedPermanent`       | Payload inválido / imposible inicializar                       |

### Semántica de intentos (OutboxMessage.AttemptCount)

Solo cuenta **intentos de claim/reclaim** del mensaje (lock, snapshot, crear deliveries).  
**No** suma reintentos de handlers — esos viven en `EventHandlerDelivery.AttemptCount`.

Un handler `FailedPermanent` **no** promueve el outbox a `FailedPermanent` salvo imposibilidad de continuar; el estado terminal es `CompletedWithFailures`.

---

## Primer claim — respuestas explícitas

### 1. ¿En qué transacción se obtiene el lock?

Una **única transacción PostgreSQL corta** por intento de claim:

```text
BEGIN
→ SELECT id … FOR UPDATE SKIP LOCKED LIMIT 1
→ UPDATE estado/lock + snapshot + INSERT deliveries
→ COMMIT
```

### 2. ¿Cuándo se consulta el registro de handlers?

Dentro de esa misma transacción, **solo si** `InitializedAtUtc IS NULL` (primer claim exitoso del mensaje).

Fuente: `IEventHandlerRegistry.GetHandlersForEvent(eventName)` — lista en memoria del despliegue actual.

### 3. ¿Cuándo se persiste ApplicableHandlerKeys?

En la misma transacción del primer claim, **antes** de crear deliveries, junto con `InitializedAtUtc = now()`.

Inmutable después — handlers nuevos en releases posteriores no alteran el snapshot.

### 4. ¿Cuándo se crean EventHandlerDelivery?

Inmediatamente después del snapshot, en la **misma transacción**, una fila `Pending` por cada handler key.

Si `ApplicableHandlerKeys` está vacío → **no** se crean deliveries; outbox pasa a `Completed` en la misma transacción.

### 5. ¿Cómo se evitan deliveries duplicadas?

| Mecanismo                                   | Rol                                                       |
| ------------------------------------------- | --------------------------------------------------------- |
| `FOR UPDATE SKIP LOCKED`                    | Un solo worker reclama el mensaje para inicializar        |
| `InitializedAtUtc`                          | Segundo worker ve mensaje ya inicializado; no re-snapshot |
| `UNIQUE (tenant_id, event_id, handler_key)` | Imposible duplicar delivery en DB                         |

No se confía en comprobaciones solo en memoria.

---

## Procesamiento por handler (atomicidad local)

Por cada `EventHandlerDelivery` pendiente:

```text
BEGIN TX
→ SELECT delivery FOR UPDATE
→ si Processed → skip
→ Status = Processing
→ ejecutar handler (solo efectos PostgreSQL en esta TX)
→ Status = Processed, ProcessedAtUtc = now
→ evaluar estado outbox
→ COMMIT
```

**Ventana crash:** si el proceso muere después del efecto de negocio pero antes de `COMMIT`, el reclaim re-ejecuta; el handler debe ser idempotente o el efecto debe estar en la misma TX.

**Handlers con red/MinIO (futuro):** sin TX abierta durante la red; state machine con idempotency key externa.

---

## Replay manual (futuro)

Operación admin explícita; no ocurre al registrar handlers nuevos.

---

## Política eventos sin handlers

```text
ApplicableHandlerKeys = []
→ OutboxMessage.Status = Completed
→ CompletedAtUtc = now
```

No permanece en `Pending`.

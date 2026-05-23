# Commands and use cases (CQRS-lite)

## Decision

Every use case is modeled as a **Command + Handler**, on top of `@nestjs/cqrs`. We use the "Lite" variant: command bus + handler, no separate query bus (queries go via plain services).

## Why

- **Explicit use cases.** A scrolling list of named commands documents what the system actually does — better than reading 200 controller methods.
- **Single dispatch surface.** HTTP, CLI, scheduled jobs, and event handlers all call `commandBus.execute(new Command(...))`.
- **Easy to wrap.** Logging, metrics, tracing, retries, sagas, and the outbox all hook at the bus level.

## Pieces

| Piece            | Where                                                                                                                      |
| ---------------- | -------------------------------------------------------------------------------------------------------------------------- |
| `AppCommand<T>`  | [`apps/backend/src/common/commands/app-command.ts`](../../apps/backend/src/common/commands/app-command.ts)                 |
| `AppCommandBus`  | [`apps/backend/src/common/commands/command-bus.service.ts`](../../apps/backend/src/common/commands/command-bus.service.ts) |
| `CommandsModule` | [`apps/backend/src/common/commands/commands.module.ts`](../../apps/backend/src/common/commands/commands.module.ts)         |

## Anatomy of a command (Phase 1 sketch)

```ts
// orders/application/commands/create-order.command.ts
export class CreateOrderCommand extends AppCommand<OrderId> {
  constructor(
    readonly input: CreateOrderInput,
    readonly issuedBy: UserId,
  ) {
    super();
  }
}

// orders/application/handlers/create-order.handler.ts
@CommandHandler(CreateOrderCommand)
export class CreateOrderHandler implements ICommandHandler<CreateOrderCommand, OrderId> {
  constructor(
    private readonly prisma: PrismaService,
    private readonly bus: EventBusService,
    private readonly outbox: OutboxService,
  ) {}

  async execute(cmd: CreateOrderCommand): Promise<OrderId> {
    return this.prisma.$transaction(async (tx) => {
      const order = await tx.order.create({
        data: {
          /* ... */
        },
      });
      const event = this.bus.build('ORDER_CREATED', {
        orderId: order.id,
        customerId: order.customerId,
        totalCents: order.totalCents,
        currency: order.currency,
        createdBy: cmd.issuedBy,
      });
      await this.outbox.record(event, tx);
      return order.id as OrderId;
    });
  }
}

// orders/presentation/orders.controller.ts
@Controller('orders')
export class OrdersController {
  constructor(private readonly bus: AppCommandBus) {}

  @Post()
  async create(@Body() dto: CreateOrderDto, @CurrentUser() user: RequestUser) {
    const id = await this.bus.execute(new CreateOrderCommand(dto, user.userId as UserId));
    return { id };
  }
}
```

## Rules

- **One command, one purpose.** No "MultiOpCommand". If two transitions go together, model the workflow with two events linked by `causationId`.
- **Idempotency by default.** Commands accept an `idempotencyKey` when called over the network. Re-running the same key returns the same result, never side-effects twice. (Implementation lands with the first real command.)
- **Commands return IDs or `void`, not entities.** If callers need the entity, they query for it. Keeps the bus output small and the handler focused.
- **No queries in the command bus.** Reads go through plain services or repositories.

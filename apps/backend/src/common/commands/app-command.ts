// Base class for every use case in the platform.
// The generic parameter is the return type of the command's handler.
//
// Example (Phase 1):
//
//   export class CreateOrderCommand extends AppCommand<OrderId> {
//     constructor(readonly input: CreateOrderInput, readonly userId: string) {
//       super();
//     }
//   }
//
//   @CommandHandler(CreateOrderCommand)
//   export class CreateOrderHandler
//     implements ICommandHandler<CreateOrderCommand, OrderId> { ... }

export abstract class AppCommand<TResult = void> {
  // Phantom marker so TS keeps the result type around for the handler typings.
  readonly _result?: TResult;
}

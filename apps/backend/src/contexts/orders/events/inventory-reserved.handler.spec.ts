import { DomainEventName, type DomainEvent } from '@binexus/events';
import { OrderState } from '@prisma/client';
import { describe, expect, it, vi } from 'vitest';

import { type AppCommandBus } from '../../../common/commands/command-bus.service';
import { type PrismaService } from '../../../common/prisma/prisma.service';
import { type SystemUserService } from '../../../common/tenant/system-user.service';
import { type TenantContextService } from '../../../common/tenant/tenant-context.service';
import { MoveOrderToPickingCommand } from '../application/commands/move-order-to-picking.command';

import { InventoryReservedOrdersHandler } from './inventory-reserved.handler';

describe('InventoryReservedOrdersHandler', () => {
  it('moves APPROVED order to picking via system user', async () => {
    const commandBus = { execute: vi.fn().mockResolvedValue({}) };
    const systemUser = { resolveForTenant: vi.fn().mockResolvedValue('system-user-1') };
    const tenantContext = {
      run: vi.fn((_ctx: unknown, fn: () => Promise<void>) => fn()),
    };
    const prisma = {
      forTenant: vi.fn().mockReturnValue({
        order: {
          findFirst: vi.fn().mockResolvedValue({ id: 'order-1', state: OrderState.APPROVED }),
        },
      }),
    };

    const handler = new InventoryReservedOrdersHandler(
      prisma as unknown as PrismaService,
      tenantContext as unknown as TenantContextService,
      systemUser as unknown as SystemUserService,
      commandBus as unknown as AppCommandBus,
    );

    const event: DomainEvent<typeof DomainEventName.INVENTORY_RESERVED> = {
      id: 'event-inv-1',
      name: DomainEventName.INVENTORY_RESERVED,
      tenantId: 'tenant-1',
      occurredAt: '2026-05-25T00:00:00.000Z',
      version: 1,
      payload: { orderId: 'order-1', branchId: 'branch-1', lineCount: 2 },
    };

    await handler.handle(event);

    expect(commandBus.execute).toHaveBeenCalledWith(expect.any(MoveOrderToPickingCommand));
  });

  it('no-ops when order is not APPROVED', async () => {
    const commandBus = { execute: vi.fn() };
    const systemUser = { resolveForTenant: vi.fn().mockResolvedValue('system-user-1') };
    const tenantContext = {
      run: vi.fn((_ctx: unknown, fn: () => Promise<void>) => fn()),
    };
    const prisma = {
      forTenant: vi.fn().mockReturnValue({
        order: {
          findFirst: vi.fn().mockResolvedValue({ id: 'order-1', state: OrderState.PICKING }),
        },
      }),
    };

    const handler = new InventoryReservedOrdersHandler(
      prisma as unknown as PrismaService,
      tenantContext as unknown as TenantContextService,
      systemUser as unknown as SystemUserService,
      commandBus as unknown as AppCommandBus,
    );

    await handler.handle({
      id: 'event-inv-1',
      name: DomainEventName.INVENTORY_RESERVED,
      tenantId: 'tenant-1',
      occurredAt: '2026-05-25T00:00:00.000Z',
      version: 1,
      payload: { orderId: 'order-1', branchId: 'branch-1', lineCount: 2 },
    });

    expect(commandBus.execute).not.toHaveBeenCalled();
  });
});

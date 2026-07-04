import { DomainEventName, type DomainEvent } from '@binexus/events';
import { type BranchId, type OrderId, type UserId } from '@binexus/types';
import { BadRequestException } from '@nestjs/common';
import { OrderState } from '@prisma/client';
import { describe, expect, it, vi } from 'vitest';

import { validateAppCommand } from '../../../../common/commands/command-validation';
import { type EventBusService } from '../../../../common/events/event-bus.service';
import { type OutboxService } from '../../../../common/events/outbox.service';
import { type PrismaService } from '../../../../common/prisma/prisma.service';
import { type TenantContextService } from '../../../../common/tenant/tenant-context.service';

import { CreateOrderCommand, CreateOrderHandler } from './create-order.command';

const tenantContext = {
  tenantId: 'tenant-1',
  userId: 'user-1',
  role: 'ADMIN',
  branchId: 'branch-1',
  requestId: 'request-1',
};

const commandInput = {
  customerId: 'customer-1',
  currency: 'MXN',
  paymentMethod: 'CASH' as const,
  lines: [
    {
      productId: 'product-1',
      productName: 'Coffee',
      quantity: 2,
      unitPriceCents: 1250,
    },
    {
      productId: 'product-2',
      productName: 'Milk',
      quantity: 1,
      unitPriceCents: 3500,
    },
  ],
};

function createHandlerFixture(): {
  handler: CreateOrderHandler;
  tx: {
    branch: { findFirst: ReturnType<typeof vi.fn> };
    order: { create: ReturnType<typeof vi.fn> };
    orderLine: { createMany: ReturnType<typeof vi.fn> };
    orderTransition: { create: ReturnType<typeof vi.fn> };
  };
  eventBus: { build: ReturnType<typeof vi.fn> };
  outbox: { record: ReturnType<typeof vi.fn> };
} {
  const tx = {
    branch: { findFirst: vi.fn().mockResolvedValue({ id: 'branch-1' }) },
    order: { create: vi.fn().mockResolvedValue({ id: 'order-1' }) },
    orderLine: { createMany: vi.fn().mockResolvedValue({ count: 2 }) },
    orderTransition: { create: vi.fn().mockResolvedValue({ id: 'transition-1' }) },
  };

  const prisma = {
    $transaction: vi.fn((callback: (client: typeof tx) => Promise<OrderId>) => callback(tx)),
  } as unknown as PrismaService;

  const tenant = {
    current: vi.fn().mockReturnValue(tenantContext),
  } as unknown as TenantContextService;

  const event: DomainEvent = {
    id: 'event-1',
    name: DomainEventName.ORDER_CREATED,
    tenantId: tenantContext.tenantId,
    occurredAt: '2026-05-23T00:00:00.000Z',
    version: 1,
    payload: {
      orderId: 'order-1',
      customerId: 'customer-1',
      totalCents: 6000,
      currency: 'MXN',
      createdBy: 'user-1',
    },
  };

  const eventBus = {
    build: vi.fn().mockReturnValue(event),
  };
  const outbox = {
    record: vi.fn().mockResolvedValue(undefined),
  };

  return {
    handler: new CreateOrderHandler(
      prisma,
      tenant,
      eventBus as unknown as EventBusService,
      outbox as unknown as OutboxService,
    ),
    tx,
    eventBus,
    outbox,
  };
}

describe('CreateOrderCommand', () => {
  it('validates required input', async () => {
    const command = new CreateOrderCommand({ ...commandInput, lines: [] }, 'user-1' as UserId, {
      commandId: 'cmd-1',
    });

    await expect(validateAppCommand(command)).rejects.toBeInstanceOf(BadRequestException);
  });

  it('requires paymentMethod', async () => {
    const command = new CreateOrderCommand(
      { ...commandInput, paymentMethod: undefined as never },
      'user-1' as UserId,
      { commandId: 'cmd-1' },
    );

    await expect(validateAppCommand(command)).rejects.toBeInstanceOf(BadRequestException);
  });
});

describe('CreateOrderHandler', () => {
  it('creates an order, lines, transition, and outbox event in one transaction', async () => {
    const { handler, tx, eventBus, outbox } = createHandlerFixture();
    const command = new CreateOrderCommand(commandInput, 'user-1' as UserId, {
      commandId: 'cmd-1',
      correlationId: 'corr-1',
    });

    const orderId = await handler.execute(command);

    expect(orderId).toBe('order-1');
    expect(tx.branch.findFirst).toHaveBeenCalledWith({
      where: { id: 'branch-1', tenantId: 'tenant-1' },
      select: { id: true },
    });
    expect(tx.order.create).toHaveBeenCalledWith({
      data: {
        tenantId: 'tenant-1',
        branchId: 'branch-1',
        customerId: 'customer-1',
        state: OrderState.DRAFT,
        paymentMethod: 'CASH',
        totalCents: 6000,
        currency: 'MXN',
        createdByUserId: 'user-1',
      },
      select: { id: true },
    });
    expect(tx.orderLine.createMany).toHaveBeenCalledWith({
      data: [
        {
          tenantId: 'tenant-1',
          orderId: 'order-1',
          productId: 'product-1',
          productName: 'Coffee',
          quantity: 2,
          unitPriceCents: 1250,
          lineTotalCents: 2500,
        },
        {
          tenantId: 'tenant-1',
          orderId: 'order-1',
          productId: 'product-2',
          productName: 'Milk',
          quantity: 1,
          unitPriceCents: 3500,
          lineTotalCents: 3500,
        },
      ],
    });
    expect(tx.orderTransition.create).toHaveBeenCalledWith({
      data: {
        tenantId: 'tenant-1',
        orderId: 'order-1',
        fromState: null,
        toState: OrderState.DRAFT,
        reason: 'Order created',
        byUserId: 'user-1',
      },
    });
    expect(eventBus.build).toHaveBeenCalledWith(
      DomainEventName.ORDER_CREATED,
      {
        orderId: 'order-1',
        customerId: 'customer-1',
        totalCents: 6000,
        currency: 'MXN',
        createdBy: 'user-1',
      },
      { correlationId: 'corr-1', causationId: 'cmd-1' },
    );
    expect(outbox.record).toHaveBeenCalledWith(
      expect.objectContaining({ id: 'event-1', name: DomainEventName.ORDER_CREATED }),
      tx,
    );
  });

  it('rejects a branch outside the current tenant', async () => {
    const { handler, tx } = createHandlerFixture();
    tx.branch.findFirst.mockResolvedValue(null);

    await expect(
      handler.execute(
        new CreateOrderCommand(
          { ...commandInput, branchId: 'branch-other' as BranchId },
          'user-1' as UserId,
          { commandId: 'cmd-1' },
        ),
      ),
    ).rejects.toBeInstanceOf(BadRequestException);
  });
});

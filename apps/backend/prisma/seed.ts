import { PrismaClient, Role } from '@prisma/client';
import argon2 from 'argon2';

const prisma = new PrismaClient();

const ALL_FEATURE_KEYS = [
  'POS_RETAIL',
  'POS_RESTAURANT',
  'ORDERS',
  'INVENTORY',
  'WAREHOUSE_LITE',
  'ROUTES',
  'LIQUIDATION',
  'BILLING',
  'ANALYTICS',
];

async function main(): Promise<void> {
  const slug = process.env.SEED_TENANT_SLUG ?? 'acme';
  const tenantName = process.env.SEED_TENANT_NAME ?? 'Acme Corp';
  const adminEmail = process.env.SEED_ADMIN_EMAIL ?? 'admin@acme.test';
  const adminPassword = process.env.SEED_ADMIN_PASSWORD ?? 'ChangeMe123!';

  console.warn(`[seed] tenant=${slug} admin=${adminEmail}`);

  const tenant = await prisma.tenant.upsert({
    where: { slug },
    update: { name: tenantName },
    create: { slug, name: tenantName },
  });

  const branch =
    (await prisma.branch.findFirst({
      where: { tenantId: tenant.id, name: 'Main' },
    })) ??
    (await prisma.branch.create({
      data: { tenantId: tenant.id, name: 'Main' },
    }));

  const passwordHash = await argon2.hash(adminPassword);

  await prisma.user.upsert({
    where: { tenantId_email: { tenantId: tenant.id, email: adminEmail } },
    update: { role: Role.SUPER_ADMIN, branchId: branch.id },
    create: {
      tenantId: tenant.id,
      email: adminEmail,
      passwordHash,
      role: Role.SUPER_ADMIN,
      branchId: branch.id,
    },
  });

  for (const key of ALL_FEATURE_KEYS) {
    await prisma.tenantFeature.upsert({
      where: { tenantId_key: { tenantId: tenant.id, key } },
      update: {},
      create: { tenantId: tenant.id, key, enabled: false },
    });
  }

  const demoProducts = ['product-demo-1', 'product-demo-2', 'product-demo-3'];
  for (const productId of demoProducts) {
    await prisma.stockItem.upsert({
      where: {
        tenantId_branchId_productId: {
          tenantId: tenant.id,
          branchId: branch.id,
          productId,
        },
      },
      update: { onHand: 1000 },
      create: {
        tenantId: tenant.id,
        branchId: branch.id,
        productId,
        onHand: 1000,
        reserved: 0,
      },
    });
  }

  console.warn('[seed] done');
}

main()
  .catch((err) => {
    console.error('[seed] failed', err);
    process.exit(1);
  })
  .finally(async () => {
    await prisma.$disconnect();
  });

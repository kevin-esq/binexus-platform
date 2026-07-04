-- CreateEnum
CREATE TYPE "PaymentMethod" AS ENUM ('CASH', 'CARD', 'TRANSFER', 'CREDIT');

-- AlterTable
ALTER TABLE "Order" ADD COLUMN     "paymentMethod" "PaymentMethod" NOT NULL DEFAULT 'CASH';

-- CreateTable
CREATE TABLE "DeliveryRouteLiquidation" (
    "id" TEXT NOT NULL,
    "tenantId" TEXT NOT NULL,
    "deliveryRouteId" TEXT NOT NULL,
    "expectedCents" INTEGER NOT NULL,
    "declaredCents" INTEGER NOT NULL,
    "discrepancyCents" INTEGER NOT NULL,
    "currency" TEXT NOT NULL,
    "closedByUserId" TEXT NOT NULL,
    "closedAt" TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "discrepancyReason" TEXT,
    "notes" TEXT,
    "createdAt" TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT "DeliveryRouteLiquidation_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "DeliveryRouteLiquidationLine" (
    "id" TEXT NOT NULL,
    "tenantId" TEXT NOT NULL,
    "deliveryRouteLiquidationId" TEXT NOT NULL,
    "deliveryRouteStopId" TEXT NOT NULL,
    "orderId" TEXT NOT NULL,
    "expectedCents" INTEGER NOT NULL,
    "declaredCents" INTEGER NOT NULL,

    CONSTRAINT "DeliveryRouteLiquidationLine_pkey" PRIMARY KEY ("id")
);

-- CreateIndex
CREATE UNIQUE INDEX "DeliveryRouteLiquidation_deliveryRouteId_key" ON "DeliveryRouteLiquidation"("deliveryRouteId");

-- CreateIndex
CREATE INDEX "DeliveryRouteLiquidation_tenantId_idx" ON "DeliveryRouteLiquidation"("tenantId");

-- CreateIndex
CREATE INDEX "DeliveryRouteLiquidation_tenantId_deliveryRouteId_idx" ON "DeliveryRouteLiquidation"("tenantId", "deliveryRouteId");

-- CreateIndex
CREATE INDEX "DeliveryRouteLiquidationLine_tenantId_deliveryRouteLiquidat_idx" ON "DeliveryRouteLiquidationLine"("tenantId", "deliveryRouteLiquidationId");

-- CreateIndex
CREATE UNIQUE INDEX "DeliveryRouteLiquidationLine_tenantId_deliveryRouteStopId_key" ON "DeliveryRouteLiquidationLine"("tenantId", "deliveryRouteStopId");

-- AddForeignKey
ALTER TABLE "DeliveryRouteLiquidation" ADD CONSTRAINT "DeliveryRouteLiquidation_deliveryRouteId_fkey" FOREIGN KEY ("deliveryRouteId") REFERENCES "DeliveryRoute"("id") ON DELETE CASCADE ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "DeliveryRouteLiquidationLine" ADD CONSTRAINT "DeliveryRouteLiquidationLine_deliveryRouteLiquidationId_fkey" FOREIGN KEY ("deliveryRouteLiquidationId") REFERENCES "DeliveryRouteLiquidation"("id") ON DELETE CASCADE ON UPDATE CASCADE;

-- CreateEnum
CREATE TYPE "StockReservationStatus" AS ENUM ('ACTIVE', 'RELEASED', 'FAILED');

-- CreateEnum
CREATE TYPE "StockMovementType" AS ENUM ('RESERVE', 'RELEASE', 'ADJUSTMENT');

-- CreateTable
CREATE TABLE "StockItem" (
    "id" TEXT NOT NULL,
    "tenantId" TEXT NOT NULL,
    "branchId" TEXT NOT NULL,
    "productId" TEXT NOT NULL,
    "onHand" INTEGER NOT NULL DEFAULT 0,
    "reserved" INTEGER NOT NULL DEFAULT 0,
    "createdAt" TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "updatedAt" TIMESTAMP(3) NOT NULL,

    CONSTRAINT "StockItem_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "StockReservation" (
    "id" TEXT NOT NULL,
    "tenantId" TEXT NOT NULL,
    "orderId" TEXT NOT NULL,
    "orderLineId" TEXT NOT NULL,
    "branchId" TEXT NOT NULL,
    "productId" TEXT NOT NULL,
    "quantity" INTEGER NOT NULL,
    "status" "StockReservationStatus" NOT NULL DEFAULT 'ACTIVE',
    "createdAt" TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "updatedAt" TIMESTAMP(3) NOT NULL,

    CONSTRAINT "StockReservation_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "StockMovement" (
    "id" TEXT NOT NULL,
    "tenantId" TEXT NOT NULL,
    "branchId" TEXT NOT NULL,
    "productId" TEXT NOT NULL,
    "orderId" TEXT,
    "orderLineId" TEXT,
    "type" "StockMovementType" NOT NULL,
    "quantity" INTEGER NOT NULL,
    "correlationId" TEXT,
    "causationId" TEXT,
    "occurredAt" TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT "StockMovement_pkey" PRIMARY KEY ("id")
);

-- CreateIndex
CREATE INDEX "StockItem_tenantId_branchId_idx" ON "StockItem"("tenantId", "branchId");

-- CreateIndex
CREATE UNIQUE INDEX "StockItem_tenantId_branchId_productId_key" ON "StockItem"("tenantId", "branchId", "productId");

-- CreateIndex
CREATE INDEX "StockReservation_tenantId_orderId_idx" ON "StockReservation"("tenantId", "orderId");

-- CreateIndex
CREATE INDEX "StockReservation_tenantId_status_idx" ON "StockReservation"("tenantId", "status");

-- CreateIndex
CREATE UNIQUE INDEX "StockReservation_tenantId_orderId_orderLineId_key" ON "StockReservation"("tenantId", "orderId", "orderLineId");

-- CreateIndex
CREATE INDEX "StockMovement_tenantId_branchId_productId_idx" ON "StockMovement"("tenantId", "branchId", "productId");

-- CreateIndex
CREATE INDEX "StockMovement_tenantId_orderId_idx" ON "StockMovement"("tenantId", "orderId");

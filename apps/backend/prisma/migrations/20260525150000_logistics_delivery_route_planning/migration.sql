-- CreateEnum
CREATE TYPE "DeliveryRouteStatus" AS ENUM ('PLANNED', 'DISPATCHED', 'COMPLETED', 'CANCELLED');

-- CreateEnum
CREATE TYPE "DeliveryRouteStopStatus" AS ENUM ('PLANNED', 'DELIVERED', 'FAILED', 'SKIPPED');

-- CreateEnum
CREATE TYPE "DeliveryRouteCandidateStatus" AS ENUM ('READY', 'ASSIGNED', 'CANCELLED');

-- CreateTable
CREATE TABLE "DeliveryRoute" (
    "id" TEXT NOT NULL,
    "tenantId" TEXT NOT NULL,
    "branchId" TEXT NOT NULL,
    "status" "DeliveryRouteStatus" NOT NULL DEFAULT 'PLANNED',
    "driverUserId" TEXT,
    "plannedDate" TIMESTAMP(3),
    "createdByUserId" TEXT NOT NULL,
    "createdAt" TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "updatedAt" TIMESTAMP(3) NOT NULL,

    CONSTRAINT "DeliveryRoute_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "DeliveryRouteStop" (
    "id" TEXT NOT NULL,
    "tenantId" TEXT NOT NULL,
    "deliveryRouteId" TEXT NOT NULL,
    "orderId" TEXT NOT NULL,
    "sequence" INTEGER NOT NULL,
    "status" "DeliveryRouteStopStatus" NOT NULL DEFAULT 'PLANNED',
    "createdAt" TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT "DeliveryRouteStop_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "DeliveryRouteCandidate" (
    "id" TEXT NOT NULL,
    "tenantId" TEXT NOT NULL,
    "orderId" TEXT NOT NULL,
    "branchId" TEXT NOT NULL,
    "status" "DeliveryRouteCandidateStatus" NOT NULL DEFAULT 'READY',
    "deliveryRouteId" TEXT,
    "createdFromEventId" TEXT,
    "createdAt" TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "updatedAt" TIMESTAMP(3) NOT NULL,

    CONSTRAINT "DeliveryRouteCandidate_pkey" PRIMARY KEY ("id")
);

-- CreateIndex
CREATE INDEX "DeliveryRoute_tenantId_status_idx" ON "DeliveryRoute"("tenantId", "status");

-- CreateIndex
CREATE INDEX "DeliveryRoute_tenantId_branchId_idx" ON "DeliveryRoute"("tenantId", "branchId");

-- CreateIndex
CREATE INDEX "DeliveryRouteStop_tenantId_deliveryRouteId_idx" ON "DeliveryRouteStop"("tenantId", "deliveryRouteId");

-- CreateIndex
CREATE INDEX "DeliveryRouteStop_tenantId_orderId_idx" ON "DeliveryRouteStop"("tenantId", "orderId");

-- CreateIndex
CREATE UNIQUE INDEX "DeliveryRouteStop_tenantId_deliveryRouteId_orderId_key" ON "DeliveryRouteStop"("tenantId", "deliveryRouteId", "orderId");

-- CreateIndex
CREATE UNIQUE INDEX "DeliveryRouteStop_tenantId_deliveryRouteId_sequence_key" ON "DeliveryRouteStop"("tenantId", "deliveryRouteId", "sequence");

-- CreateIndex
CREATE INDEX "DeliveryRouteCandidate_tenantId_status_idx" ON "DeliveryRouteCandidate"("tenantId", "status");

-- CreateIndex
CREATE INDEX "DeliveryRouteCandidate_tenantId_branchId_idx" ON "DeliveryRouteCandidate"("tenantId", "branchId");

-- CreateIndex
CREATE INDEX "DeliveryRouteCandidate_tenantId_deliveryRouteId_idx" ON "DeliveryRouteCandidate"("tenantId", "deliveryRouteId");

-- CreateIndex
CREATE UNIQUE INDEX "DeliveryRouteCandidate_tenantId_orderId_key" ON "DeliveryRouteCandidate"("tenantId", "orderId");

-- AddForeignKey
ALTER TABLE "DeliveryRouteStop" ADD CONSTRAINT "DeliveryRouteStop_deliveryRouteId_fkey" FOREIGN KEY ("deliveryRouteId") REFERENCES "DeliveryRoute"("id") ON DELETE CASCADE ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "DeliveryRouteCandidate" ADD CONSTRAINT "DeliveryRouteCandidate_deliveryRouteId_fkey" FOREIGN KEY ("deliveryRouteId") REFERENCES "DeliveryRoute"("id") ON DELETE SET NULL ON UPDATE CASCADE;

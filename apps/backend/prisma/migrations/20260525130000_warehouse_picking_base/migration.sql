-- CreateEnum
CREATE TYPE "PickingTaskStatus" AS ENUM ('PENDING', 'COMPLETED', 'CANCELLED');

-- CreateTable
CREATE TABLE "PickingTask" (
    "id" TEXT NOT NULL,
    "tenantId" TEXT NOT NULL,
    "orderId" TEXT NOT NULL,
    "branchId" TEXT NOT NULL,
    "status" "PickingTaskStatus" NOT NULL DEFAULT 'PENDING',
    "createdFromEventId" TEXT,
    "completedByUserId" TEXT,
    "createdAt" TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "updatedAt" TIMESTAMP(3) NOT NULL,
    "completedAt" TIMESTAMP(3),

    CONSTRAINT "PickingTask_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "PickingLine" (
    "id" TEXT NOT NULL,
    "tenantId" TEXT NOT NULL,
    "pickingTaskId" TEXT NOT NULL,
    "orderLineId" TEXT NOT NULL,
    "productId" TEXT NOT NULL,
    "quantity" INTEGER NOT NULL,
    "pickedQuantity" INTEGER NOT NULL DEFAULT 0,
    "createdAt" TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT "PickingLine_pkey" PRIMARY KEY ("id")
);

-- CreateIndex
CREATE UNIQUE INDEX "PickingTask_tenantId_orderId_key" ON "PickingTask"("tenantId", "orderId");
CREATE INDEX "PickingTask_tenantId_status_idx" ON "PickingTask"("tenantId", "status");
CREATE INDEX "PickingTask_tenantId_branchId_idx" ON "PickingTask"("tenantId", "branchId");
CREATE UNIQUE INDEX "PickingLine_tenantId_pickingTaskId_orderLineId_key" ON "PickingLine"("tenantId", "pickingTaskId", "orderLineId");
CREATE INDEX "PickingLine_tenantId_pickingTaskId_idx" ON "PickingLine"("tenantId", "pickingTaskId");

-- AddForeignKey
ALTER TABLE "PickingLine" ADD CONSTRAINT "PickingLine_pickingTaskId_fkey" FOREIGN KEY ("pickingTaskId") REFERENCES "PickingTask"("id") ON DELETE CASCADE ON UPDATE CASCADE;

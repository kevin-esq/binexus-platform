-- CreateEnum
CREATE TYPE "StockTransferStatus" AS ENUM ('PENDING', 'RECEIVED', 'CANCELLED');

-- AlterEnum
ALTER TYPE "StockMovementType" ADD VALUE 'TRANSFER_OUT';
ALTER TYPE "StockMovementType" ADD VALUE 'TRANSFER_IN';

-- CreateTable
CREATE TABLE "StockTransfer" (
    "id" TEXT NOT NULL,
    "tenantId" TEXT NOT NULL,
    "sourceBranchId" TEXT NOT NULL,
    "destinationBranchId" TEXT NOT NULL,
    "productId" TEXT NOT NULL,
    "quantity" INTEGER NOT NULL,
    "status" "StockTransferStatus" NOT NULL DEFAULT 'PENDING',
    "reason" TEXT,
    "createdByUserId" TEXT NOT NULL,
    "receivedByUserId" TEXT,
    "cancelledByUserId" TEXT,
    "createdAt" TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "updatedAt" TIMESTAMP(3) NOT NULL,
    "receivedAt" TIMESTAMP(3),
    "cancelledAt" TIMESTAMP(3),

    CONSTRAINT "StockTransfer_pkey" PRIMARY KEY ("id")
);

-- CreateIndex
CREATE INDEX "StockTransfer_tenantId_status_idx" ON "StockTransfer"("tenantId", "status");
CREATE INDEX "StockTransfer_tenantId_sourceBranchId_idx" ON "StockTransfer"("tenantId", "sourceBranchId");
CREATE INDEX "StockTransfer_tenantId_destinationBranchId_idx" ON "StockTransfer"("tenantId", "destinationBranchId");
CREATE INDEX "StockTransfer_tenantId_productId_idx" ON "StockTransfer"("tenantId", "productId");

-- CreateTable
CREATE TABLE "DeliveryProof" (
    "id" TEXT NOT NULL,
    "tenantId" TEXT NOT NULL,
    "deliveryRouteStopId" TEXT NOT NULL,
    "recipientName" TEXT,
    "notes" TEXT,
    "photoObjectKey" TEXT,
    "signatureObjectKey" TEXT,
    "latitude" DOUBLE PRECISION,
    "longitude" DOUBLE PRECISION,
    "capturedByUserId" TEXT NOT NULL,
    "capturedAt" TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "createdAt" TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "updatedAt" TIMESTAMP(3) NOT NULL,

    CONSTRAINT "DeliveryProof_pkey" PRIMARY KEY ("id")
);

-- CreateIndex
CREATE UNIQUE INDEX "DeliveryProof_deliveryRouteStopId_key" ON "DeliveryProof"("deliveryRouteStopId");

-- CreateIndex
CREATE INDEX "DeliveryProof_tenantId_idx" ON "DeliveryProof"("tenantId");

-- CreateIndex
CREATE INDEX "DeliveryProof_tenantId_deliveryRouteStopId_idx" ON "DeliveryProof"("tenantId", "deliveryRouteStopId");

-- AddForeignKey
ALTER TABLE "DeliveryProof" ADD CONSTRAINT "DeliveryProof_deliveryRouteStopId_fkey" FOREIGN KEY ("deliveryRouteStopId") REFERENCES "DeliveryRouteStop"("id") ON DELETE CASCADE ON UPDATE CASCADE;

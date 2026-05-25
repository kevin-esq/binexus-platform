-- Add dispatch metadata to DeliveryRoute for the dispatch base slice.

ALTER TABLE "DeliveryRoute" ADD COLUMN "dispatchedAt" TIMESTAMP(3);
ALTER TABLE "DeliveryRoute" ADD COLUMN "dispatchedByUserId" TEXT;

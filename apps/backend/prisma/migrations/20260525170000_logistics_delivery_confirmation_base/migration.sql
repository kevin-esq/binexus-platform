-- Add delivery confirmation tracing for stops and route completion.

ALTER TABLE "DeliveryRouteStop" ADD COLUMN "deliveredAt" TIMESTAMP(3);
ALTER TABLE "DeliveryRouteStop" ADD COLUMN "deliveredByUserId" TEXT;

ALTER TABLE "DeliveryRoute" ADD COLUMN "completedAt" TIMESTAMP(3);

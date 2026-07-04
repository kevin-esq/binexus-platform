-- CreateEnum
CREATE TYPE "DeliveryFailureReason" AS ENUM ('NO_RECIPIENT', 'WRONG_ADDRESS', 'REFUSED', 'DAMAGED', 'OTHER');

-- AlterEnum
ALTER TYPE "OrderState" ADD VALUE 'DELIVERY_ATTEMPT_FAILED';

-- AlterTable
ALTER TABLE "DeliveryRouteStop" ADD COLUMN     "failedAt" TIMESTAMP(3),
ADD COLUMN     "failedByUserId" TEXT,
ADD COLUMN     "failureNotes" TEXT,
ADD COLUMN     "failureReason" "DeliveryFailureReason";

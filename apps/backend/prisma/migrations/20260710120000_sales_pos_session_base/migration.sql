-- CreateEnum
CREATE TYPE "SalesSessionStatus" AS ENUM ('OPEN', 'CLOSED');

-- CreateEnum
CREATE TYPE "TicketStatus" AS ENUM ('COMPLETED');

-- AlterEnum
ALTER TYPE "StockMovementType" ADD VALUE 'SALE';

-- CreateTable
CREATE TABLE "SalesSession" (
    "id" TEXT NOT NULL,
    "tenantId" TEXT NOT NULL,
    "branchId" TEXT NOT NULL,
    "terminalId" TEXT NOT NULL,
    "status" "SalesSessionStatus" NOT NULL DEFAULT 'OPEN',
    "openingFloatCents" INTEGER NOT NULL,
    "currency" TEXT NOT NULL DEFAULT 'MXN',
    "openedByUserId" TEXT NOT NULL,
    "openedAt" TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "closedByUserId" TEXT,
    "closedAt" TIMESTAMP(3),
    "expectedClosingCents" INTEGER,
    "declaredClosingCents" INTEGER,
    "discrepancyCents" INTEGER,
    "discrepancyReason" TEXT,
    "closeNotes" TEXT,

    CONSTRAINT "SalesSession_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "Ticket" (
    "id" TEXT NOT NULL,
    "tenantId" TEXT NOT NULL,
    "sessionId" TEXT NOT NULL,
    "branchId" TEXT NOT NULL,
    "terminalId" TEXT NOT NULL,
    "customerLabel" TEXT NOT NULL,
    "status" "TicketStatus" NOT NULL DEFAULT 'COMPLETED',
    "totalCents" INTEGER NOT NULL,
    "currency" TEXT NOT NULL,
    "cashierUserId" TEXT NOT NULL,
    "createdAt" TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT "Ticket_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "TicketLine" (
    "id" TEXT NOT NULL,
    "tenantId" TEXT NOT NULL,
    "ticketId" TEXT NOT NULL,
    "productId" TEXT NOT NULL,
    "productName" TEXT NOT NULL,
    "quantity" INTEGER NOT NULL,
    "unitPriceCents" INTEGER NOT NULL,
    "lineTotalCents" INTEGER NOT NULL,

    CONSTRAINT "TicketLine_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "PaymentCapture" (
    "id" TEXT NOT NULL,
    "tenantId" TEXT NOT NULL,
    "ticketId" TEXT NOT NULL,
    "sessionId" TEXT NOT NULL,
    "method" "PaymentMethod" NOT NULL,
    "amountCents" INTEGER NOT NULL,
    "currency" TEXT NOT NULL,
    "capturedAt" TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT "PaymentCapture_pkey" PRIMARY KEY ("id")
);

-- CreateIndex
CREATE INDEX "SalesSession_tenantId_idx" ON "SalesSession"("tenantId");

-- CreateIndex
CREATE INDEX "SalesSession_tenantId_branchId_terminalId_idx" ON "SalesSession"("tenantId", "branchId", "terminalId");

-- CreateIndex
CREATE INDEX "SalesSession_tenantId_status_idx" ON "SalesSession"("tenantId", "status");

-- Partial unique: at most one OPEN session per terminal within a branch.
CREATE UNIQUE INDEX "SalesSession_open_terminal_unique" ON "SalesSession"("tenantId", "branchId", "terminalId") WHERE "status" = 'OPEN';

-- CreateIndex
CREATE INDEX "Ticket_tenantId_idx" ON "Ticket"("tenantId");

-- CreateIndex
CREATE INDEX "Ticket_tenantId_sessionId_idx" ON "Ticket"("tenantId", "sessionId");

-- CreateIndex
CREATE INDEX "TicketLine_tenantId_ticketId_idx" ON "TicketLine"("tenantId", "ticketId");

-- CreateIndex
CREATE INDEX "PaymentCapture_tenantId_sessionId_idx" ON "PaymentCapture"("tenantId", "sessionId");

-- CreateIndex
CREATE INDEX "PaymentCapture_tenantId_ticketId_idx" ON "PaymentCapture"("tenantId", "ticketId");

-- AddForeignKey
ALTER TABLE "Ticket" ADD CONSTRAINT "Ticket_sessionId_fkey" FOREIGN KEY ("sessionId") REFERENCES "SalesSession"("id") ON DELETE CASCADE ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "TicketLine" ADD CONSTRAINT "TicketLine_ticketId_fkey" FOREIGN KEY ("ticketId") REFERENCES "Ticket"("id") ON DELETE CASCADE ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "PaymentCapture" ADD CONSTRAINT "PaymentCapture_ticketId_fkey" FOREIGN KEY ("ticketId") REFERENCES "Ticket"("id") ON DELETE CASCADE ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "PaymentCapture" ADD CONSTRAINT "PaymentCapture_sessionId_fkey" FOREIGN KEY ("sessionId") REFERENCES "SalesSession"("id") ON DELETE CASCADE ON UPDATE CASCADE;

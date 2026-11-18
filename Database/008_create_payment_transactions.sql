/* ============================================================================
   NEET BEE - payment transactions (net banking, UPI, card).
   One row per payment attempt, written by AuthenticationService (PaymentService).
   Card numbers and CVV are never stored: MethodDetail holds only a masked,
   display-only value such as "VISA ****1234", a bank code, or a masked UPI ID.
   Run after 007. Safe to re-run (idempotent).
   sqlcmd -S localhost -E -i 008_create_payment_transactions.sql
   ========================================================================== */

USE NEET_BEE;
GO

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'dbo.PaymentTransactions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PaymentTransactions
    (
        Id               BIGINT         NOT NULL IDENTITY(1,1),
        UserId           INT            NOT NULL,
        Amount           DECIMAL(12,2)  NOT NULL,
        Currency         CHAR(3)        NOT NULL CONSTRAINT DF_PaymentTransactions_Currency DEFAULT ('INR'),
        Method           VARCHAR(20)    NOT NULL,              -- netbanking | upi | card
        Status           VARCHAR(20)    NOT NULL,              -- pending | success | failed
        Description      NVARCHAR(200)  NULL,                  -- what the payment is for
        MethodDetail     NVARCHAR(64)   NULL,                  -- masked, display only. Never a full card number.
        Gateway          VARCHAR(32)    NOT NULL,              -- fake | razorpay | ...
        GatewayReference VARCHAR(100)   NULL,                  -- the gateway's payment ID
        FailureReason    NVARCHAR(400)  NULL,
        CreatedAt        DATETIME2(7)   NOT NULL CONSTRAINT DF_PaymentTransactions_CreatedAt DEFAULT (SYSUTCDATETIME()),
        UpdatedAt        DATETIME2(7)   NOT NULL CONSTRAINT DF_PaymentTransactions_UpdatedAt DEFAULT (SYSUTCDATETIME()),
        CompletedAt      DATETIME2(7)   NULL,
        CONSTRAINT PK_PaymentTransactions PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_PaymentTransactions_Users FOREIGN KEY (UserId) REFERENCES dbo.Users (Id) ON DELETE CASCADE,
        CONSTRAINT CK_PaymentTransactions_Amount CHECK (Amount > 0),
        CONSTRAINT CK_PaymentTransactions_Method CHECK (Method IN ('netbanking', 'upi', 'card')),
        CONSTRAINT CK_PaymentTransactions_Status CHECK (Status IN ('pending', 'success', 'failed'))
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PaymentTransactions_UserId_CreatedAt' AND object_id = OBJECT_ID(N'dbo.PaymentTransactions'))
    CREATE INDEX IX_PaymentTransactions_UserId_CreatedAt ON dbo.PaymentTransactions (UserId, CreatedAt DESC);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PaymentTransactions_GatewayReference' AND object_id = OBJECT_ID(N'dbo.PaymentTransactions'))
    -- Not a filtered index on purpose: a filtered index would make DELETEs on dbo.Users (which cascade here)
    -- fail in sessions with QUOTED_IDENTIFIER OFF, such as plain sqlcmd.
    CREATE INDEX IX_PaymentTransactions_GatewayReference ON dbo.PaymentTransactions (Gateway, GatewayReference);
GO

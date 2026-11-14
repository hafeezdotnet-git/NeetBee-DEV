/* ============================================================================
   NEET BEE - email one-time sign-in codes.
   Run after 002. Safe to re-run (idempotent).
   sqlcmd -S localhost -E -i 003_email_otp.sql
   ========================================================================== */

USE NEET_BEE;
GO

IF OBJECT_ID(N'dbo.EmailOtpCodes', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.EmailOtpCodes
    (
        Id         BIGINT        NOT NULL IDENTITY(1,1),
        Email      NVARCHAR(256) NOT NULL,
        CodeHash   CHAR(64)      NOT NULL,   -- HMAC-SHA256 hex of email:code. The code itself is never stored.
        CreatedAt  DATETIME2(7)  NOT NULL CONSTRAINT DF_EmailOtpCodes_CreatedAt DEFAULT (SYSUTCDATETIME()),
        ExpiresAt  DATETIME2(7)  NOT NULL,
        Attempts   INT           NOT NULL CONSTRAINT DF_EmailOtpCodes_Attempts DEFAULT (0),
        ConsumedAt DATETIME2(7)  NULL,
        CONSTRAINT PK_EmailOtpCodes PRIMARY KEY CLUSTERED (Id)
    );

    CREATE INDEX IX_EmailOtpCodes_Email_CreatedAt ON dbo.EmailOtpCodes (Email, CreatedAt);
END;
GO

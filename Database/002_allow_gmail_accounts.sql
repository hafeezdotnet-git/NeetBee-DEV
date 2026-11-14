/* ============================================================================
   NEET BEE - allow Gmail (Google) accounts, which have no password.
   Run after 001. For a database created from the current 001 script this is
   already done. For an older database, run:
   sqlcmd -S localhost -E -i 002_allow_gmail_accounts.sql
   Safe to re-run (idempotent).
   ========================================================================== */

USE NEET_BEE;
GO

IF EXISTS (SELECT 1 FROM sys.columns
           WHERE object_id = OBJECT_ID(N'dbo.Users') AND name = N'PasswordHash' AND is_nullable = 0)
BEGIN
    ALTER TABLE dbo.Users ALTER COLUMN PasswordHash NVARCHAR(512) NULL;
END;
GO

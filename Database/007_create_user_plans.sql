/* ============================================================================
   NEET BEE - the plan each user has bought (Starter, Premium or Advance).
   A user "has premium" when a Premium or Advance row is active now
   (StartsAt has passed and ExpiresAt is empty or still in the future).
   Run after 006. Safe to re-run (idempotent).
   sqlcmd -S localhost -E -i 007_create_user_plans.sql
   ========================================================================== */

USE NEET_BEE;
GO

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'dbo.UserPlans', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.UserPlans
    (
        Id        INT          NOT NULL IDENTITY(1,1),
        UserId    INT          NOT NULL,
        PlanCode  VARCHAR(20)  NOT NULL,              -- starter | premium | advance
        StartsAt  DATETIME2(7) NOT NULL CONSTRAINT DF_UserPlans_StartsAt  DEFAULT (SYSUTCDATETIME()),
        ExpiresAt DATETIME2(7) NULL,                  -- NULL = does not expire (e.g. "valid till NEET 2027" set explicitly)
        CreatedAt DATETIME2(7) NOT NULL CONSTRAINT DF_UserPlans_CreatedAt DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_UserPlans PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_UserPlans_Users FOREIGN KEY (UserId) REFERENCES dbo.Users (Id) ON DELETE CASCADE,
        CONSTRAINT CK_UserPlans_PlanCode CHECK (PlanCode IN ('starter', 'premium', 'advance')),
        CONSTRAINT CK_UserPlans_Dates    CHECK (ExpiresAt IS NULL OR ExpiresAt > StartsAt)
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_UserPlans_UserId' AND object_id = OBJECT_ID(N'dbo.UserPlans'))
    CREATE INDEX IX_UserPlans_UserId ON dbo.UserPlans (UserId, PlanCode) INCLUDE (StartsAt, ExpiresAt);
GO

/* ============================================================================
   NEET BEE - database bootstrap
   Server : localhost (SQL Server, Windows Authentication)
   Run    : sqlcmd -S localhost -E -i 001_create_neet_bee.sql
   Safe to re-run (idempotent).
   ========================================================================== */

IF DB_ID(N'NEET_BEE') IS NULL
BEGIN
    CREATE DATABASE NEET_BEE;
END;
GO

USE NEET_BEE;
GO

IF OBJECT_ID(N'dbo.Users', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Users
    (
        Id           INT           NOT NULL IDENTITY(1,1),
        UserName     NVARCHAR(64)  NOT NULL,
        Email        NVARCHAR(256) NOT NULL,
        PasswordHash NVARCHAR(512) NULL,         -- NULL for Gmail-only accounts (see 002)
        IsActive     BIT           NOT NULL CONSTRAINT DF_Users_IsActive  DEFAULT (1),
        CreatedAt    DATETIME2(7)  NOT NULL CONSTRAINT DF_Users_CreatedAt DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_Users PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT UQ_Users_UserName UNIQUE (UserName),
        CONSTRAINT UQ_Users_Email    UNIQUE (Email)
    );
END;
GO

IF OBJECT_ID(N'dbo.RefreshTokens', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.RefreshTokens
    (
        Id                  BIGINT           NOT NULL IDENTITY(1,1),
        UserId              INT              NOT NULL,
        TokenHash           CHAR(64)         NOT NULL,   -- SHA-256 hex of the raw token; raw tokens are never stored
        FamilyId            UNIQUEIDENTIFIER NOT NULL,   -- all rotations of one login share a family
        CreatedAt           DATETIME2(7)     NOT NULL CONSTRAINT DF_RefreshTokens_CreatedAt DEFAULT (SYSUTCDATETIME()),
        ExpiresAt           DATETIME2(7)     NOT NULL,
        RevokedAt           DATETIME2(7)     NULL,
        ReplacedByTokenHash CHAR(64)         NULL,
        CreatedByIp         VARCHAR(45)      NULL,
        RevokedByIp         VARCHAR(45)      NULL,
        CONSTRAINT PK_RefreshTokens PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT UQ_RefreshTokens_TokenHash UNIQUE (TokenHash),
        CONSTRAINT FK_RefreshTokens_Users FOREIGN KEY (UserId) REFERENCES dbo.Users (Id) ON DELETE CASCADE,
        CONSTRAINT CK_RefreshTokens_Expiry CHECK (ExpiresAt > CreatedAt)
    );

    CREATE INDEX IX_RefreshTokens_UserId   ON dbo.RefreshTokens (UserId);
    CREATE INDEX IX_RefreshTokens_FamilyId ON dbo.RefreshTokens (FamilyId);
END;
GO

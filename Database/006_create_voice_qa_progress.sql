/* ============================================================================
   NEET BEE - Voice Q&A listening progress: the last question each user heard,
   kept separately for every subject/year filter ("Biology:2016", "all:all", ...).
   Run after 005. Safe to re-run (idempotent).
   sqlcmd -S localhost -E -i 006_create_voice_qa_progress.sql
   ========================================================================== */

USE NEET_BEE;
GO

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'dbo.VoiceQaProgress', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.VoiceQaProgress
    (
        Id             INT          NOT NULL IDENTITY(1,1),
        UserId         INT          NOT NULL,
        Scope          NVARCHAR(40) NOT NULL,   -- "<subject|all>:<year|all>"
        LastQuestionId INT          NOT NULL,
        UpdatedAt      DATETIME2(7) NOT NULL CONSTRAINT DF_VoiceQaProgress_UpdatedAt DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_VoiceQaProgress PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT UQ_VoiceQaProgress_User_Scope UNIQUE (UserId, Scope),
        CONSTRAINT FK_VoiceQaProgress_Users     FOREIGN KEY (UserId)         REFERENCES dbo.Users (Id) ON DELETE CASCADE,
        CONSTRAINT FK_VoiceQaProgress_Questions FOREIGN KEY (LastQuestionId) REFERENCES dbo.Questions (Id)
    );
END;
GO

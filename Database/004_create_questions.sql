/* ============================================================================
   NEET BEE - question bank (multiple-choice questions by exam year and subject).
   Run after 003. Safe to re-run (idempotent).
   sqlcmd -S localhost -E -i 004_create_questions.sql
   Data is loaded by 005_seed_questions_2015_2017.sql.
   ========================================================================== */

USE NEET_BEE;
GO

-- Filtered indexes need these options; sqlcmd turns QUOTED_IDENTIFIER off unless -I is passed.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'dbo.Questions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Questions
    (
        Id            INT            NOT NULL IDENTITY(1,1),
        ExamYear      SMALLINT       NOT NULL,
        Subject       NVARCHAR(20)   NOT NULL,
        Lesson        NVARCHAR(200)  NULL,
        Topic         NVARCHAR(200)  NULL,
        SourceRow     INT            NOT NULL,   -- row number in the source workbook, keeps the import re-runnable
        QuestionText  NVARCHAR(MAX)  NOT NULL,
        OptionA       NVARCHAR(1000) NOT NULL,
        OptionB       NVARCHAR(1000) NOT NULL,
        OptionC       NVARCHAR(1000) NOT NULL,
        OptionD       NVARCHAR(1000) NOT NULL,
        CorrectOption CHAR(1)        NOT NULL,
        Explanation   NVARCHAR(MAX)  NULL,
        IsActive      BIT            NOT NULL CONSTRAINT DF_Questions_IsActive  DEFAULT (1),
        CreatedAt     DATETIME2(7)   NOT NULL CONSTRAINT DF_Questions_CreatedAt DEFAULT (SYSUTCDATETIME()),
        UpdatedAt     DATETIME2(7)   NOT NULL CONSTRAINT DF_Questions_UpdatedAt DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_Questions PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT UQ_Questions_ExamYear_SourceRow UNIQUE (ExamYear, SourceRow),
        CONSTRAINT CK_Questions_Subject       CHECK (Subject IN (N'Biology', N'Chemistry', N'Physics')),
        CONSTRAINT CK_Questions_CorrectOption CHECK (CorrectOption IN ('A', 'B', 'C', 'D')),
        CONSTRAINT CK_Questions_ExamYear      CHECK (ExamYear BETWEEN 1988 AND 2100)
    );
END;
GO

-- The Voice Q&A page filters by subject and year and orders by year, then source row.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Questions_Subject_ExamYear' AND object_id = OBJECT_ID(N'dbo.Questions'))
    CREATE INDEX IX_Questions_Subject_ExamYear ON dbo.Questions (Subject, ExamYear, SourceRow) WHERE IsActive = 1;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Questions_ExamYear' AND object_id = OBJECT_ID(N'dbo.Questions'))
    CREATE INDEX IX_Questions_ExamYear ON dbo.Questions (ExamYear, SourceRow) WHERE IsActive = 1;
GO

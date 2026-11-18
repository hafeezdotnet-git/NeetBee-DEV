using Microsoft.Data.SqlClient;
using NeetBee.Web.Models;

namespace NeetBee.Web.Services;

/// <summary>Supplies the questions read aloud on the Voice Q&amp;A page.</summary>
public interface IVoiceQuestionService
{
    /// <summary>Subjects that can be chosen in the filter.</summary>
    IReadOnlyList<string> Subjects { get; }

    /// <summary>Exam years that have questions, newest first (at most ten).</summary>
    Task<IReadOnlyList<int>> GetYearsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Returns the questions matching the filter and the total match count. When <paramref name="pageSize"/>
    /// is null, every match is returned. Each question's <see cref="VoiceQuestion.Number"/> is its position
    /// in the filtered list.
    /// </summary>
    Task<(IReadOnlyList<VoiceQuestion> Questions, int Total)> GetPageAsync(
        VoiceQaFilter filter, int page, int? pageSize, CancellationToken cancellationToken);

    /// <summary>
    /// The position of the last question this user heard in the filtered list, and the list size;
    /// null when nothing has been heard for this filter yet.
    /// </summary>
    Task<(int LastHeardNumber, int Total)?> GetProgressAsync(int userId, VoiceQaFilter filter, CancellationToken cancellationToken);

    /// <summary>Records the last question heard for this filter. Ignored when the question is not in the filter.</summary>
    Task SaveProgressAsync(int userId, VoiceQaFilter filter, int questionId, CancellationToken cancellationToken);

    /// <summary>Forgets the progress for this filter, so listening starts again from question 1.</summary>
    Task ResetProgressAsync(int userId, VoiceQaFilter filter, CancellationToken cancellationToken);
}

/// <summary>
/// Reads questions from dbo.Questions in the NEET_BEE SQL Server database
/// (created by Database\004_create_questions.sql, loaded by Database\005_seed_questions_2015_2017.sql).
/// Uses the "NeetBee" connection string. All filter values are passed as SQL parameters.
/// </summary>
public sealed class SqlVoiceQuestionService(IConfiguration configuration, ILogger<SqlVoiceQuestionService> logger)
    : IVoiceQuestionService
{
    private static readonly string[] SubjectNames = ["Biology", "Chemistry", "Physics"];

    private string ConnectionString => configuration.GetConnectionString("NeetBee")
        ?? throw new InvalidOperationException("ConnectionStrings:NeetBee is not configured.");

    public IReadOnlyList<string> Subjects => SubjectNames;

    public async Task<IReadOnlyList<int>> GetYearsAsync(CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT DISTINCT TOP (10) ExamYear
            FROM dbo.Questions
            WHERE IsActive = 1
            ORDER BY ExamYear DESC;
            """;

        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var years = new List<int>();
        while (await reader.ReadAsync(cancellationToken)) years.Add(reader.GetInt16(0));
        return years;
    }

    public async Task<(IReadOnlyList<VoiceQuestion> Questions, int Total)> GetPageAsync(
        VoiceQaFilter filter, int page, int? pageSize, CancellationToken cancellationToken)
    {
        // One round trip: the total count and the requested page.
        const string sql = """
            SELECT COUNT(*)
            FROM dbo.Questions
            WHERE IsActive = 1
              AND (@Subject IS NULL OR Subject = @Subject)
              AND (@Year IS NULL OR ExamYear = @Year);

            SELECT Id, ExamYear, Subject, Topic, QuestionText, OptionA, OptionB, OptionC, OptionD, CorrectOption, Explanation
            FROM dbo.Questions
            WHERE IsActive = 1
              AND (@Subject IS NULL OR Subject = @Subject)
              AND (@Year IS NULL OR ExamYear = @Year)
            ORDER BY ExamYear DESC, SourceRow
            OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;
            """;

        var skip = pageSize is null ? 0 : (page - 1) * pageSize.Value;
        var take = pageSize ?? int.MaxValue;

        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        AddFilterParameters(command, filter);
        command.Parameters.Add(new SqlParameter("@Skip", System.Data.SqlDbType.Int) { Value = skip });
        command.Parameters.Add(new SqlParameter("@Take", System.Data.SqlDbType.Int) { Value = take });

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        var total = reader.GetInt32(0);

        await reader.NextResultAsync(cancellationToken);
        var questions = new List<VoiceQuestion>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var correct = reader.GetString(9);
            questions.Add(new VoiceQuestion(
                Id: reader.GetInt32(0),
                Number: skip + questions.Count + 1,
                Subject: reader.GetString(2),
                Year: reader.GetInt16(1),
                Topic: reader.IsDBNull(3) ? null : reader.GetString(3),
                Text: reader.GetString(4),
                Options: [reader.GetString(5), reader.GetString(6), reader.GetString(7), reader.GetString(8)],
                CorrectIndex: correct[0] - 'A',
                Explanation: reader.IsDBNull(10) ? "" : reader.GetString(10)));
        }

        logger.LogDebug("Voice Q&A query {Subject}/{Year}: {Count} of {Total}", filter.Subject, filter.Year, questions.Count, total);
        return (questions, total);
    }

    public async Task<(int LastHeardNumber, int Total)?> GetProgressAsync(int userId, VoiceQaFilter filter, CancellationToken cancellationToken)
    {
        // Position of the saved question within the same ordering the page uses.
        const string sql = """
            DECLARE @Last INT = (SELECT LastQuestionId FROM dbo.VoiceQaProgress WHERE UserId = @UserId AND Scope = @Scope);
            IF @Last IS NULL RETURN;

            WITH Filtered AS (
                SELECT Id, ROW_NUMBER() OVER (ORDER BY ExamYear DESC, SourceRow) AS Position
                FROM dbo.Questions
                WHERE IsActive = 1
                  AND (@Subject IS NULL OR Subject = @Subject)
                  AND (@Year IS NULL OR ExamYear = @Year)
            )
            SELECT (SELECT Position FROM Filtered WHERE Id = @Last), (SELECT COUNT(*) FROM Filtered);
            """;

        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        AddFilterParameters(command, filter);
        command.Parameters.Add(new SqlParameter("@UserId", System.Data.SqlDbType.Int) { Value = userId });
        command.Parameters.Add(new SqlParameter("@Scope", System.Data.SqlDbType.NVarChar, 40) { Value = filter.ProgressScope });

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        // Nothing saved yet, or the saved question is no longer listed.
        if (!await reader.ReadAsync(cancellationToken) || reader.IsDBNull(0)) return null;
        return ((int)reader.GetInt64(0), reader.GetInt32(1));
    }

    public async Task SaveProgressAsync(int userId, VoiceQaFilter filter, int questionId, CancellationToken cancellationToken)
    {
        const string sql = """
            IF NOT EXISTS (SELECT 1 FROM dbo.Questions
                           WHERE Id = @QuestionId AND IsActive = 1
                             AND (@Subject IS NULL OR Subject = @Subject)
                             AND (@Year IS NULL OR ExamYear = @Year))
                RETURN;

            UPDATE dbo.VoiceQaProgress
            SET LastQuestionId = @QuestionId, UpdatedAt = SYSUTCDATETIME()
            WHERE UserId = @UserId AND Scope = @Scope;

            IF @@ROWCOUNT = 0
                INSERT dbo.VoiceQaProgress (UserId, Scope, LastQuestionId) VALUES (@UserId, @Scope, @QuestionId);
            """;

        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        AddFilterParameters(command, filter);
        command.Parameters.Add(new SqlParameter("@UserId", System.Data.SqlDbType.Int) { Value = userId });
        command.Parameters.Add(new SqlParameter("@Scope", System.Data.SqlDbType.NVarChar, 40) { Value = filter.ProgressScope });
        command.Parameters.Add(new SqlParameter("@QuestionId", System.Data.SqlDbType.Int) { Value = questionId });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task ResetProgressAsync(int userId, VoiceQaFilter filter, CancellationToken cancellationToken)
    {
        const string sql = "DELETE FROM dbo.VoiceQaProgress WHERE UserId = @UserId AND Scope = @Scope;";
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add(new SqlParameter("@UserId", System.Data.SqlDbType.Int) { Value = userId });
        command.Parameters.Add(new SqlParameter("@Scope", System.Data.SqlDbType.NVarChar, 40) { Value = filter.ProgressScope });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddFilterParameters(SqlCommand command, VoiceQaFilter filter)
    {
        command.Parameters.Add(new SqlParameter("@Subject", System.Data.SqlDbType.NVarChar, 20) { Value = (object?)filter.Subject ?? DBNull.Value });
        command.Parameters.Add(new SqlParameter("@Year", System.Data.SqlDbType.SmallInt) { Value = filter.Year is null ? DBNull.Value : (short)filter.Year.Value });
    }
}

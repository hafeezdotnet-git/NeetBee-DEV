namespace NeetBee.Web.Models;

/// <summary>
/// Learning progress shown on the dashboard. Values come from IStudentProgressService, never from sample data in the views.
/// </summary>
public sealed record StudentProgress(
    int StreakDays,
    int DailyGoalPercent,
    int CoursesEnrolled,
    int QuestionsPracticed,
    int BiologyPercent,
    int ChemistryPercent,
    int PhysicsPercent,
    ContinueLearningItem? ContinueLearning,
    IReadOnlyList<MockTestItem> UpcomingMockTests)
{
    /// <summary>Progress for a student with no recorded activity.</summary>
    public static StudentProgress Empty { get; } = new(
        StreakDays: 0,
        DailyGoalPercent: 0,
        CoursesEnrolled: 0,
        QuestionsPracticed: 0,
        BiologyPercent: 0,
        ChemistryPercent: 0,
        PhysicsPercent: 0,
        ContinueLearning: null,
        UpcomingMockTests: Array.Empty<MockTestItem>());
}

/// <summary>The lesson the student stopped at, shown in the Continue Learning card.</summary>
public sealed record ContinueLearningItem(string Subject, string Lesson, int Percent);

/// <summary>A scheduled mock test shown in the Upcoming Mock Tests card.</summary>
public sealed record MockTestItem(string Title, DateTime StartsAtUtc, int DurationMinutes);

using NeetBee.Web.Models;

namespace NeetBee.Web.Services;

/// <summary>Supplies the learning progress shown on the dashboard.</summary>
public interface IStudentProgressService
{
    Task<StudentProgress> GetAsync(string userId, CancellationToken cancellationToken);
}

/// <summary>
/// The project has no progress store yet, so this returns empty progress (zeros and empty lists).
/// When a progress source exists, read it here for the signed-in user. The dashboard views need no changes.
/// </summary>
public sealed class StudentProgressService(ILogger<StudentProgressService> logger) : IStudentProgressService
{
    /// <summary>Returns the progress for one student. Logs the lookup so it can be traced.</summary>
    public Task<StudentProgress> GetAsync(string userId, CancellationToken cancellationToken)
    {
        logger.LogDebug("Progress lookup for UserId {UserId}: no progress store configured, returning empty progress", userId);
        return Task.FromResult(StudentProgress.Empty);
    }
}

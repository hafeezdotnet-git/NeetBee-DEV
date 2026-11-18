using System.Security.Claims;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;

namespace NeetBee.Web.Services;

/// <summary>Tells whether the signed-in user has a paid Premium (or higher) plan.</summary>
public interface IUserPlanService
{
    /// <summary>True when the user has an active Premium or Advance plan in dbo.UserPlans.</summary>
    Task<bool> HasPremiumAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default);

    /// <summary>Forgets the cached answer for a user (call after a plan is bought or changed).</summary>
    void Forget(int userId);
}

/// <summary>
/// Reads dbo.UserPlans (Database\007_create_user_plans.sql). The answer is cached for two minutes per user,
/// so the header does not query the database on every page. If the database cannot be reached the user is
/// treated as not premium, which only means the "Go Premium" button is shown.
/// </summary>
public sealed class UserPlanService(IConfiguration configuration, IMemoryCache cache, ILogger<UserPlanService> logger)
    : IUserPlanService
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(2);

    public async Task<bool> HasPremiumAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        if (!int.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return false;
        if (cache.TryGetValue(CacheKey(userId), out bool cached)) return cached;

        const string sql = """
            SELECT CASE WHEN EXISTS (
                SELECT 1 FROM dbo.UserPlans
                WHERE UserId = @UserId
                  AND PlanCode IN ('premium', 'advance')
                  AND StartsAt <= SYSUTCDATETIME()
                  AND (ExpiresAt IS NULL OR ExpiresAt > SYSUTCDATETIME())
            ) THEN 1 ELSE 0 END;
            """;

        try
        {
            var connectionString = configuration.GetConnectionString("NeetBee")
                ?? throw new InvalidOperationException("ConnectionStrings:NeetBee is not configured.");
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new SqlCommand(sql, connection);
            command.Parameters.Add(new SqlParameter("@UserId", System.Data.SqlDbType.Int) { Value = userId });
            var hasPremium = (int)(await command.ExecuteScalarAsync(cancellationToken) ?? 0) == 1;

            cache.Set(CacheKey(userId), hasPremium, CacheFor);
            return hasPremium;
        }
        catch (SqlException ex)
        {
            logger.LogWarning(ex, "Could not read the plan for UserId {UserId}; treating as not premium", userId);
            return false;
        }
    }

    public void Forget(int userId) => cache.Remove(CacheKey(userId));

    private static string CacheKey(int userId) => $"plan:premium:{userId}";
}

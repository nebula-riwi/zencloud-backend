namespace ZenCloud.Services.Interfaces;

public interface IPlanValidationService
{
    Task<bool> CanCreateDatabaseAsync(Guid userId, Guid engineId);
    Task<int> GetMaxDatabasesPerEngineAsync(Guid userId);
    Task<(bool CanCreate, string? ErrorMessage, int CurrentCount, int MaxCount)> CanCreateDatabaseWithDetailsAsync(Guid userId, Guid engineId);
    Task EnforcePlanLimitsAsync(Guid userId);
}
namespace ZenCloud.Services.Interfaces;

public interface IPlanValidationService
{
    Task<bool> CanCreateDatabaseAsync(Guid userId, Guid engineId);
    Task<int> GetMaxDatabasesPerEngineAsync(Guid userId);
}
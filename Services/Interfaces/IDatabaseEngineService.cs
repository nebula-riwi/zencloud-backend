namespace ZenCloud.Services.Interfaces;

public interface IDatabaseEngineService
{
    Task CreatePhysicalDatabaseAsync(string engineName, string databaseName, string username, string password);
    Task DeletePhysicalDatabaseAsync(string engineName, string databaseName, string username);
}
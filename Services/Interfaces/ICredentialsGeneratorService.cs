namespace ZenCloud.Services.Interfaces;

public interface ICredentialsGeneratorService
{
    string GenerateDatabaseName(string engineName, Guid engineId);
    string GenerateUsername(string databaseName);
    string GeneratePassword();
    string HashPassword(string password);
}
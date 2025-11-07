using MySqlConnector;
using ZenCloud.Services.Interfaces;

namespace ZenCloud.Services.Implementations;

public class DatabaseEngineService : IDatabaseEngineService
{
    private readonly IConfiguration _configuration;

    public DatabaseEngineService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task CreatePhysicalDatabaseAsync(string engineName, string databaseName, string username,
        string password)
    {
        switch (engineName.ToLower())
        {
            case "mysql":
                await CreateMySqlDatabaseAsync(databaseName, username, password);
                break;
            case "postgresql":
                throw new NotImplementedException("PostgreSQL aún no implementado");
            default:
                throw new NotSupportedException($"Motor {engineName} no soportado");
        }
    }

    public async Task DeletePhysicalDatabaseAsync(string engineName, string databaseName, string username)
    {
        switch (databaseName.ToLower())
        {
            case "mysql":
                await DeleteMySqlDatabaseAsync(databaseName, username);
                break;
            case "postgresql":
                // TODO: Implementar PostgreSQL
                throw new NotImplementedException("PostgreSQL aún no implementado");
            default:
                throw new NotSupportedException($"Motor {engineName} no soportado");
        }
    }

    private async Task CreateMySqlDatabaseAsync(string databaseName, string username, string password)
    {
        var host = _configuration["MYSQL_HOST"];
        var port = _configuration["MYSQL_PORT"];
        var adminUser = _configuration["MYSQL_ADMIN_USER"];
        var adminPassword = _configuration["MYSQL_ADMIN_PASSWORD"];
        
        var adminConnectionString = 
            $"Server={host};Port={port};User={adminUser};Password={adminPassword};Database=mysql";
        
        using var connection = new MySqlConnection(adminConnectionString);
        await connection.OpenAsync();
        
        var createDbCommand = new MySqlCommand($"CREATE DATABASE IF NOT EXISTS`{databaseName}`;", connection);
        await createDbCommand.ExecuteNonQueryAsync();
    }
    
    private async Task DeleteMySqlDatabaseAsync(string databaseName, string username)
    {
        var connectionString = _configuration.GetConnectionString("MySQL");
        
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();

        // 1. Eliminar la base de datos
        var dropDbCommand = new MySqlCommand($"DROP DATABASE IF EXISTS `{databaseName}`;", connection);
        await dropDbCommand.ExecuteNonQueryAsync();

        // 2. Eliminar el usuario
        var dropUserCommand = new MySqlCommand($"DROP USER IF EXISTS '{username}'@'%';", connection);
        await dropUserCommand.ExecuteNonQueryAsync();

        // 3. Aplicar los cambios
        var flushCommand = new MySqlCommand("FLUSH PRIVILEGES;", connection);
        await flushCommand.ExecuteNonQueryAsync();
    }
}
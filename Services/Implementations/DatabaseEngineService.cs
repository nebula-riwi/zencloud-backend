using Microsoft.Data.SqlClient;
using MySqlConnector;
using Npgsql;
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
                await CreatePostgreSqlDatabaseAsync(databaseName, username, password);
                break;
            case "sqlserver":
                await CreateSQLServerDatabaseAsync(databaseName, username, password);
                break;
            default:
                throw new NotSupportedException($"Motor {engineName} no soportado");
        }
    }

    public async Task DeletePhysicalDatabaseAsync(string engineName, string databaseName, string username)
    {
        switch (engineName.ToLower())
        {
            case "mysql":
                await DeleteMySqlDatabaseAsync(databaseName, username);
                break;
            case "postgresql":
                await DeletePostgreSqlDatabaseAsync(databaseName, username);
                break;
            case "sqlserver":
                await DeleteSQLServerDatabaseAsync(databaseName, username);
                break;
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
        
        // 2. Crear el usuario
        var createUserCommand = new MySqlCommand(
            $"CREATE USER IF NOT EXISTS '{username}'@'%' IDENTIFIED BY '{password}';", 
            connection);
        await createUserCommand.ExecuteNonQueryAsync();

        // 3. Asignar permisos
        var grantCommand = new MySqlCommand(
            $"GRANT ALL PRIVILEGES ON `{databaseName}`.* TO '{username}'@'%';", 
            connection);
        await grantCommand.ExecuteNonQueryAsync();

        // 4. Aplicar cambios
        var flushCommand = new MySqlCommand("FLUSH PRIVILEGES;", connection);
        await flushCommand.ExecuteNonQueryAsync();
    }
    
    private async Task DeleteMySqlDatabaseAsync(string databaseName, string username)
    {
        var host = _configuration["MYSQL_HOST"];
        var port = _configuration["MYSQL_PORT"];
        var adminUser = _configuration["MYSQL_ADMIN_USER"];
        var adminPassword = _configuration["MYSQL_ADMIN_PASSWORD"];
        
        var adminConnectionString = 
            $"Server={host};Port={port};User={adminUser};Password={adminPassword};Database=mysql";
        
        using var connection = new MySqlConnection(adminConnectionString);
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

    private async Task CreatePostgreSqlDatabaseAsync(string databaseName, string username, string password)
    {
        var host = _configuration["POSTGRES_HOST"];
        var port = _configuration["POSTGRES_PORT"];
        var adminUser = _configuration["POSTGRES_ADMIN_USER"];
        var adminPassword = _configuration["POSTGRES_ADMIN_PASSWORD"];
        
        var adminConnectionString = 
            $"Host={host};Port={port};Username={adminUser};Password={adminPassword};Database=postgres";
        
        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync();

        // 1. Crear el usuario (rol)
        var createUserCommand = new NpgsqlCommand(
            $"DO $$ BEGIN IF NOT EXISTS (SELECT FROM pg_user WHERE usename = '{username}') THEN CREATE USER \"{username}\" WITH PASSWORD '{password}'; END IF; END $$;",
            connection);
        await createUserCommand.ExecuteNonQueryAsync();

        // 2. Crear la base de datos
        var createDbCommand = new NpgsqlCommand(
            $"CREATE DATABASE \"{databaseName}\" OWNER \"{username}\";",
            connection);
        
        try
        {
            await createDbCommand.ExecuteNonQueryAsync();
        }
        catch (PostgresException ex) when (ex.SqlState == "42P04") // Database already exists
        {
            // Si la base de datos ya existe, solo asignamos el owner
            var alterDbCommand = new NpgsqlCommand(
                $"ALTER DATABASE \"{databaseName}\" OWNER TO \"{username}\";",
                connection);
            await alterDbCommand.ExecuteNonQueryAsync();
        }

        // 3. Dar todos los privilegios al usuario
        var grantCommand = new NpgsqlCommand(
            $"GRANT ALL PRIVILEGES ON DATABASE \"{databaseName}\" TO \"{username}\";",
            connection);
        await grantCommand.ExecuteNonQueryAsync();
    }
    
    private async Task DeletePostgreSqlDatabaseAsync(string databaseName, string username)
    {
        var host = _configuration["POSTGRES_HOST"];
        var port = _configuration["POSTGRES_PORT"];
        var adminUser = _configuration["POSTGRES_ADMIN_USER"];
        var adminPassword = _configuration["POSTGRES_ADMIN_PASSWORD"];
        
        var adminConnectionString = 
            $"Host={host};Port={port};Username={adminUser};Password={adminPassword};Database=postgres";
        
        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync();

        // 1. Terminar todas las conexiones activas a esa base de datos
        var terminateConnectionsCommand = new NpgsqlCommand(
            $"SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '{databaseName}' AND pid <> pg_backend_pid();",
            connection);
        await terminateConnectionsCommand.ExecuteNonQueryAsync();

        // 2. Eliminar la base de datos
        var dropDbCommand = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{databaseName}\";", connection);
        await dropDbCommand.ExecuteNonQueryAsync();

        // 3. Eliminar el usuario
        var dropUserCommand = new NpgsqlCommand($"DROP USER IF EXISTS \"{username}\";", connection);
        await dropUserCommand.ExecuteNonQueryAsync();
    }

    private async Task CreateSQLServerDatabaseAsync(string databaseName, string username, string password)
    {
        var host = _configuration["SQLSERVER_HOST"];
        var port = _configuration["SQLSERVER_PORT"];
        var adminPassword = _configuration["SQLSERVER_ADMIN_PASSWORD"];
        
        var adminConnectionString = 
            $"Server={host},{port};User Id=sa;Password={adminPassword};TrustServerCertificate=True;";
        
        await using var connection = new SqlConnection(adminConnectionString);
        await connection.OpenAsync();

        // 1. Crear la base de datos
        var createDbCommand = new SqlCommand(
            $"IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = '{databaseName}') " +
            $"CREATE DATABASE [{databaseName}];",
            connection);
        await createDbCommand.ExecuteNonQueryAsync();

        // 2. Crear el login (a nivel de servidor)
        var createLoginCommand = new SqlCommand(
            $"IF NOT EXISTS (SELECT name FROM sys.server_principals WHERE name = '{username}') " +
            $"CREATE LOGIN [{username}] WITH PASSWORD = '{password}';",
            connection);
        await createLoginCommand.ExecuteNonQueryAsync();

        // 3. Cambiar al contexto de la base de datos creada
        var useDbCommand = new SqlCommand($"USE [{databaseName}];", connection);
        await useDbCommand.ExecuteNonQueryAsync();

        // 4. Crear el usuario en la base de datos
        var createUserCommand = new SqlCommand(
            $"IF NOT EXISTS (SELECT name FROM sys.database_principals WHERE name = '{username}') " +
            $"CREATE USER [{username}] FOR LOGIN [{username}];",
            connection);
        await createUserCommand.ExecuteNonQueryAsync();

        // 5. Asignar permisos de db_owner al usuario
        var grantPermissionsCommand = new SqlCommand(
            $"ALTER ROLE db_owner ADD MEMBER [{username}];",
            connection);
        await grantPermissionsCommand.ExecuteNonQueryAsync();
    }

    private async Task DeleteSQLServerDatabaseAsync(string databaseName, string username)
    {
        var host = _configuration["SQLSERVER_HOST"];
        var port = _configuration["SQLSERVER_PORT"];
        var adminPassword = _configuration["SQLSERVER_ADMIN_PASSWORD"];
        
        var adminConnectionString = 
            $"Server={host},{port};User Id=sa;Password={adminPassword};TrustServerCertificate=True;";
        
        await using var connection = new SqlConnection(adminConnectionString);
        await connection.OpenAsync();

        // 1. Cambiar a master para poder eliminar la base de datos
        var useMasterCommand = new SqlCommand("USE master;", connection);
        await useMasterCommand.ExecuteNonQueryAsync();

        // 2. Terminar todas las conexiones activas a la base de datos
        var killConnectionsCommand = new SqlCommand(
            $"IF EXISTS (SELECT name FROM sys.databases WHERE name = '{databaseName}') " +
            $"BEGIN " +
            $"  ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; " +
            $"END",
            connection);
        await killConnectionsCommand.ExecuteNonQueryAsync();

        // 3. Eliminar la base de datos
        var dropDbCommand = new SqlCommand(
            $"IF EXISTS (SELECT name FROM sys.databases WHERE name = '{databaseName}') " +
            $"DROP DATABASE [{databaseName}];",
            connection);
        await dropDbCommand.ExecuteNonQueryAsync();

        // 4. Eliminar el login
        var dropLoginCommand = new SqlCommand(
            $"IF EXISTS (SELECT name FROM sys.server_principals WHERE name = '{username}') " +
            $"DROP LOGIN [{username}];",
            connection);
        await dropLoginCommand.ExecuteNonQueryAsync();
    }
}
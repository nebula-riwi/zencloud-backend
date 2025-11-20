using Microsoft.Data.SqlClient;
using MongoDB.Bson;
using MongoDB.Driver;
using MySqlConnector;
using Npgsql;
using StackExchange.Redis;
using ZenCloud.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace ZenCloud.Services.Implementations;

public class DatabaseEngineService : IDatabaseEngineService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<DatabaseEngineService> _logger;

    public DatabaseEngineService(IConfiguration configuration, ILogger<DatabaseEngineService> logger)
    {
        _configuration = configuration;
        _logger = logger;
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
            case "mongodb":
                await CreateMongoDatabaseAsync(databaseName, username, password);
                break;
            case "redis":
                await CreateRedisDatabaseAsync(databaseName, username, password);
                break;
            case "sqlserver":
                await CreateSQLServerDatabaseAsync(databaseName, username, password);
                break;
            case "cassandra":
                await CreateCassandraKeyspaceAsync(databaseName, username, password);
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
            case "mongodb":
                await DeleteMongoDatabaseAsync(databaseName, username);
                break;
            case "redis":
                await DeleteRedisDatabaseAsync(databaseName, username);
                break;
            case "sqlserver":
                await DeleteSQLServerDatabaseAsync(databaseName, username);
                break;
            case "cassandra":
                await DeleteCassandraKeyspaceAsync(databaseName, username);
                break;
            default:
                throw new NotSupportedException($"Motor {engineName} no soportado");
        }
    }

    public async Task RotateCredentialsAsync(string engineName, string databaseName, string oldUsername, string newUsername, string newPassword)
    {
        switch (engineName.ToLower())
        {
            case "mysql":
                await RotateMySqlCredentialsAsync(databaseName, oldUsername, newUsername, newPassword);
                break;
            case "postgresql":
                await RotatePostgreSqlCredentialsAsync(databaseName, oldUsername, newUsername, newPassword);
                break;
            case "mongodb":
                await RotateMongoCredentialsAsync(databaseName, oldUsername, newUsername, newPassword);
                break;
            case "redis":
                await RotateRedisCredentialsAsync(databaseName, oldUsername, newUsername, newPassword);
                break;
            default:
                throw new NotSupportedException($"Motor {engineName} no soportado para rotación de credenciales");
        }
    }

    private async Task CreateMySqlDatabaseAsync(string databaseName, string username, string password)
    {
        var host = _configuration["MYSQL_HOST"] ?? throw new InvalidOperationException("MYSQL_HOST no configurado");
        var port = _configuration["MYSQL_PORT"] ?? throw new InvalidOperationException("MYSQL_PORT no configurado");
        var adminUser = _configuration["MYSQL_ADMIN_USER"] ?? throw new InvalidOperationException("MYSQL_ADMIN_USER no configurado");
        var adminPassword = _configuration["MYSQL_ADMIN_PASSWORD"] ?? throw new InvalidOperationException("MYSQL_ADMIN_PASSWORD no configurado");
        
        if (string.IsNullOrWhiteSpace(databaseName) || string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            throw new ArgumentException("Database name, username y password son requeridos");
        }
        
        var adminConnectionString = 
            $"Server={host};Port={port};User={adminUser};Password={adminPassword};Database=mysql;Connection Timeout=30;";
        
        using var connection = new MySqlConnection(adminConnectionString);
        try
        {
            await connection.OpenAsync();
        }
        catch (MySqlException mysqlEx)
        {
            var errorMessage = mysqlEx.ErrorCode switch
            {
                MySqlErrorCode.UnableToConnectToHost => $"No se puede conectar al servidor MySQL en {host}:{port}. Verifica que el servidor esté corriendo y el puerto sea correcto.",
                MySqlErrorCode.AccessDenied => "Credenciales de administrador incorrectas. Verifica MYSQL_ADMIN_USER y MYSQL_ADMIN_PASSWORD.",
                _ => $"Error conectando a MySQL: {mysqlEx.Message}"
            };
            _logger.LogError(mysqlEx, "Error conectando a MySQL: {Host}:{Port}", host, port);
            throw new InvalidOperationException(errorMessage, mysqlEx);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error inesperado conectando a MySQL: {Host}:{Port}", host, port);
            throw new InvalidOperationException($"Error conectando a MySQL en {host}:{port}: {ex.Message}", ex);
        }
        
        try
        {
            // 1. Crear la base de datos
            var createDbCommand = new MySqlCommand($"CREATE DATABASE IF NOT EXISTS `{databaseName}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;", connection);
        await createDbCommand.ExecuteNonQueryAsync();
            _logger.LogInformation("Base de datos MySQL '{DatabaseName}' creada o ya existe", databaseName);
        
        // 2. Crear el usuario
        var createUserCommand = new MySqlCommand(
            $"CREATE USER IF NOT EXISTS '{username}'@'%' IDENTIFIED BY '{password}';", 
            connection);
        await createUserCommand.ExecuteNonQueryAsync();
            _logger.LogInformation("Usuario MySQL '{Username}' creado o ya existe", username);

        // 3. Asignar permisos
        var grantCommand = new MySqlCommand(
            $"GRANT ALL PRIVILEGES ON `{databaseName}`.* TO '{username}'@'%';", 
            connection);
        await grantCommand.ExecuteNonQueryAsync();
            _logger.LogInformation("Permisos otorgados a '{Username}' en '{DatabaseName}'", username, databaseName);

        // 4. Aplicar cambios
        var flushCommand = new MySqlCommand("FLUSH PRIVILEGES;", connection);
        await flushCommand.ExecuteNonQueryAsync();
            _logger.LogInformation("Privilegios aplicados");
        }
        catch (MySqlException ex)
        {
            throw new InvalidOperationException($"Error creando base de datos MySQL: {ex.Message}", ex);
        }
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
        // Usar POSTGRES_USER_HOST para crear bases de usuarios, si no existe usar POSTGRES_HOST
        var host = _configuration["POSTGRES_USER_HOST"] ?? _configuration["POSTGRES_HOST"] ?? throw new InvalidOperationException("POSTGRES_USER_HOST o POSTGRES_HOST no configurado");
        var port = _configuration["POSTGRES_USER_PORT"] ?? _configuration["POSTGRES_PORT"] ?? throw new InvalidOperationException("POSTGRES_USER_PORT o POSTGRES_PORT no configurado");
        var adminUser = _configuration["POSTGRES_ADMIN_USER"] ?? throw new InvalidOperationException("POSTGRES_ADMIN_USER no configurado");
        var adminPassword = _configuration["POSTGRES_ADMIN_PASSWORD"] ?? throw new InvalidOperationException("POSTGRES_ADMIN_PASSWORD no configurado");
        
        if (string.IsNullOrWhiteSpace(databaseName) || string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            throw new ArgumentException("Database name, username y password son requeridos");
        }
        
        var adminConnectionString = 
            $"Host={host};Port={port};Username={adminUser};Password={adminPassword};Database=postgres;Timeout=30;";
        
        await using var connection = new NpgsqlConnection(adminConnectionString);
        try
        {
            await connection.OpenAsync();
        }
        catch (NpgsqlException npgsqlEx) when (npgsqlEx.InnerException is System.Net.Sockets.SocketException socketEx)
        {
            var errorMessage = socketEx.SocketErrorCode == System.Net.Sockets.SocketError.ConnectionRefused
                ? $"No se puede conectar al servidor PostgreSQL en {host}:{port}. El servidor no está disponible o no está corriendo. Verifica que el contenedor 'postgres-ZenDb' esté activo."
                : $"Error de conexión a PostgreSQL en {host}:{port}: {socketEx.Message}";
            
            _logger.LogError(npgsqlEx, "Error conectando a PostgreSQL: {Host}:{Port} - {ErrorMessage}", host, port, errorMessage);
            throw new InvalidOperationException(errorMessage, npgsqlEx);
        }
        catch (NpgsqlException npgsqlEx)
        {
            var errorMessage = $"Error conectando a PostgreSQL en {host}:{port}: {npgsqlEx.Message}";
            _logger.LogError(npgsqlEx, "Error de PostgreSQL: {Host}:{Port}", host, port);
            throw new InvalidOperationException(errorMessage, npgsqlEx);
        }
        catch (Exception ex)
        {
            var errorMessage = $"Error inesperado conectando a PostgreSQL en {host}:{port}: {ex.Message}";
            _logger.LogError(ex, "Error inesperado conectando a PostgreSQL: {Host}:{Port}", host, port);
            throw new InvalidOperationException(errorMessage, ex);
        }

        try
        {
            // Escapar identificadores para PostgreSQL (usar comillas dobles)
            var escapedUsername = $"\"{username.Replace("\"", "\"\"")}\"";
            var escapedDatabaseName = $"\"{databaseName.Replace("\"", "\"\"")}\"";

        // 1. Crear el usuario (rol)
        var createUserCommand = new NpgsqlCommand(
                $"DO $$ BEGIN IF NOT EXISTS (SELECT FROM pg_catalog.pg_user WHERE usename = '{username}') THEN CREATE USER {escapedUsername} WITH PASSWORD '{password.Replace("'", "''")}'; END IF; END $$;",
            connection);
        await createUserCommand.ExecuteNonQueryAsync();
            _logger.LogInformation("Usuario PostgreSQL '{Username}' creado o ya existe", username);

        // 2. Crear la base de datos
        var createDbCommand = new NpgsqlCommand(
                $"CREATE DATABASE {escapedDatabaseName} OWNER {escapedUsername};",
            connection);
        
        try
        {
            await createDbCommand.ExecuteNonQueryAsync();
                _logger.LogInformation("Base de datos PostgreSQL '{DatabaseName}' creada", databaseName);
        }
        catch (PostgresException ex) when (ex.SqlState == "42P04") // Database already exists
        {
            // Si la base de datos ya existe, solo asignamos el owner
            var alterDbCommand = new NpgsqlCommand(
                    $"ALTER DATABASE {escapedDatabaseName} OWNER TO {escapedUsername};",
                connection);
            await alterDbCommand.ExecuteNonQueryAsync();
                _logger.LogInformation("Base de datos PostgreSQL '{DatabaseName}' ya existe, owner actualizado", databaseName);
        }

        // 3. Dar todos los privilegios al usuario
        var grantCommand = new NpgsqlCommand(
                $"GRANT ALL PRIVILEGES ON DATABASE {escapedDatabaseName} TO {escapedUsername};",
            connection);
        await grantCommand.ExecuteNonQueryAsync();
            _logger.LogInformation("Permisos otorgados a '{Username}' en '{DatabaseName}'", username, databaseName);
        }
        catch (PostgresException ex)
        {
            throw new InvalidOperationException($"Error creando base de datos PostgreSQL: {ex.Message} (SQL State: {ex.SqlState})", ex);
        }
    }
    
    private async Task DeletePostgreSqlDatabaseAsync(string databaseName, string username)
    {
        // Usar POSTGRES_USER_HOST para eliminar bases de usuarios
        var host = _configuration["POSTGRES_USER_HOST"] ?? _configuration["POSTGRES_HOST"];
        var port = _configuration["POSTGRES_USER_PORT"] ?? _configuration["POSTGRES_PORT"];
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

    private async Task CreateMongoDatabaseAsync(string databaseName, string username, string password)
    {
        var host = _configuration["MONGO_HOST"];
        var port = _configuration["MONGO_PORT"];
        var adminUser = _configuration["MONGO_ADMIN_USER"] ?? "root";
        var adminPassword = _configuration["MONGO_ADMIN_PASSWORD"];
        
        var encodedPassword = Uri.EscapeDataString(adminPassword);

        // Conexión administrativa al servidor MongoDB
        var adminConnectionString =
            $"mongodb://{adminUser}:{encodedPassword}@{host}:{port}/?authSource=admin";
        
        var client = new MongoClient(adminConnectionString);
        
        var targetDatabase = client.GetDatabase(databaseName);
        
        var roles = new List<BsonDocument> {
            new BsonDocument {
                { "role", "dbOwner" },
                { "db", databaseName }
            }
        };
        
        try
        {
            // Crea el usuario en la base de datos de destino. Si la DB no existe, se crea.
            await targetDatabase.RunCommandAsync((Command<BsonDocument>)$"{{ createUser: \"{username}\", pwd: \"{password}\", roles: {BsonArray.Create(roles).ToJson()} }}");
        }
        catch (MongoWriteException ex) when (ex.WriteConcernError?.Code == 51003)
        {
            // Código de error 51003: User already exists
            _logger.LogInformation("Usuario '{Username}' ya existe. Ignorando creación.", username);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Error al crear la base de datos y usuario en MongoDB: {ex.Message}", ex);
        }
    }

    private async Task DeleteMongoDatabaseAsync(string databaseName, string username)
    {
        var host = _configuration["MONGO_HOST"];
        var port = _configuration["MONGO_PORT"];
        var adminUser = _configuration["MONGO_ADMIN_USER"] ?? "root";
        var adminPassword = _configuration["MONGO_ADMIN_PASSWORD"];
        
        var encodedPassword = Uri.EscapeDataString(adminPassword);

        // Conexión administrativa al servidor MongoDB
        var adminConnectionString =
            $"mongodb://{adminUser}:{encodedPassword}@{host}:{port}/?authSource=admin";
        
        var client = new MongoClient(adminConnectionString);
        
        try
        {
            var targetDatabase = client.GetDatabase(databaseName);
            // El comando dropUser debe ejecutarse en la base de datos donde se creó el usuario
            await targetDatabase.RunCommandAsync((Command<BsonDocument>)$"{{ dropUser: \"{username}\" }}");
        }
        catch (MongoCommandException ex) when (ex.Code == 11)
        {
            // Código de error 11: User not found
            _logger.LogInformation("Usuario '{Username}' no encontrado en DB '{DatabaseName}'. Ignorando eliminación de usuario.", username, databaseName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error al eliminar el usuario '{Username}'", username);
        }

        // 2. Eliminar la base de datos
        try
        {
            await client.DropDatabaseAsync(databaseName);
        }
        catch (MongoCommandException ex) when (ex.Code == 59)
        {
            // Código de error 59: Command failed (a menudo si la DB no existía)
            _logger.LogWarning("Error al eliminar la base de datos '{DatabaseName}'. Puede que no existiera.", databaseName);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Error al eliminar la base de datos en MongoDB: {ex.Message}", ex);
        }
    }
    private async Task CreateRedisDatabaseAsync(string databaseName, string username, string password)
    {
        var host = _configuration["REDIS_HOST"];
        var port = _configuration["REDIS_PORT"];
        var adminPassword = _configuration["REDIS_ADMIN_PASSWORD"];

        // 1. Conexión administrativa
        // Se conecta usando las credenciales del usuario por defecto o administrador
        var config = new ConfigurationOptions()
        {
            EndPoints = { { host, int.Parse(port) } },
            Password = adminPassword,
            AllowAdmin = true // Necesario para comandos administrativos como ACL
        };

        using var connection = await ConnectionMultiplexer.ConnectAsync(config);
        var server = connection.GetServer(host, int.Parse(port));

        try
        {
            // En Redis, la "creación" de una base de datos numerada (ej. DB 1) es implícita.
            // La seguridad se basa en ACL. Aquí creamos un usuario/contraseña.

            // 2. Crear un nuevo usuario (ACL SETUSER)
            // Se asume que le damos acceso a todos los comandos (+) y todas las claves (&*).
            // Para mayor seguridad, se debería restringir a un número de DB (>0) o a prefijos de claves (~databaseName:*).
            var aclCommand = $"ACL SETUSER {username} on >{password} +@all &*";

            // Para restringir el acceso solo a una DB numérica (ej. DB 1), sería:
            // var aclCommand = $"ACL SETUSER {username} on >{password} +@all ~* +select +swapdb -debug +config |{databaseNumber}";
            
            await server.ExecuteAsync("ACL", new[] { "SETUSER", username, "on", $">{password}", "+@all", "&*" });
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Error al crear usuario ACL en Redis: {ex.Message}", ex);
        }
    }

    private async Task DeleteRedisDatabaseAsync(string databaseName, string username)
    {
        var host = _configuration["REDIS_HOST"];
        var port = _configuration["REDIS_PORT"];
        var adminPassword = _configuration["REDIS_ADMIN_PASSWORD"];

        // 1. Conexión administrativa
        var config = new ConfigurationOptions
        {
            EndPoints = { { host, int.Parse(port) } },
            Password = adminPassword,
            AllowAdmin = true // Necesario para comandos administrativos
        };

        using var connection = await ConnectionMultiplexer.ConnectAsync(config);
        var server = connection.GetServer(host, int.Parse(port));

        // 2. Eliminar el contenido de la DB numérica (si se usa un esquema numérico)
        // ADVERTENCIA: Esta parte depende de tu esquema. Si el cliente usa una DB numérica dedicada, 
        // puedes usar FLUSHDB en esa DB. Si usa prefijos de clave, deberías eliminarlos por lotes.
        
        // Asumiendo que "databaseName" corresponde a un índice numérico de DB (0-15)
        // Ejemplo: Si el databaseName es "DB1", el índice es 1. 
        if (int.TryParse(databaseName, out int dbIndex))
        {
            try
            {
                // Limpia *todos* los datos de la base de datos numérica.
                await server.FlushDatabaseAsync(dbIndex);
            }
            catch (Exception ex)
            {
                 // No es crítico si falla la limpieza de datos
                 _logger.LogWarning(ex, "Falló FLUSHDB en Redis DB {DbIndex}", dbIndex);
            }
        }
        
        // 3. Eliminar el usuario ACL
        try
        {
            await server.ExecuteAsync("ACL", "DELUSER", username);
        }
        catch (Exception ex)
        {
            // No es crítico si falla la eliminación del usuario (podría no existir)
            _logger.LogWarning(ex, "Error al eliminar usuario ACL '{Username}'", username);
        }
    }

    private async Task RotateMySqlCredentialsAsync(string databaseName, string oldUsername, string newUsername, string newPassword)
    {
        var host = _configuration["MYSQL_HOST"] ?? throw new InvalidOperationException("MYSQL_HOST no configurado");
        var port = _configuration["MYSQL_PORT"] ?? throw new InvalidOperationException("MYSQL_PORT no configurado");
        var adminUser = _configuration["MYSQL_ADMIN_USER"] ?? throw new InvalidOperationException("MYSQL_ADMIN_USER no configurado");
        var adminPassword = _configuration["MYSQL_ADMIN_PASSWORD"] ?? throw new InvalidOperationException("MYSQL_ADMIN_PASSWORD no configurado");
        
        var adminConnectionString = 
            $"Server={host};Port={port};User={adminUser};Password={adminPassword};Database=mysql;Connection Timeout=30;";
        
        using var connection = new MySqlConnection(adminConnectionString);
        await connection.OpenAsync();

        try
        {
            // 1. Crear el nuevo usuario
            var createUserCommand = new MySqlCommand(
                $"CREATE USER IF NOT EXISTS '{newUsername}'@'%' IDENTIFIED BY '{newPassword}';", 
                connection);
            await createUserCommand.ExecuteNonQueryAsync();
            _logger.LogInformation("Nuevo usuario MySQL '{NewUsername}' creado", newUsername);

            // 2. Asignar permisos al nuevo usuario
            var grantCommand = new MySqlCommand(
                $"GRANT ALL PRIVILEGES ON `{databaseName}`.* TO '{newUsername}'@'%';", 
                connection);
            await grantCommand.ExecuteNonQueryAsync();
            _logger.LogInformation("Permisos otorgados a '{NewUsername}' en '{DatabaseName}'", newUsername, databaseName);

            // 3. Eliminar el usuario antiguo (si existe y es diferente)
            if (oldUsername != newUsername)
            {
                var dropUserCommand = new MySqlCommand($"DROP USER IF EXISTS '{oldUsername}'@'%';", connection);
                await dropUserCommand.ExecuteNonQueryAsync();
                _logger.LogInformation("Usuario antiguo '{OldUsername}' eliminado", oldUsername);
            }
            else
            {
                // Si el usuario es el mismo, solo actualizar la contraseña
                var alterUserCommand = new MySqlCommand(
                    $"ALTER USER '{newUsername}'@'%' IDENTIFIED BY '{newPassword}';", 
                    connection);
                await alterUserCommand.ExecuteNonQueryAsync();
                _logger.LogInformation("Contraseña actualizada para '{NewUsername}'", newUsername);
            }

            // 4. Aplicar cambios
            var flushCommand = new MySqlCommand("FLUSH PRIVILEGES;", connection);
            await flushCommand.ExecuteNonQueryAsync();
            _logger.LogInformation("Privilegios aplicados");
        }
        catch (MySqlException ex)
        {
            throw new InvalidOperationException($"Error rotando credenciales MySQL: {ex.Message}", ex);
        }
    }

    private async Task RotatePostgreSqlCredentialsAsync(string databaseName, string oldUsername, string newUsername, string newPassword)
    {
        // Usar POSTGRES_USER_HOST para rotar credenciales de bases de usuarios
        var host = _configuration["POSTGRES_USER_HOST"] ?? _configuration["POSTGRES_HOST"] ?? throw new InvalidOperationException("POSTGRES_USER_HOST o POSTGRES_HOST no configurado");
        var port = _configuration["POSTGRES_USER_PORT"] ?? _configuration["POSTGRES_PORT"] ?? throw new InvalidOperationException("POSTGRES_USER_PORT o POSTGRES_PORT no configurado");
        var adminUser = _configuration["POSTGRES_ADMIN_USER"] ?? throw new InvalidOperationException("POSTGRES_ADMIN_USER no configurado");
        var adminPassword = _configuration["POSTGRES_ADMIN_PASSWORD"] ?? throw new InvalidOperationException("POSTGRES_ADMIN_PASSWORD no configurado");
        
        var adminConnectionString = 
            $"Host={host};Port={port};Username={adminUser};Password={adminPassword};Database=postgres;Timeout=30;";
        
        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync();

        try
        {
            var escapedNewUsername = $"\"{newUsername.Replace("\"", "\"\"")}\"";
            var escapedOldUsername = $"\"{oldUsername.Replace("\"", "\"\"")}\"";
            var escapedDatabaseName = $"\"{databaseName.Replace("\"", "\"\"")}\"";

            // 1. Crear el nuevo usuario (si no existe)
            var createUserCommand = new NpgsqlCommand(
                $"DO $$ BEGIN IF NOT EXISTS (SELECT FROM pg_catalog.pg_user WHERE usename = '{newUsername}') THEN CREATE USER {escapedNewUsername} WITH PASSWORD '{newPassword.Replace("'", "''")}'; END IF; END $$;",
                connection);
            await createUserCommand.ExecuteNonQueryAsync();
            _logger.LogInformation("Nuevo usuario PostgreSQL '{NewUsername}' creado o ya existe", newUsername);

            // 2. Asignar permisos al nuevo usuario
            var grantCommand = new NpgsqlCommand(
                $"GRANT ALL PRIVILEGES ON DATABASE {escapedDatabaseName} TO {escapedNewUsername};",
                connection);
            await grantCommand.ExecuteNonQueryAsync();
            _logger.LogInformation("Permisos otorgados a '{NewUsername}' en '{DatabaseName}'", newUsername, databaseName);

            // 3. Cambiar el owner de la base de datos al nuevo usuario
            var alterDbCommand = new NpgsqlCommand(
                $"ALTER DATABASE {escapedDatabaseName} OWNER TO {escapedNewUsername};",
                connection);
            await alterDbCommand.ExecuteNonQueryAsync();
            _logger.LogInformation("Owner de '{DatabaseName}' cambiado a '{NewUsername}'", databaseName, newUsername);

            // 4. Eliminar el usuario antiguo (si existe y es diferente)
            if (oldUsername != newUsername)
            {
                var dropUserCommand = new NpgsqlCommand($"DROP USER IF EXISTS {escapedOldUsername};", connection);
                await dropUserCommand.ExecuteNonQueryAsync();
                _logger.LogInformation("Usuario antiguo '{OldUsername}' eliminado", oldUsername);
            }
            else
            {
                // Si el usuario es el mismo, solo actualizar la contraseña
                var alterUserCommand = new NpgsqlCommand(
                    $"ALTER USER {escapedNewUsername} WITH PASSWORD '{newPassword.Replace("'", "''")}';",
                    connection);
                await alterUserCommand.ExecuteNonQueryAsync();
                _logger.LogInformation("Contraseña actualizada para '{NewUsername}'", newUsername);
            }
        }
        catch (PostgresException ex)
        {
            throw new InvalidOperationException($"Error rotando credenciales PostgreSQL: {ex.Message} (SQL State: {ex.SqlState})", ex);
        }
    }

    private async Task RotateMongoCredentialsAsync(string databaseName, string oldUsername, string newUsername, string newPassword)
    {
        var host = _configuration["MONGO_HOST"];
        var port = _configuration["MONGO_PORT"];
        var adminUser = _configuration["MONGO_ADMIN_USER"] ?? "root";
        var adminPassword = _configuration["MONGO_ADMIN_PASSWORD"];
        
        var encodedPassword = Uri.EscapeDataString(adminPassword);
        var adminConnectionString = $"mongodb://{adminUser}:{encodedPassword}@{host}:{port}/?authSource=admin";
        
        var client = new MongoClient(adminConnectionString);
        var targetDatabase = client.GetDatabase(databaseName);
        
        var roles = new List<BsonDocument> {
            new BsonDocument {
                { "role", "dbOwner" },
                { "db", databaseName }
            }
        };

        try
        {
            // 1. Crear el nuevo usuario
            await targetDatabase.RunCommandAsync((Command<BsonDocument>)$"{{ createUser: \"{newUsername}\", pwd: \"{newPassword}\", roles: {BsonArray.Create(roles).ToJson()} }}");
            _logger.LogInformation("Nuevo usuario MongoDB '{NewUsername}' creado", newUsername);

            // 2. Eliminar el usuario antiguo (si existe y es diferente)
            if (oldUsername != newUsername)
            {
                try
                {
                    await targetDatabase.RunCommandAsync((Command<BsonDocument>)$"{{ dropUser: \"{oldUsername}\" }}");
                    _logger.LogInformation("Usuario antiguo '{OldUsername}' eliminado", oldUsername);
                }
                catch (MongoCommandException ex) when (ex.Code == 11)
                {
                    _logger.LogInformation("Usuario antiguo '{OldUsername}' no encontrado. Continuando...", oldUsername);
                }
            }
            else
            {
                // Si el usuario es el mismo, actualizar la contraseña
                await targetDatabase.RunCommandAsync((Command<BsonDocument>)$"{{ updateUser: \"{newUsername}\", pwd: \"{newPassword}\" }}");
                _logger.LogInformation("Contraseña actualizada para '{NewUsername}'", newUsername);
            }
        }
        catch (MongoWriteException ex) when (ex.WriteConcernError?.Code == 51003)
        {
            // Usuario ya existe, actualizar contraseña
            await targetDatabase.RunCommandAsync((Command<BsonDocument>)$"{{ updateUser: \"{newUsername}\", pwd: \"{newPassword}\" }}");
            _logger.LogInformation("Contraseña actualizada para usuario existente '{NewUsername}'", newUsername);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Error rotando credenciales MongoDB: {ex.Message}", ex);
        }
    }

    private async Task RotateRedisCredentialsAsync(string databaseName, string oldUsername, string newUsername, string newPassword)
    {
        var host = _configuration["REDIS_HOST"];
        var port = _configuration["REDIS_PORT"];
        var adminPassword = _configuration["REDIS_ADMIN_PASSWORD"];

        var config = new ConfigurationOptions()
        {
            EndPoints = { { host, int.Parse(port) } },
            Password = adminPassword,
            AllowAdmin = true
        };

        using var connection = await ConnectionMultiplexer.ConnectAsync(config);
        var server = connection.GetServer(host, int.Parse(port));

        try
        {
            // 1. Crear el nuevo usuario ACL
            await server.ExecuteAsync("ACL", new[] { "SETUSER", newUsername, "on", $">{newPassword}", "+@all", "&*" });
            _logger.LogInformation("Nuevo usuario Redis '{NewUsername}' creado", newUsername);

            // 2. Eliminar el usuario antiguo (si existe y es diferente)
            if (oldUsername != newUsername)
            {
                try
                {
                    await server.ExecuteAsync("ACL", "DELUSER", oldUsername);
                    _logger.LogInformation("Usuario antiguo '{OldUsername}' eliminado", oldUsername);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error al eliminar usuario antiguo '{OldUsername}'", oldUsername);
                }
            }
            else
            {
                // Si el usuario es el mismo, actualizar la contraseña
                await server.ExecuteAsync("ACL", new[] { "SETUSER", newUsername, "on", $">{newPassword}", "+@all", "&*" });
                _logger.LogInformation("Contraseña actualizada para '{NewUsername}'", newUsername);
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Error rotando credenciales Redis: {ex.Message}", ex);
        }
    }

    // ==================== SQL SERVER ====================
    private async Task CreateSQLServerDatabaseAsync(string databaseName, string username, string password)
    {
        var host = _configuration["SQLSERVER_HOST"] ?? "sqlserver-ZenDb";
        var port = _configuration["SQLSERVER_PORT"] ?? "1433";
        var adminPassword = _configuration["SQLSERVER_ADMIN_PASSWORD"] ?? throw new InvalidOperationException("SQLSERVER_ADMIN_PASSWORD no configurado");

        var connectionString = $"Server={host},{port};User Id=sa;Password={adminPassword};TrustServerCertificate=true;Connection Timeout=30;";

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        // Crear base de datos
        var createDbCommand = $"IF NOT EXISTS (SELECT * FROM sys.databases WHERE name = '{databaseName}') CREATE DATABASE [{databaseName}]";
        await using (var cmd = new SqlCommand(createDbCommand, connection))
        {
            await cmd.ExecuteNonQueryAsync();
        }
        _logger.LogInformation("Base de datos SQL Server '{DatabaseName}' creada o ya existe", databaseName);

        // Cambiar a la base de datos creada
        await connection.ChangeDatabaseAsync(databaseName);

        // Crear login y usuario
        var createLoginCommand = $@"
            IF NOT EXISTS (SELECT * FROM sys.server_principals WHERE name = '{username}')
            CREATE LOGIN [{username}] WITH PASSWORD = '{password}'";
        await using (var cmd = new SqlCommand(createLoginCommand, connection))
        {
            await cmd.ExecuteNonQueryAsync();
        }

        var createUserCommand = $@"
            IF NOT EXISTS (SELECT * FROM sys.database_principals WHERE name = '{username}')
            CREATE USER [{username}] FOR LOGIN [{username}]";
        await using (var cmd2 = new SqlCommand(createUserCommand, connection))
        {
            await cmd2.ExecuteNonQueryAsync();
        }

        // Otorgar permisos
        var grantCommand = $"ALTER ROLE db_owner ADD MEMBER [{username}]";
        await using (var cmd3 = new SqlCommand(grantCommand, connection))
        {
            await cmd3.ExecuteNonQueryAsync();
        }

        _logger.LogInformation("Usuario SQL Server '{Username}' creado con permisos", username);
    }

    private async Task DeleteSQLServerDatabaseAsync(string databaseName, string username)
    {
        var host = _configuration["SQLSERVER_HOST"] ?? "sqlserver-ZenDb";
        var port = _configuration["SQLSERVER_PORT"] ?? "1433";
        var adminPassword = _configuration["SQLSERVER_ADMIN_PASSWORD"] ?? throw new InvalidOperationException("SQLSERVER_ADMIN_PASSWORD no configurado");

        var connectionString = $"Server={host},{port};User Id=sa;Password={adminPassword};TrustServerCertificate=true;Connection Timeout=30;";

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        // Eliminar usuario
        try
        {
            await connection.ChangeDatabaseAsync(databaseName);
            var dropUserCommand = $"IF EXISTS (SELECT * FROM sys.database_principals WHERE name = '{username}') DROP USER [{username}]";
            await using var cmd = new SqlCommand(dropUserCommand, connection);
            await cmd.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error eliminando usuario SQL Server '{Username}'", username);
        }

        // Volver a master y eliminar login
        await connection.ChangeDatabaseAsync("master");
        try
        {
            var dropLoginCommand = $"IF EXISTS (SELECT * FROM sys.server_principals WHERE name = '{username}') DROP LOGIN [{username}]";
            await using var cmd = new SqlCommand(dropLoginCommand, connection);
            await cmd.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error eliminando login SQL Server '{Username}'", username);
        }

        // Eliminar base de datos
        var dropDbCommand = $@"
            IF EXISTS (SELECT * FROM sys.databases WHERE name = '{databaseName}')
            BEGIN
                ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE [{databaseName}];
            END";
        await using (var cmd2 = new SqlCommand(dropDbCommand, connection))
        {
            await cmd2.ExecuteNonQueryAsync();
        }

        _logger.LogInformation("Base de datos SQL Server '{DatabaseName}' eliminada", databaseName);
    }

    // ==================== CASSANDRA ====================
    private async Task CreateCassandraKeyspaceAsync(string keyspaceName, string username, string password)
    {
        // Cassandra no requiere crear keyspace por adelantado desde aquí
        // Se crea cuando el usuario se conecta por primera vez
        // Solo loguear que está disponible
        _logger.LogInformation("Keyspace Cassandra '{KeyspaceName}' preparado para usuario '{Username}'", keyspaceName, username);
        await Task.CompletedTask;
    }

    private async Task DeleteCassandraKeyspaceAsync(string keyspaceName, string username)
    {
        // Cassandra: eliminación manual o script externo
        // Por simplicidad, solo loguear
        _logger.LogInformation("Keyspace Cassandra '{KeyspaceName}' marcado para eliminación", keyspaceName);
        await Task.CompletedTask;
    }
}
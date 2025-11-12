using Microsoft.Data.SqlClient;
using MongoDB.Bson;
using MongoDB.Driver;
using MySqlConnector;
using Npgsql;
using StackExchange.Redis;
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
            case "mongodb":
                await CreateMongoDatabaseAsync(databaseName, username, password);
                break;
            case "redis":
                await CreateRedisDatabaseAsync(databaseName, username, password);
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
            default:
                throw new NotSupportedException($"Motor {engineName} no soportado");
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
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Error conectando a MySQL: {ex.Message}", ex);
        }
        
        try
        {
            // 1. Crear la base de datos
            var createDbCommand = new MySqlCommand($"CREATE DATABASE IF NOT EXISTS `{databaseName}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;", connection);
            await createDbCommand.ExecuteNonQueryAsync();
            Console.WriteLine($"Base de datos MySQL '{databaseName}' creada o ya existe");
            
            // 2. Crear el usuario
            var createUserCommand = new MySqlCommand(
                $"CREATE USER IF NOT EXISTS '{username}'@'%' IDENTIFIED BY '{password}';", 
                connection);
            await createUserCommand.ExecuteNonQueryAsync();
            Console.WriteLine($"Usuario MySQL '{username}' creado o ya existe");

            // 3. Asignar permisos
            var grantCommand = new MySqlCommand(
                $"GRANT ALL PRIVILEGES ON `{databaseName}`.* TO '{username}'@'%';", 
                connection);
            await grantCommand.ExecuteNonQueryAsync();
            Console.WriteLine($"Permisos otorgados a '{username}' en '{databaseName}'");

            // 4. Aplicar cambios
            var flushCommand = new MySqlCommand("FLUSH PRIVILEGES;", connection);
            await flushCommand.ExecuteNonQueryAsync();
            Console.WriteLine($"Privilegios aplicados");
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
        var host = _configuration["POSTGRES_HOST"] ?? throw new InvalidOperationException("POSTGRES_HOST no configurado");
        var port = _configuration["POSTGRES_PORT"] ?? throw new InvalidOperationException("POSTGRES_PORT no configurado");
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
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Error conectando a PostgreSQL: {ex.Message}", ex);
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
            Console.WriteLine($"Usuario PostgreSQL '{username}' creado o ya existe");

            // 2. Crear la base de datos
            var createDbCommand = new NpgsqlCommand(
                $"CREATE DATABASE {escapedDatabaseName} OWNER {escapedUsername};",
                connection);
            
            try
            {
                await createDbCommand.ExecuteNonQueryAsync();
                Console.WriteLine($"Base de datos PostgreSQL '{databaseName}' creada");
            }
            catch (PostgresException ex) when (ex.SqlState == "42P04") // Database already exists
            {
                // Si la base de datos ya existe, solo asignamos el owner
                var alterDbCommand = new NpgsqlCommand(
                    $"ALTER DATABASE {escapedDatabaseName} OWNER TO {escapedUsername};",
                    connection);
                await alterDbCommand.ExecuteNonQueryAsync();
                Console.WriteLine($"Base de datos PostgreSQL '{databaseName}' ya existe, owner actualizado");
            }

            // 3. Dar todos los privilegios al usuario
            var grantCommand = new NpgsqlCommand(
                $"GRANT ALL PRIVILEGES ON DATABASE {escapedDatabaseName} TO {escapedUsername};",
                connection);
            await grantCommand.ExecuteNonQueryAsync();
            Console.WriteLine($"Permisos otorgados a '{username}' en '{databaseName}'");
        }
        catch (PostgresException ex)
        {
            throw new InvalidOperationException($"Error creando base de datos PostgreSQL: {ex.Message} (SQL State: {ex.SqlState})", ex);
        }
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
            Console.WriteLine($"Usuario '{username}' ya existe. Ignorando creación.");
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
            Console.WriteLine($"Usuario '{username}' no encontrado en DB '{databaseName}'. Ignorando eliminación de usuario.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Advertencia: Error al eliminar el usuario '{username}': {ex.Message}");
        }

        // 2. Eliminar la base de datos
        try
        {
            await client.DropDatabaseAsync(databaseName);
        }
        catch (MongoCommandException ex) when (ex.Code == 59)
        {
            // Código de error 59: Command failed (a menudo si la DB no existía)
            Console.WriteLine($"Advertencia: Error al eliminar la base de datos '{databaseName}'. Puede que no existiera.");
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
                 Console.WriteLine($"Advertencia: Falló FLUSHDB en Redis DB {dbIndex}. {ex.Message}");
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
            Console.WriteLine($"Advertencia: Error al eliminar usuario ACL '{username}': {ex.Message}");
        }
    }
}
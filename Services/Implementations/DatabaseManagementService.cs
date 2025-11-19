using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using Npgsql;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using ZenCloud.Data.DbContext;
using ZenCloud.Data.Entities;
using ZenCloud.Data.Repositories.Interfaces;
using ZenCloud.DTOs.DatabaseManagement;
using ZenCloud.Exceptions;
using ZenCloud.Services.Interfaces;

namespace ZenCloud.Services.Implementations
{
    public class DatabaseManagementService : IDatabaseManagementService
    {
        private readonly IMySQLConnectionManager _connectionManager;
        private readonly IQueryExecutor _queryExecutor;
        private readonly IAuditService _auditService;
        private readonly IEncryptionService _encryptionService;
        private readonly PgDbContext _context;
        private readonly ILogger<DatabaseManagementService> _logger;
        private readonly IDatabaseQueryHistoryRepository _queryHistoryRepository;
        private readonly IPostgresQueryExecutor _postgresQueryExecutor;
        private readonly ISQLServerQueryExecutor _sqlServerQueryExecutor;

        private readonly Regex _validIdentifierRegex = new Regex("^[a-zA-Z_][a-zA-Z0-9_]{0,63}$", RegexOptions.Compiled);

        private readonly Regex _sqlInjectionPattern = new Regex(@"(;(?!\s*$)|\-\-|#|/\*|\*/|union\s+select|drop\s+table|delete\s+from|grant\s+.*to|revoke\s+.*from|exec(\s+|ute)|xp_|sp_|load_file|outfile|dumpfile)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly string[] AllowedStatements = { "SELECT", "INSERT", "UPDATE", "DELETE", "SHOW", "DESCRIBE", "EXPLAIN", "CREATE", "ALTER", "DROP" };
        private const string AllowedStatementsDescription = "Puedes ejecutar consultas individuales de lectura o mantenimiento (SELECT, INSERT, UPDATE, DELETE, SHOW, DESCRIBE, EXPLAIN, CREATE, ALTER y DROP).";

        public DatabaseManagementService(
            IMySQLConnectionManager connectionManager,
            IQueryExecutor queryExecutor,
            IAuditService auditService,
            IPostgresQueryExecutor postgresQueryExecutor,
            ISQLServerQueryExecutor sqlServerQueryExecutor,
            IEncryptionService encryptionService,
            PgDbContext context,
            ILogger<DatabaseManagementService> logger,
            IDatabaseQueryHistoryRepository queryHistoryRepository)
        {
            _connectionManager = connectionManager;
            _queryExecutor = queryExecutor;
            _auditService = auditService;
            _postgresQueryExecutor = postgresQueryExecutor;
            _sqlServerQueryExecutor = sqlServerQueryExecutor;
            _encryptionService = encryptionService;
            _context = context;
            _logger = logger;
            _queryHistoryRepository = queryHistoryRepository;
        }

        public async Task<QueryResult> ExecuteQueryAsync(Guid instanceId, Guid userId, string query)
        {
            await ValidateUserAccessAsync(instanceId, userId);

            query = query.Trim();
            if (query.EndsWith(";"))
            {
                query = query[..^1].Trim();
            }

            ValidateCustomQuerySecurity(query);

            var instance = await GetDatabaseInstanceAsync(instanceId);
            var engineType = instance.Engine?.EngineName ?? throw new NotFoundException("Motor de base de datos no encontrado");

            var stopwatch = Stopwatch.StartNew();

            try
            {
                QueryResult result = engineType switch
                {
                    DatabaseEngineType.MySQL => await ExecuteMySqlQueryAsync(instance, query),
                    DatabaseEngineType.PostgreSQL => await ExecutePostgresQueryAsync(instance, query),
                    DatabaseEngineType.SQLServer => await ExecuteSQLServerQueryAsync(instance, query),
                    _ => throw new BadRequestException($"El motor {engineType} no soporta el editor SQL todavía")
                };

                stopwatch.Stop();

                await PersistQueryHistoryAsync(instance, userId, query, result.Success, result.Rows.Count, stopwatch.Elapsed.TotalMilliseconds, result.ErrorMessage);

                await _auditService.LogDatabaseEventAsync(
                    userId,
                    instanceId,
                    AuditAction.DatabaseUpdated,
                    $"Query executed: {GetQueryType(query)}, Success: {result.Success}, Rows: {result.Rows.Count}"
                );

                return result;
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("conectar") || ex.Message.Contains("Timeout"))
            {
                stopwatch.Stop();
                await PersistQueryHistoryAsync(instance, userId, query, false, null, stopwatch.Elapsed.TotalMilliseconds, ex.Message);
                _logger.LogWarning(ex, "Error de conexión ejecutando query para instancia {InstanceId}", instanceId);
                throw new BadRequestException(ex.Message);
            }
            catch (PostgresException ex)
            {
                stopwatch.Stop();
                await PersistQueryHistoryAsync(instance, userId, query, false, null, stopwatch.Elapsed.TotalMilliseconds, ex.Message);
                _logger.LogWarning(ex, "Error de PostgreSQL ejecutando query para instancia {InstanceId}", instanceId);
                throw new BadRequestException(ex.Message);
            }
            catch (NpgsqlException ex)
            {
                stopwatch.Stop();
                await PersistQueryHistoryAsync(instance, userId, query, false, null, stopwatch.Elapsed.TotalMilliseconds, ex.Message);
                _logger.LogWarning(ex, "Error de conexión PostgreSQL ejecutando query para instancia {InstanceId}", instanceId);
                throw new BadRequestException(ex.Message);
            }
            catch (TimeoutException ex)
            {
                stopwatch.Stop();
                var timeoutMessage = "La consulta tardó más de 60 segundos en ejecutarse y fue cancelada. Considera optimizar tu consulta o dividirla en consultas más pequeñas.";
                await PersistQueryHistoryAsync(instance, userId, query, false, null, stopwatch.Elapsed.TotalMilliseconds, timeoutMessage);
                _logger.LogWarning(ex, "Timeout ejecutando query para instancia {InstanceId}", instanceId);
                throw new BadRequestException(timeoutMessage);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                await PersistQueryHistoryAsync(instance, userId, query, false, null, stopwatch.Elapsed.TotalMilliseconds, ex.Message);
                _logger.LogError(ex, "Error ejecutando query para instancia {InstanceId}", instanceId);
                throw;
            }
        }

        public async Task<List<TableInfo>> GetTablesAsync(Guid instanceId, Guid userId)
        {
            var instance = await _context.DatabaseInstances
                .Include(i => i.Engine)
                .FirstOrDefaultAsync(x => x.InstanceId == instanceId);

            if (instance == null)
                throw new NotFoundException($"Base de datos con ID {instanceId} no encontrada");

            if (instance.UserId != userId)
                throw new UnauthorizedAccessException("No tienes acceso a esta base de datos");

            var decryptedPassword = _encryptionService.Decrypt(instance.DatabasePasswordHash) ?? string.Empty;
            bool hadNulls = decryptedPassword.IndexOf('\0') >= 0;
            decryptedPassword = decryptedPassword.Replace("\0", string.Empty);
            decryptedPassword = new string(decryptedPassword.Where(c => !char.IsControl(c) || c == '\n' || c == '\r' || c == '\t').ToArray()).Trim();

            _logger.LogDebug("Database connection: InstanceId={InstanceId} User={User} Host={Host} Port={Port} Db={Db} PwdLen={PwdLen} HadNulls={HadNulls}",
                instance.InstanceId, instance.DatabaseUser, instance.ServerIpAddress, instance.AssignedPort, instance.DatabaseName, decryptedPassword.Length, hadNulls);

            if (string.IsNullOrWhiteSpace(decryptedPassword))
            {
                _logger.LogError("Database password is empty after decryption/cleanup for instance {InstanceId}", instance.InstanceId);
                throw new BadRequestException("La contraseña de la base de datos es inválida después de la descifrado.");
            }

            if (instance.Engine == null)
            {
                throw new NotFoundException("Motor de base de datos no encontrado para esta instancia");
            }

            QueryResult result;
            try
            {
                result = instance.Engine.EngineName == DatabaseEngineType.PostgreSQL
                    ? await GetPostgreSQLTablesAsync(instance)
                    : await GetMySQLTablesAsync(instance);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("conectar") || ex.Message.Contains("Timeout"))
            {
                _logger.LogWarning(ex, "Error de conexión obteniendo tablas para instancia {InstanceId}", instanceId);
                throw new BadRequestException(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error obteniendo tablas para instancia {InstanceId}", instanceId);
                throw new BadRequestException($"Error al obtener las tablas: {ex.Message}");
            }

            if (!result.Success)
            {
                var errorMessage = !string.IsNullOrEmpty(result.ErrorMessage) 
                    ? result.ErrorMessage 
                    : "Error desconocido al obtener las tablas";
                throw new BadRequestException($"Error al obtener las tablas: {errorMessage}");
            }

            return result.Rows
                .Cast<object[]>()
                .Select(r => new TableInfo
                {
                    TableName = r[0]?.ToString() ?? "Unknown",
                    TableType = r.Length > 1 ? r[1]?.ToString() ?? "TABLE" : "TABLE",
                    RowCount = r.Length > 2 ? Convert.ToInt64(r[2] ?? 0) : 0,
                    CreateTime = r.Length > 3 && r[3] is DateTime dt ? dt : DateTime.UtcNow
                })
                .ToList();
        }

        private async Task<QueryResult> GetMySQLTablesAsync(DatabaseInstance instance)
        {
            var result = new QueryResult();
            try
            {
                var decryptedPassword = _encryptionService.Decrypt(instance.DatabasePasswordHash) ?? string.Empty;
                decryptedPassword = decryptedPassword.Replace("\0", string.Empty).Trim();

                // Si el backend está en Docker, usar el nombre del contenedor y puerto interno
                // Si no, usar la IP externa y puerto externo
                var host = instance.ServerIpAddress == "168.119.182.243" || instance.ServerIpAddress == "localhost" || instance.ServerIpAddress == "127.0.0.1"
                    ? "mysql-ZenDb" // Usar nombre del contenedor dentro de Docker
                    : instance.ServerIpAddress;
                var port = instance.ServerIpAddress == "168.119.182.243" || instance.ServerIpAddress == "localhost" || instance.ServerIpAddress == "127.0.0.1"
                    ? 3306u // Puerto interno de MySQL en Docker
                    : (uint)instance.AssignedPort;

                var builder = new MySqlConnector.MySqlConnectionStringBuilder
                {
                    Server = host,
                    Port = port,
                    Database = instance.DatabaseName,
                    UserID = instance.DatabaseUser,
                    Password = decryptedPassword,
                    ConnectionTimeout = 30,
                    DefaultCommandTimeout = 60
                };

                _logger.LogInformation("MySQL connstring prepared for instance {InstanceId}: user={User} host={Host} port={Port} pwdLen={PwdLen}",
                    instance.InstanceId, instance.DatabaseUser, host, port, decryptedPassword.Length);

                await using var connection = new MySqlConnector.MySqlConnection(builder.ConnectionString);
                
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                await connection.OpenAsync(cts.Token);

                const string query = @"
                    SELECT 
                        t.TABLE_NAME,
                        t.TABLE_TYPE,
                        COALESCE(t.TABLE_ROWS, 0) as RowCount,
                        t.CREATE_TIME
                    FROM INFORMATION_SCHEMA.TABLES t
                    WHERE t.TABLE_SCHEMA = DATABASE()
                    ORDER BY t.TABLE_NAME";

                await using var command = new MySqlConnector.MySqlCommand(query, connection);
                await using var reader = await command.ExecuteReaderAsync();

                for (int i = 0; i < reader.FieldCount; i++)
                    result.Columns.Add(reader.GetName(i));

                while (await reader.ReadAsync())
                {
                    var row = new object[reader.FieldCount];
                    for (int i = 0; i < reader.FieldCount; i++)
                        row[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                    result.Rows.Add(row);
                }

                result.Success = true;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                _logger.LogError(ex, "Error obteniendo tablas de MySQL para instanceId: {InstanceId}", instance.InstanceId);
            }

            return result;
        }

        private async Task<QueryResult> GetPostgreSQLTablesAsync(DatabaseInstance instance)
        {
            var result = new QueryResult();
            try
            {
                var decryptedPassword = _encryptionService.Decrypt(instance.DatabasePasswordHash) ?? string.Empty;
                decryptedPassword = decryptedPassword.Replace("\0", string.Empty).Trim();

                // Si el backend está en Docker, usar el nombre del contenedor y puerto interno
                // Si no, usar la IP externa y puerto externo
                var host = instance.ServerIpAddress == "168.119.182.243" || instance.ServerIpAddress == "localhost" || instance.ServerIpAddress == "127.0.0.1"
                    ? "postgres-ZenDb" // Usar nombre del contenedor dentro de Docker
                    : instance.ServerIpAddress;
                var port = instance.ServerIpAddress == "168.119.182.243" || instance.ServerIpAddress == "localhost" || instance.ServerIpAddress == "127.0.0.1"
                    ? 5432 // Puerto interno de PostgreSQL en Docker
                    : instance.AssignedPort;

                var pgBuilder = new Npgsql.NpgsqlConnectionStringBuilder
                {
                    Host = host,
                    Port = port,
                    Database = instance.DatabaseName,
                    Username = instance.DatabaseUser,
                    Password = decryptedPassword,
                    Timeout = 30
                };

                _logger.LogInformation("Postgres connstring prepared for instance {InstanceId}: user={User} host={Host} port={Port} pwdLen={PwdLen}",
                    instance.InstanceId, instance.DatabaseUser, host, port, decryptedPassword.Length);

                await using var connection = new Npgsql.NpgsqlConnection(pgBuilder.ConnectionString);
                await connection.OpenAsync();

                const string query = @"
                    SELECT 
                        t.tablename,
                        'BASE TABLE'::text as table_type,
                        COALESCE(s.n_live_tup, 0)::bigint as row_count,
                        CURRENT_TIMESTAMP as create_time
                    FROM pg_catalog.pg_tables t
                    LEFT JOIN pg_stat_user_tables s ON s.relname = t.tablename
                    WHERE t.schemaname = 'public'
                    ORDER BY t.tablename";

                await using var command = new Npgsql.NpgsqlCommand(query, connection);
                await using var reader = await command.ExecuteReaderAsync();

                for (int i = 0; i < reader.FieldCount; i++)
                    result.Columns.Add(reader.GetName(i));

                while (await reader.ReadAsync())
                {
                    var row = new object[reader.FieldCount];
                    for (int i = 0; i < reader.FieldCount; i++)
                        row[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                    result.Rows.Add(row);
                }

                result.Success = true;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                _logger.LogError(ex, "Error obteniendo tablas de PostgreSQL para instanceId: {InstanceId}", instance.InstanceId);
            }

            return result;
        }

        public async Task<TableSchema> GetTableSchemaAsync(Guid instanceId, Guid userId, string tableName)
        {
            await ValidateUserAccessAsync(instanceId, userId);

            if (!IsValidTableName(tableName))
                throw new ArgumentException("Nombre de tabla contiene caracteres inválidos");

            var instance = await GetDatabaseInstanceAsync(instanceId);
            var engineType = instance.Engine?.EngineName ?? throw new NotFoundException("Motor de base de datos no encontrado");

            if (!IsValidDatabaseIdentifier(instance.DatabaseName))
                throw new ArgumentException("Nombre de base de datos contiene caracteres inválidos");

            return engineType switch
            {
                DatabaseEngineType.MySQL => await GetMySqlTableSchemaAsync(instance, tableName),
                DatabaseEngineType.PostgreSQL => await GetPostgresTableSchemaAsync(instance, tableName),
                _ => throw new BadRequestException("El motor seleccionado no soporta la visualización del esquema todavía")
            };
        }

        private async Task<TableSchema> GetMySqlTableSchemaAsync(DatabaseInstance instance, string tableName)
        {
            await using var connection = await _connectionManager.GetConnectionAsync(instance);

            const string columnsQuery = @"
                SELECT 
                    COLUMN_NAME as ColumnName,
                    DATA_TYPE as DataType,
                    IS_NULLABLE as IsNullable,
                    COLUMN_DEFAULT as DefaultValue,
                    CHARACTER_MAXIMUM_LENGTH as MaxLength,
                    COLUMN_KEY as ColumnKey
                FROM INFORMATION_SCHEMA.COLUMNS 
                WHERE TABLE_SCHEMA = @databaseName 
                AND TABLE_NAME = @tableName
                ORDER BY ORDINAL_POSITION";

            var parameters = new { databaseName = instance.DatabaseName, tableName };
            var columnsResult = await ExecuteMySqlParameterizedQueryAsync(connection, columnsQuery, parameters);

            return new TableSchema
            {
                TableName = tableName,
                Columns = MapToColumnInfoList(columnsResult)
            };
        }

        private async Task<TableSchema> GetPostgresTableSchemaAsync(DatabaseInstance instance, string tableName)
        {
            await using var connection = await CreatePostgresConnectionAsync(instance);

            const string columnsQuery = @"
                SELECT 
                    c.column_name AS ColumnName,
                    c.data_type AS DataType,
                    c.is_nullable AS IsNullable,
                    c.column_default AS DefaultValue,
                    c.character_maximum_length AS MaxLength,
                    CASE WHEN tc.constraint_type = 'PRIMARY KEY' THEN 'PRI' ELSE '' END AS ColumnKey
                FROM information_schema.columns c
                LEFT JOIN information_schema.key_column_usage kcu
                    ON c.table_name = kcu.table_name
                    AND c.table_schema = kcu.table_schema
                    AND c.column_name = kcu.column_name
                LEFT JOIN information_schema.table_constraints tc
                    ON tc.constraint_name = kcu.constraint_name
                    AND tc.table_schema = kcu.table_schema
                    AND tc.table_name = kcu.table_name
                WHERE c.table_schema = 'public'
                AND c.table_name = @tableName
                ORDER BY c.ordinal_position";

            var parameters = new { tableName };
            var columnsResult = await ExecutePostgresParameterizedQueryAsync(connection, columnsQuery, parameters);

            return new TableSchema
            {
                TableName = tableName,
                Columns = MapToColumnInfoList(columnsResult)
            };
        }

        public async Task<QueryResult> GetTableDataAsync(Guid instanceId, Guid userId, string tableName, int limit = 100)
        {
            await ValidateUserAccessAsync(instanceId, userId);

            if (!IsValidTableName(tableName))
                throw new ArgumentException("Nombre de tabla contiene caracteres inválidos");

            if (limit <= 0 || limit > 1000)
                limit = 100;

            var instance = await GetDatabaseInstanceAsync(instanceId);
            var engineType = instance.Engine?.EngineName ?? throw new NotFoundException("Motor de base de datos no encontrado");

            QueryResult result = engineType switch
            {
                DatabaseEngineType.MySQL => await GetMySqlTableDataAsync(instance, tableName, limit),
                DatabaseEngineType.PostgreSQL => await GetPostgresTableDataAsync(instance, tableName, limit),
                _ => throw new BadRequestException("El motor seleccionado no soporta la visualización de datos todavía")
            };

            await _auditService.LogDatabaseEventAsync(
                userId,
                instanceId,
                AuditAction.DatabaseUpdated,
                $"Table data accessed: {tableName}, Rows: {result.Rows.Count}"
            );

            return result;
        }

        private async Task<QueryResult> GetMySqlTableDataAsync(DatabaseInstance instance, string tableName, int limit)
        {
            await using var connection = await _connectionManager.GetConnectionAsync(instance);

            var tableExistsQuery = @"
                SELECT COUNT(*) 
                FROM INFORMATION_SCHEMA.TABLES 
                WHERE TABLE_SCHEMA = @databaseName 
                AND TABLE_NAME = @tableName";

            var tableExistsParams = new { databaseName = instance.DatabaseName, tableName };
            var tableExistsResult = await ExecuteMySqlParameterizedQueryAsync(connection, tableExistsQuery, tableExistsParams);

            if (tableExistsResult.Rows.Count == 0 || (long)(tableExistsResult.Rows[0][0] ?? 0) == 0)
                throw new ArgumentException($"La tabla '{tableName}' no existe");

            var query = $"SELECT * FROM `{EscapeTableName(tableName)}` LIMIT {limit}";
            return await _queryExecutor.ExecuteSelectQueryAsync(connection, query, limit);
        }

        private async Task<QueryResult> GetPostgresTableDataAsync(DatabaseInstance instance, string tableName, int limit)
        {
            await using var connection = await CreatePostgresConnectionAsync(instance);

            var tableExistsQuery = @"
                SELECT COUNT(*) 
                FROM information_schema.tables 
                WHERE table_schema = 'public' 
                AND table_name = @tableName";

            var tableExistsParams = new { tableName };
            var tableExistsResult = await ExecutePostgresParameterizedQueryAsync(connection, tableExistsQuery, tableExistsParams);

            if (tableExistsResult.Rows.Count == 0 || Convert.ToInt64(tableExistsResult.Rows[0][0] ?? 0) == 0)
                throw new ArgumentException($"La tabla '{tableName}' no existe");

            var query = $"SELECT * FROM \"{EscapePostgresIdentifier(tableName)}\" LIMIT {limit}";
            return await _postgresQueryExecutor.ExecuteSelectQueryAsync(connection, query, limit);
        }

        public async Task<bool> TestConnectionAsync(Guid instanceId, Guid userId)
        {
            await ValidateUserAccessAsync(instanceId, userId);

            var instance = await GetDatabaseInstanceAsync(instanceId);
            var engineType = instance.Engine?.EngineName ?? throw new NotFoundException("Motor de base de datos no encontrado");

            bool result = engineType switch
            {
                DatabaseEngineType.MySQL => await _connectionManager.ValidateConnectionAsync(instance),
                DatabaseEngineType.PostgreSQL => await TestPostgresConnectionAsync(instance),
                _ => throw new BadRequestException("El motor seleccionado no soporta test de conexión")
            };

            await _auditService.LogDatabaseEventAsync(
                userId,
                instanceId,
                AuditAction.DatabaseStatusChanged,
                $"Connection tested, Success: {result}"
            );

            return result;
        }

        private async Task<bool> TestPostgresConnectionAsync(DatabaseInstance instance)
        {
            try
            {
                await using var connection = await CreatePostgresConnectionAsync(instance);
                await using var command = new NpgsqlCommand("SELECT 1", connection);
                var result = await command.ExecuteScalarAsync();
                return result?.ToString() == "1";
            }
            catch
            {
                return false;
            }
        }

        public async Task<DatabaseInfo> GetDatabaseInfoAsync(Guid instanceId, Guid userId)
        {
            await ValidateUserAccessAsync(instanceId, userId);

            var instance = await GetDatabaseInstanceAsync(instanceId);
            var engineType = instance.Engine?.EngineName ?? throw new NotFoundException("Motor de base de datos no encontrado");

            if (!IsValidDatabaseIdentifier(instance.DatabaseName))
                throw new ArgumentException("Nombre de base de datos contiene caracteres inválidos");

            return engineType switch
            {
                DatabaseEngineType.MySQL => await GetMySqlDatabaseInfoAsync(instance),
                DatabaseEngineType.PostgreSQL => await GetPostgresDatabaseInfoAsync(instance),
                _ => throw new BadRequestException("El motor seleccionado no soporta la obtención de información todavía")
            };
        }

        private async Task<DatabaseInfo> GetMySqlDatabaseInfoAsync(DatabaseInstance instance)
        {
            await using var connection = await _connectionManager.GetConnectionAsync(instance);

            var query = @"
                SELECT 
                    @@version as Version,
                    @@hostname as Hostname,
                    @@port as Port,
                    (SELECT SUM(data_length + index_length) 
                     FROM information_schema.TABLES 
                     WHERE table_schema = @databaseName) as TotalSize,
                    (SELECT COUNT(*) 
                     FROM information_schema.TABLES 
                     WHERE table_schema = @databaseName) as TableCount,
                    @@character_set_database as CharacterSet,
                    @@collation_database as Collation";

            var parameters = new { databaseName = instance.DatabaseName };
            var result = await ExecuteMySqlParameterizedQueryAsync(connection, query, parameters);

            if (!result.Success || result.Rows.Count == 0)
                throw new Exception("Failed to get database information");

            return new DatabaseInfo
            {
                Name = instance.DatabaseName,
                Version = result.Rows[0][0]?.ToString() ?? "Unknown",
                Hostname = result.Rows[0][1]?.ToString() ?? "Unknown",
                Port = int.Parse(result.Rows[0][2]?.ToString() ?? "0"),
                TotalSize = decimal.Parse(result.Rows[0][3]?.ToString() ?? "0"),
                TableCount = int.Parse(result.Rows[0][4]?.ToString() ?? "0"),
                CharacterSet = result.Rows[0][5]?.ToString() ?? "Unknown",
                Collation = result.Rows[0][6]?.ToString() ?? "Unknown"
            };
        }

        private async Task<DatabaseInfo> GetPostgresDatabaseInfoAsync(DatabaseInstance instance)
        {
            await using var connection = await CreatePostgresConnectionAsync(instance);

            var query = @"
                SELECT 
                    version() as Version,
                    inet_server_addr() as Hostname,
                    inet_server_port() as Port,
                    pg_database_size(@databaseName) as TotalSize,
                    (SELECT COUNT(*) 
                     FROM information_schema.tables 
                     WHERE table_schema = 'public') as TableCount,
                    pg_encoding_to_char(encoding) as CharacterSet,
                    datcollate as Collation
                FROM pg_database
                WHERE datname = @databaseName";

            var parameters = new { databaseName = instance.DatabaseName };
            var result = await ExecutePostgresParameterizedQueryAsync(connection, query, parameters);

            if (!result.Success || result.Rows.Count == 0)
                throw new Exception("Failed to get database information");

            return new DatabaseInfo
            {
                Name = instance.DatabaseName,
                Version = result.Rows[0][0]?.ToString() ?? "Unknown",
                Hostname = result.Rows[0][1]?.ToString() ?? "Unknown",
                Port = result.Rows[0][2] != null ? int.Parse(result.Rows[0][2]?.ToString() ?? "0") : instance.AssignedPort,
                TotalSize = result.Rows[0][3] != null ? Convert.ToDecimal(result.Rows[0][3]) / (1024 * 1024) : 0,
                TableCount = int.Parse(result.Rows[0][4]?.ToString() ?? "0"),
                CharacterSet = result.Rows[0][5]?.ToString() ?? "UTF8",
                Collation = result.Rows[0][6]?.ToString() ?? "Unknown"
            };
        }

        public async Task<List<DatabaseProcess>> GetProcessListAsync(Guid instanceId, Guid userId)
        {
            await ValidateUserAccessAsync(instanceId, userId);

            var instance = await GetDatabaseInstanceAsync(instanceId);
            var engineType = instance.Engine?.EngineName ?? throw new NotFoundException("Motor de base de datos no encontrado");

            List<DatabaseProcess> processes = engineType switch
            {
                DatabaseEngineType.MySQL => await GetMySqlProcessListAsync(instance),
                DatabaseEngineType.PostgreSQL => await GetPostgresProcessListAsync(instance),
                _ => throw new BadRequestException("El motor seleccionado no soporta la lista de procesos todavía")
            };

            await _auditService.LogDatabaseEventAsync(
                userId,
                instanceId,
                AuditAction.DatabaseStatusChanged,
                $"Process list accessed, Count: {processes.Count}"
            );

            return processes;
        }

        private async Task<List<DatabaseProcess>> GetMySqlProcessListAsync(DatabaseInstance instance)
        {
            await using var connection = await _connectionManager.GetConnectionAsync(instance);

            var query = "SHOW PROCESSLIST";
            var result = await _queryExecutor.ExecuteSafeQueryAsync(connection, query);

            var processes = new List<DatabaseProcess>();
            foreach (var row in result.Rows)
            {
                processes.Add(new DatabaseProcess
                {
                    Id = int.Parse(row[0]?.ToString() ?? "0"),
                    User = row[1]?.ToString() ?? "Unknown",
                    Host = row[2]?.ToString() ?? "Unknown",
                    Database = row[3]?.ToString(),
                    Command = row[4]?.ToString() ?? "Unknown",
                    Time = int.Parse(row[5]?.ToString() ?? "0"),
                    State = row[6]?.ToString(),
                    Info = row[7]?.ToString()
                });
            }

            return processes;
        }

        private async Task<List<DatabaseProcess>> GetPostgresProcessListAsync(DatabaseInstance instance)
        {
            await using var connection = await CreatePostgresConnectionAsync(instance);

            var query = @"
                SELECT 
                    pid,
                    usename,
                    client_addr::text,
                    datname,
                    state,
                    EXTRACT(EPOCH FROM (NOW() - state_change))::int,
                    wait_event_type,
                    query
                FROM pg_stat_activity
                WHERE datname = @databaseName
                ORDER BY pid";

            var parameters = new { databaseName = instance.DatabaseName };
            var result = await ExecutePostgresParameterizedQueryAsync(connection, query, parameters);

            var processes = new List<DatabaseProcess>();
            foreach (var row in result.Rows)
            {
                processes.Add(new DatabaseProcess
                {
                    Id = int.Parse(row[0]?.ToString() ?? "0"),
                    User = row[1]?.ToString() ?? "Unknown",
                    Host = row[2]?.ToString() ?? "Unknown",
                    Database = row[3]?.ToString(),
                    Command = row[4]?.ToString() ?? "Unknown",
                    Time = int.Parse(row[5]?.ToString() ?? "0"),
                    State = row[6]?.ToString(),
                    Info = row[7]?.ToString()
                });
            }

            return processes;
        }

        public async Task<bool> KillProcessAsync(Guid instanceId, Guid userId, int processId)
        {
            await ValidateUserAccessAsync(instanceId, userId);

            if (processId <= 0)
                throw new ArgumentException("ID de proceso inválido");

            var instance = await GetDatabaseInstanceAsync(instanceId);
            var engineType = instance.Engine?.EngineName ?? throw new NotFoundException("Motor de base de datos no encontrado");

            bool result = engineType switch
            {
                DatabaseEngineType.MySQL => await KillMySqlProcessAsync(instance, processId),
                DatabaseEngineType.PostgreSQL => await KillPostgresProcessAsync(instance, processId),
                _ => throw new BadRequestException("El motor seleccionado no soporta la eliminación de procesos todavía")
            };

            await _auditService.LogDatabaseEventAsync(
                userId,
                instanceId,
                AuditAction.SystemConfigChanged,
                $"Process killed: {processId}"
            );

            return result;
        }

        private async Task<bool> KillMySqlProcessAsync(DatabaseInstance instance, int processId)
        {
            await using var connection = await _connectionManager.GetConnectionAsync(instance);
            var query = "KILL @processId";

            using var command = new MySqlCommand(query, connection);
            command.Parameters.AddWithValue("@processId", processId);
            await command.ExecuteNonQueryAsync();

            return true;
        }

        private async Task<bool> KillPostgresProcessAsync(DatabaseInstance instance, int processId)
        {
            await using var connection = await CreatePostgresConnectionAsync(instance);
            var query = "SELECT pg_terminate_backend(@processId)";

            await using var command = new NpgsqlCommand(query, connection);
            command.Parameters.AddWithValue("@processId", processId);
            var result = await command.ExecuteScalarAsync();

            return result != null && (bool)result;
        }

        public async Task<IReadOnlyList<QueryHistoryItemDto>> GetQueryHistoryAsync(Guid instanceId, Guid userId, int limit = 20)
        {
            await ValidateUserAccessAsync(instanceId, userId);
            var cappedLimit = Math.Clamp(limit, 1, 100);
            var entries = await _queryHistoryRepository.GetRecentByUserAndInstanceAsync(userId, instanceId, cappedLimit);

            return entries
                .Select(entry => new QueryHistoryItemDto
                {
                    Id = entry.QueryHistoryId,
                    Query = entry.QueryText,
                    Success = entry.IsSuccess,
                    RowCount = entry.RowCount,
                    ExecutionTimeMs = entry.ExecutionTimeMs,
                    Error = entry.ErrorMessage,
                    ExecutedAt = entry.ExecutedAt,
                    DatabaseName = entry.Instance?.DatabaseName,
                    EngineType = entry.EngineType?.ToString()
                })
                .ToList();
        }

        public async Task<DatabaseExportResult> ExportDatabaseAsync(Guid instanceId, Guid userId)
        {
            await ValidateUserAccessAsync(instanceId, userId);
            var instance = await GetDatabaseInstanceAsync(instanceId);

            if (instance.Engine == null)
            {
                throw new NotFoundException("Motor de base de datos no encontrado");
            }

            // Solo permitir exportación de motores relacionales
            if (instance.Engine.EngineName != DatabaseEngineType.MySQL && 
                instance.Engine.EngineName != DatabaseEngineType.PostgreSQL &&
                instance.Engine.EngineName != DatabaseEngineType.SQLServer)
            {
                throw new ForbiddenException($"La exportación no está disponible para el motor {instance.Engine.EngineName}. Solo está disponible para MySQL, PostgreSQL y SQL Server.");
            }

            // Use MemoryStream for backward compatibility
            using var memoryStream = new MemoryStream();
            await ExportDatabaseToStreamAsync(instanceId, userId, memoryStream);
            
            return new DatabaseExportResult
            {
                Content = memoryStream.ToArray(),
                FileName = $"{instance.DatabaseName}_{DateTime.UtcNow:yyyyMMddHHmmss}.sql"
            };
        }

        public async Task ExportDatabaseToStreamAsync(Guid instanceId, Guid userId, Stream outputStream)
        {
            await ValidateUserAccessAsync(instanceId, userId);
            var instance = await GetDatabaseInstanceAsync(instanceId);

            if (instance.Engine == null)
            {
                throw new NotFoundException("Motor de base de datos no encontrado");
            }

            // Solo permitir exportación de motores relacionales
            if (instance.Engine.EngineName != DatabaseEngineType.MySQL && 
                instance.Engine.EngineName != DatabaseEngineType.PostgreSQL &&
                instance.Engine.EngineName != DatabaseEngineType.SQLServer)
            {
                throw new ForbiddenException($"La exportación no está disponible para el motor {instance.Engine.EngineName}. Solo está disponible para MySQL, PostgreSQL y SQL Server.");
            }

            var writer = new StreamWriter(outputStream, Encoding.UTF8, leaveOpen: true);

            try
            {
                switch (instance.Engine.EngineName)
                {
                    case DatabaseEngineType.MySQL:
                        await ExportMySqlToStreamAsync(instance, writer);
                        break;
                    case DatabaseEngineType.PostgreSQL:
                        await ExportPostgreSqlToStreamAsync(instance, writer);
                        break;
                    case DatabaseEngineType.SQLServer:
                        await ExportSQLServerToStreamAsync(instance, writer);
                        break;
                    default:
                        throw new ForbiddenException("Motor no soportado para exportación");
                }

                await writer.FlushAsync();
            }
            finally
            {
                await writer.FlushAsync();
            }
        }

        private async Task<string> ExportMySqlAsync(DatabaseInstance instance)
        {
            await using var connection = await _connectionManager.GetConnectionAsync(instance);
            return await BuildMySqlDumpAsync(connection, instance.DatabaseName);
        }

        private async Task<string> ExportPostgreSqlAsync(DatabaseInstance instance)
        {
            await using var connection = await CreatePostgresConnectionAsync(instance);
            return await BuildPostgresDumpAsync(connection, instance.DatabaseName);
        }

        private async Task ExportMySqlToStreamAsync(DatabaseInstance instance, StreamWriter writer)
        {
            await using var connection = await _connectionManager.GetConnectionAsync(instance);
            await BuildMySqlDumpToStreamAsync(connection, instance.DatabaseName, writer);
        }

        private async Task ExportPostgreSqlToStreamAsync(DatabaseInstance instance, StreamWriter writer)
        {
            await using var connection = await CreatePostgresConnectionAsync(instance);
            await BuildPostgresDumpToStreamAsync(connection, instance.DatabaseName, writer);
        }

        #region Métodos de Seguridad

        private bool IsValidDatabaseIdentifier(string identifier)
        {
            if (string.IsNullOrWhiteSpace(identifier))
                return false;

            if (!_validIdentifierRegex.IsMatch(identifier))
                return false;

            var reservedWords = new[] { "mysql", "information_schema", "performance_schema", "sys", "test" };
            if (reservedWords.Contains(identifier.ToLower()))
                return false;

            return true;
        }

        private bool IsValidTableName(string tableName)
        {
            if (string.IsNullOrWhiteSpace(tableName))
                return false;

            if (!_validIdentifierRegex.IsMatch(tableName))
                return false;

            return true;
        }

        private string EscapeTableName(string tableName)
        {
            if (!IsValidTableName(tableName))
                throw new ArgumentException("Nombre de tabla inválido");

            return tableName.Replace("`", "``");
        }

        private static string EscapePostgresIdentifier(string identifier)
        {
            if (string.IsNullOrWhiteSpace(identifier))
                throw new ArgumentException("Identificador inválido");

            return identifier.Replace("\"", "\"\"");
        }

        private async Task<QueryResult> ExecuteMySqlQueryAsync(DatabaseInstance instance, string query)
        {
            await using var connection = await _connectionManager.GetConnectionAsync(instance);
            return await _queryExecutor.ExecuteSafeQueryAsync(connection, query);
        }

        private async Task<QueryResult> ExecutePostgresQueryAsync(DatabaseInstance instance, string query)
        {
            await using var connection = await CreatePostgresConnectionAsync(instance);
            return await _postgresQueryExecutor.ExecuteSafeQueryAsync(connection, query);
        }

        private async Task<QueryResult> ExecuteSQLServerQueryAsync(DatabaseInstance instance, string query)
        {
            await using var connection = await CreateSQLServerConnectionAsync(instance);
            return await _sqlServerQueryExecutor.ExecuteSafeQueryAsync(connection, query);
        }

        private void ValidateCustomQuerySecurity(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                throw new ArgumentException("La consulta no puede estar vacía");

            if (query.Length > 10000)
                throw new InvalidOperationException("La consulta es demasiado larga. Divide la instrucción o limita la cantidad de columnas/filas.");

            var queryToValidate = query.TrimEnd();
            if (queryToValidate.EndsWith(";"))
                queryToValidate = queryToValidate[..^1].Trim();

            if (_sqlInjectionPattern.IsMatch(queryToValidate))
                throw new InvalidOperationException($"La consulta contiene patrones que no están permitidos por seguridad. {AllowedStatementsDescription}");

            var firstWord = queryToValidate.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.ToUpper();

            if (firstWord == null || !AllowedStatements.Contains(firstWord))
                throw new InvalidOperationException($"Tipo de consulta no permitido ({firstWord ?? "desconocido"}). {AllowedStatementsDescription}");

            if (queryToValidate.Contains(';'))
                throw new InvalidOperationException("Solo se permite ejecutar una consulta por vez. Elimina los puntos y coma intermedios para evitar múltiples sentencias.");
        }

        private async Task<QueryResult> ExecuteMySqlParameterizedQueryAsync(MySqlConnection connection, string query, object parameters)
        {
            var result = new QueryResult();

            try
            {
                using var command = new MySqlCommand(query, connection);
                command.CommandType = CommandType.Text;

                foreach (var prop in parameters.GetType().GetProperties())
                {
                    var paramName = "@" + prop.Name;
                    var paramValue = prop.GetValue(parameters) ?? DBNull.Value;
                    command.Parameters.AddWithValue(paramName, paramValue);
                }

                using var reader = await command.ExecuteReaderAsync();

                for (int i = 0; i < reader.FieldCount; i++)
                {
                    result.Columns.Add(reader.GetName(i));
                }

                while (await reader.ReadAsync())
                {
                    var row = new object[reader.FieldCount];
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        row[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                    }
                    result.Rows.Add(row);
                }

                result.Success = true;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                _logger.LogError(ex, "Error executing MySQL parameterized query: {Query}", query);
            }

            return result;
        }

        private async Task<QueryResult> ExecutePostgresParameterizedQueryAsync(NpgsqlConnection connection, string query, object parameters)
        {
            var result = new QueryResult();

            try
            {
                await using var command = new NpgsqlCommand(query, connection);
                command.CommandType = CommandType.Text;

                foreach (var prop in parameters.GetType().GetProperties())
                {
                    var paramName = "@" + prop.Name;
                    var paramValue = prop.GetValue(parameters) ?? DBNull.Value;
                    command.Parameters.AddWithValue(paramName, paramValue);
                }

                await using var reader = await command.ExecuteReaderAsync();

                for (int i = 0; i < reader.FieldCount; i++)
                {
                    result.Columns.Add(reader.GetName(i));
                }

                while (await reader.ReadAsync())
                {
                    var row = new object[reader.FieldCount];
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        row[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                    }
                    result.Rows.Add(row);
                }

                result.Success = true;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                _logger.LogError(ex, "Error executing PostgreSQL parameterized query: {Query}", query);
            }

            return result;
        }

        #endregion

        #region Métodos Auxiliares

        private async Task<string> BuildMySqlDumpAsync(MySqlConnection connection, string databaseName)
        {
            var builder = new StringBuilder();
            builder.AppendLine("-- ZenCloud SQL Export");
            builder.AppendLine($"-- Database: `{databaseName}`");
            builder.AppendLine($"-- Generated at: {DateTime.UtcNow:O}");
            builder.AppendLine("SET FOREIGN_KEY_CHECKS=0;");

            var tables = new List<string>();
            using (var showTablesCommand = new MySqlCommand("SHOW TABLES;", connection))
            using (var reader = await showTablesCommand.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    tables.Add(reader.GetString(0));
                }
            }

            foreach (var table in tables)
            {
                builder.AppendLine();
                builder.AppendLine($"-- ----------------------------");
                builder.AppendLine($"-- Table structure for `{table}`");
                builder.AppendLine($"DROP TABLE IF EXISTS `{table}`;");

                using (var createCommand = new MySqlCommand($"SHOW CREATE TABLE `{table}`;", connection))
                using (var createReader = await createCommand.ExecuteReaderAsync())
                {
                    if (await createReader.ReadAsync())
                    {
                        var createStatement = createReader.GetString(1);
                        builder.AppendLine($"{createStatement};");
                    }
                }

                builder.AppendLine();
                builder.AppendLine($"-- Data for table `{table}`");

                using (var dataCommand = new MySqlCommand($"SELECT * FROM `{table}`;", connection))
                using (var dataReader = await dataCommand.ExecuteReaderAsync())
                {
                    while (await dataReader.ReadAsync())
                    {
                        var values = new string[dataReader.FieldCount];
                        for (int i = 0; i < dataReader.FieldCount; i++)
                        {
                            values[i] = FormatSqlValue(dataReader.IsDBNull(i) ? null : dataReader.GetValue(i));
                        }

                        builder.AppendLine($"INSERT INTO `{table}` VALUES ({string.Join(", ", values)});");
                    }
                }
            }

            builder.AppendLine("SET FOREIGN_KEY_CHECKS=1;");
            return builder.ToString();
        }

        private async Task<NpgsqlConnection> CreatePostgresConnectionAsync(DatabaseInstance instance)
        {
            var decryptedPassword = _encryptionService.Decrypt(instance.DatabasePasswordHash) ?? string.Empty;
            decryptedPassword = decryptedPassword.Replace("\0", string.Empty);

            var connectionBuilder = new NpgsqlConnectionStringBuilder
            {
                Host = string.IsNullOrWhiteSpace(instance.ServerIpAddress) ? "127.0.0.1" : instance.ServerIpAddress,
                Port = instance.AssignedPort > 0 ? instance.AssignedPort : 5432,
                Database = instance.DatabaseName,
                Username = instance.DatabaseUser,
                Password = decryptedPassword,
                Timeout = 30,
                CommandTimeout = 60,
                Pooling = true, // Habilitar pooling para mejor rendimiento
                MaxPoolSize = 10, // Límite de conexiones en el pool
                MinPoolSize = 0
            };

            var connection = new NpgsqlConnection(connectionBuilder.ConnectionString);
            await connection.OpenAsync();
            return connection;
        }

        private async Task<SqlConnection> CreateSQLServerConnectionAsync(DatabaseInstance instance)
        {
            var decryptedPassword = _encryptionService.Decrypt(instance.DatabasePasswordHash) ?? string.Empty;
            decryptedPassword = decryptedPassword.Replace("\0", string.Empty);

            var connectionBuilder = new SqlConnectionStringBuilder
            {
                DataSource = $"{(string.IsNullOrWhiteSpace(instance.ServerIpAddress) ? "127.0.0.1" : instance.ServerIpAddress)},{(instance.AssignedPort > 0 ? instance.AssignedPort : 1433)}",
                InitialCatalog = instance.DatabaseName,
                UserID = instance.DatabaseUser,
                Password = decryptedPassword,
                ConnectTimeout = 30,
                TrustServerCertificate = true,
                Pooling = true,
                MaxPoolSize = 10,
                MinPoolSize = 0
            };

            var connection = new SqlConnection(connectionBuilder.ConnectionString);
            await connection.OpenAsync();
            return connection;
        }

        private async Task<string> BuildPostgresDumpAsync(NpgsqlConnection connection, string databaseName)
        {
            var builder = new StringBuilder();
            builder.AppendLine("-- ZenCloud SQL Export (PostgreSQL)");
            builder.AppendLine($"-- Database: \"{databaseName}\"");
            builder.AppendLine($"-- Generated at: {DateTime.UtcNow:O}");
            builder.AppendLine("SET statement_timeout = 0;");
            builder.AppendLine("SET lock_timeout = 0;");
            builder.AppendLine("SET client_encoding = 'UTF8';");
            builder.AppendLine("SET standard_conforming_strings = on;");
            builder.AppendLine();

            var tables = new List<string>();
            const string tableSql = @"
                SELECT table_name
                FROM information_schema.tables
                WHERE table_schema = 'public' AND table_type = 'BASE TABLE'
                ORDER BY table_name;";

            await using (var command = new NpgsqlCommand(tableSql, connection))
            await using (var reader = await command.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    tables.Add(reader.GetString(0));
                }
            }

            foreach (var table in tables)
            {
                builder.AppendLine();
                builder.AppendLine($"-- Table: \"{table}\"");
                builder.AppendLine($"DROP TABLE IF EXISTS \"{table}\" CASCADE;");

                var columnDefinitions = await GetPostgresColumnDefinitionsAsync(connection, table);
                builder.AppendLine($"CREATE TABLE \"{table}\" (");
                builder.AppendLine(string.Join(",\n", columnDefinitions.Select(column => $"    {column}")));
                builder.AppendLine(");");

                var primaryKeys = await GetPostgresPrimaryKeyColumnsAsync(connection, table);
                if (primaryKeys.Count > 0)
                {
                    builder.AppendLine($"ALTER TABLE ONLY \"{table}\" ADD CONSTRAINT \"{table}_pkey\" PRIMARY KEY ({string.Join(", ", primaryKeys.Select(pk => $"\"{pk}\""))});");
                }

                await AppendPostgresTableDataAsync(connection, table, builder);
            }

            return builder.ToString();
        }

        private async Task<List<string>> GetPostgresColumnDefinitionsAsync(NpgsqlConnection connection, string table)
        {
            const string columnsSql = @"
                SELECT column_name,
                       data_type,
                       is_nullable,
                       character_maximum_length,
                       numeric_precision,
                       numeric_scale,
                       column_default
                FROM information_schema.columns
                WHERE table_schema = 'public' AND table_name = @table
                ORDER BY ordinal_position;";

            var definitions = new List<string>();
            await using var command = new NpgsqlCommand(columnsSql, connection);
            command.Parameters.AddWithValue("table", table);

            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var columnName = reader.GetString(0);
                var dataType = reader.GetString(1);
                var isNullable = reader.GetString(2).Equals("YES", StringComparison.OrdinalIgnoreCase);
                var charLength = reader.IsDBNull(3) ? (int?)null : reader.GetInt32(3);
                var numericPrecision = reader.IsDBNull(4) ? (int?)null : reader.GetInt32(4);
                var numericScale = reader.IsDBNull(5) ? (int?)null : reader.GetInt32(5);
                var defaultValue = reader.IsDBNull(6) ? null : reader.GetString(6);

                var resolvedType = ResolvePostgresColumnType(dataType, charLength, numericPrecision, numericScale);
                var definition = $"\"{columnName}\" {resolvedType}";

                if (!string.IsNullOrWhiteSpace(defaultValue))
                {
                    definition += $" DEFAULT {defaultValue}";
                }

                if (!isNullable)
                {
                    definition += " NOT NULL";
                }

                definitions.Add(definition);
            }

            return definitions;
        }

        private static string ResolvePostgresColumnType(string dataType, int? charLength, int? numericPrecision, int? numericScale)
        {
            return dataType switch
            {
                "character varying" or "varchar" or "character" or "char" => charLength.HasValue ? $"{dataType}({charLength.Value})" : dataType,
                "numeric" or "decimal" => numericPrecision.HasValue
                    ? $"{dataType}({numericPrecision.Value}{(numericScale.HasValue ? $", {numericScale.Value}" : string.Empty)})"
                    : dataType,
                _ => dataType
            };
        }

        private async Task<List<string>> GetPostgresPrimaryKeyColumnsAsync(NpgsqlConnection connection, string table)
        {
            const string pkSql = @"
                SELECT a.attname
                FROM pg_index i
                JOIN pg_class c ON c.oid = i.indrelid
                JOIN pg_namespace n ON n.oid = c.relnamespace
                JOIN pg_attribute a ON a.attrelid = c.oid AND a.attnum = ANY(i.indkey)
                WHERE i.indisprimary = true
                    AND n.nspname = 'public'
                    AND c.relname = @table
                ORDER BY a.attnum;";

            var columns = new List<string>();
            await using var command = new NpgsqlCommand(pkSql, connection);
            command.Parameters.AddWithValue("table", table);

            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                columns.Add(reader.GetString(0));
            }

            return columns;
        }

        private async Task AppendPostgresTableDataAsync(NpgsqlConnection connection, string table, StringBuilder builder)
        {
            builder.AppendLine();
            builder.AppendLine($"-- Data for table \"{table}\"");

            var selectSql = $"SELECT * FROM \"{table}\";";
            await using var command = new NpgsqlCommand(selectSql, connection);
            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var values = new string[reader.FieldCount];
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    values[i] = FormatSqlValue(reader.IsDBNull(i) ? null : reader.GetValue(i), usePostgresSyntax: true);
                }

                builder.AppendLine($"INSERT INTO \"{table}\" VALUES ({string.Join(", ", values)});");
            }
        }

        private async Task BuildMySqlDumpToStreamAsync(MySqlConnection connection, string databaseName, StreamWriter writer)
        {
            await writer.WriteLineAsync("-- ZenCloud SQL Export");
            await writer.WriteLineAsync($"-- Database: `{databaseName}`");
            await writer.WriteLineAsync($"-- Generated at: {DateTime.UtcNow:O}");
            await writer.WriteLineAsync("SET FOREIGN_KEY_CHECKS=0;");
            await writer.FlushAsync(); // Flush inicial

            var tables = new List<string>();
            using (var showTablesCommand = new MySqlCommand("SHOW TABLES;", connection))
            using (var reader = await showTablesCommand.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    tables.Add(reader.GetString(0));
                }
            }

            foreach (var table in tables)
            {
                await writer.WriteLineAsync();
                await writer.WriteLineAsync("-- ----------------------------");
                await writer.WriteLineAsync($"-- Table structure for `{table}`");
                await writer.WriteLineAsync($"DROP TABLE IF EXISTS `{table}`;");

                using (var createCommand = new MySqlCommand($"SHOW CREATE TABLE `{table}`;", connection))
                using (var createReader = await createCommand.ExecuteReaderAsync())
                {
                    if (await createReader.ReadAsync())
                    {
                        var createStatement = createReader.GetString(1);
                        await writer.WriteLineAsync($"{createStatement};");
                    }
                }

                await writer.WriteLineAsync();
                await writer.WriteLineAsync($"-- Data for table `{table}`");
                await writer.FlushAsync(); // Flush después de estructura

                int rowCount = 0;
                using (var dataCommand = new MySqlCommand($"SELECT * FROM `{table}`;", connection))
                using (var dataReader = await dataCommand.ExecuteReaderAsync())
                {
                    while (await dataReader.ReadAsync())
                    {
                        var values = new string[dataReader.FieldCount];
                        for (int i = 0; i < dataReader.FieldCount; i++)
                        {
                            values[i] = FormatSqlValue(dataReader.IsDBNull(i) ? null : dataReader.GetValue(i));
                        }

                        await writer.WriteLineAsync($"INSERT INTO `{table}` VALUES ({string.Join(", ", values)});");
                        
                        // Flush cada 100 filas para mantener el streaming activo
                        rowCount++;
                        if (rowCount % 100 == 0)
                        {
                            await writer.FlushAsync();
                        }
                    }
                }
                
                await writer.FlushAsync(); // Flush al final de cada tabla
            }

            await writer.WriteLineAsync("SET FOREIGN_KEY_CHECKS=1;");
            await writer.FlushAsync(); // Flush final
        }

        private async Task BuildPostgresDumpToStreamAsync(NpgsqlConnection connection, string databaseName, StreamWriter writer)
        {
            await writer.WriteLineAsync("-- ZenCloud SQL Export (PostgreSQL)");
            await writer.WriteLineAsync($"-- Database: \"{databaseName}\"");
            await writer.WriteLineAsync($"-- Generated at: {DateTime.UtcNow:O}");
            await writer.WriteLineAsync("SET statement_timeout = 0;");
            await writer.WriteLineAsync("SET lock_timeout = 0;");
            await writer.WriteLineAsync("SET client_encoding = 'UTF8';");
            await writer.WriteLineAsync("SET standard_conforming_strings = on;");
            await writer.WriteLineAsync();
            await writer.FlushAsync(); // Flush inicial

            var tables = new List<string>();
            const string tableSql = @"
                SELECT table_name
                FROM information_schema.tables
                WHERE table_schema = 'public' AND table_type = 'BASE TABLE'
                ORDER BY table_name;";

            // Read table names first and close the reader
            await using (var tableCommand = new NpgsqlCommand(tableSql, connection))
            {
                await using var tableReader = await tableCommand.ExecuteReaderAsync();
                while (await tableReader.ReadAsync())
                {
                    tables.Add(tableReader.GetString(0));
                }
            } // Reader is disposed here

            foreach (var table in tables)
            {
                await writer.WriteLineAsync();
                await writer.WriteLineAsync($"-- Table: \"{table}\"");
                await writer.WriteLineAsync($"DROP TABLE IF EXISTS \"{table}\" CASCADE;");

                var columnDefinitions = await GetPostgresColumnDefinitionsAsync(connection, table);
                await writer.WriteLineAsync($"CREATE TABLE \"{table}\" (");
                var columnDefs = string.Join(",\n", columnDefinitions.Select(column => $"    {column}"));
                await writer.WriteLineAsync(columnDefs);
                await writer.WriteLineAsync(");");

                var primaryKeys = await GetPostgresPrimaryKeyColumnsAsync(connection, table);
                if (primaryKeys.Count > 0)
                {
                    var pkColumns = string.Join(", ", primaryKeys.Select(pk => $"\"{pk}\""));
                    await writer.WriteLineAsync($"ALTER TABLE ONLY \"{table}\" ADD CONSTRAINT \"{table}_pkey\" PRIMARY KEY ({pkColumns});");
                }

                await writer.FlushAsync(); // Flush después de estructura

                await AppendPostgresTableDataToStreamAsync(connection, table, writer);
                
                await writer.FlushAsync(); // Flush al final de cada tabla
            }
        }

        private async Task AppendPostgresTableDataToStreamAsync(NpgsqlConnection connection, string table, StreamWriter writer)
        {
            await writer.WriteLineAsync();
            await writer.WriteLineAsync($"-- Data for table \"{table}\"");

            var selectSql = $"SELECT * FROM \"{table}\";";
            await using var command = new NpgsqlCommand(selectSql, connection);
            await using var reader = await command.ExecuteReaderAsync();

            int rowCount = 0;
            while (await reader.ReadAsync())
            {
                var values = new string[reader.FieldCount];
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    values[i] = FormatSqlValue(reader.IsDBNull(i) ? null : reader.GetValue(i), usePostgresSyntax: true);
                }

                await writer.WriteLineAsync($"INSERT INTO \"{table}\" VALUES ({string.Join(", ", values)});");
                
                // Flush cada 100 filas para mantener el streaming activo
                rowCount++;
                if (rowCount % 100 == 0)
                {
                    await writer.FlushAsync();
                }
            }
        }

        private string FormatSqlValue(object? value, bool usePostgresSyntax = false)
        {
            if (value == null || value == DBNull.Value)
            {
                return "NULL";
            }

            switch (value)
            {
                case bool booleanValue:
                    return usePostgresSyntax ? (booleanValue ? "TRUE" : "FALSE") : (booleanValue ? "1" : "0");
                case byte or sbyte or short or ushort or int or uint or long or ulong:
                    return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "0";
                case float or double or decimal:
                    return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "0";
                case DateTime dateTime:
                    return $"'{dateTime:yyyy-MM-dd HH:mm:ss}'";
                case byte[] bytes:
                    var hex = BitConverter.ToString(bytes).Replace("-", string.Empty);
                    return usePostgresSyntax
                        ? $"E'\\\\x{hex.ToLowerInvariant()}'"
                        : $"X'{hex}'";
                default:
                    var stringValue = Convert.ToString(value) ?? string.Empty;
                    var escaped = usePostgresSyntax
                        ? stringValue.Replace("'", "''")
                        : MySqlHelper.EscapeString(stringValue);
                    return $"'{escaped}'";
            }
        }

        private async Task PersistQueryHistoryAsync(DatabaseInstance instance, Guid userId, string query, bool success, int? rowCount, double elapsedMilliseconds, string? errorMessage)
        {
            try
            {
                var sanitizedQuery = query.Length > 8000 ? query[..8000] : query;
                var entry = new DatabaseQueryHistory
                {
                    QueryHistoryId = Guid.NewGuid(),
                    InstanceId = instance.InstanceId,
                    UserId = userId,
                    QueryText = sanitizedQuery,
                    IsSuccess = success,
                    RowCount = rowCount,
                    ExecutionTimeMs = Math.Round(elapsedMilliseconds, 2),
                    ErrorMessage = success ? null : errorMessage,
                    ExecutedAt = DateTime.UtcNow,
                    EngineType = instance.Engine?.EngineName
                };

                await _queryHistoryRepository.AddAsync(entry);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to persist query history for instance {InstanceId}", instance.InstanceId);
            }
        }

        private async Task ValidateUserAccessAsync(Guid instanceId, Guid userId)
        {
            var instance = await _context.DatabaseInstances
                .FirstOrDefaultAsync(di => di.InstanceId == instanceId && di.UserId == userId);

            if (instance == null)
                throw new UnauthorizedAccessException("User does not have access to this database instance");
        }

        private async Task<DatabaseInstance> GetDatabaseInstanceAsync(Guid instanceId)
        {
            // Optimización: usar proyección para solo cargar campos necesarios cuando solo necesitamos el Engine
            // Sin embargo, como necesitamos devolver la instancia completa, mantenemos Include
            // pero podríamos optimizar si solo necesitamos el EngineName
            var instance = await _context.DatabaseInstances
                .Include(di => di.Engine)
                .AsNoTracking() // Optimización: solo lectura para este método
                .FirstOrDefaultAsync(di => di.InstanceId == instanceId);

            if (instance == null)
                throw new KeyNotFoundException($"Database instance {instanceId} not found");

            return instance;
        }

        private string GetQueryType(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return "UNKNOWN";

            var firstWord = query.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault()?
                .ToUpper();

            return firstWord switch
            {
                "SELECT" => "SELECT",
                "SHOW" => "SHOW",
                "DESCRIBE" => "DESCRIBE",
                "EXPLAIN" => "EXPLAIN",
                _ => "OTHER"
            };
        }

        private List<TableInfo> MapToTableInfoList(QueryResult result)
        {
            var tables = new List<TableInfo>();
            foreach (var row in result.Rows)
            {
                tables.Add(new TableInfo
                {
                    TableName = row[0]?.ToString() ?? string.Empty,
                    TableType = row[1]?.ToString() ?? string.Empty,
                    RowCount = long.Parse(row[2]?.ToString() ?? "0")
                });
            }
            return tables;
        }

        private List<ColumnInfo> MapToColumnInfoList(QueryResult result)
        {
            var columns = new List<ColumnInfo>();
            foreach (var row in result.Rows)
            {
                columns.Add(new ColumnInfo
                {
                    ColumnName = row[0]?.ToString() ?? string.Empty,
                    DataType = row[1]?.ToString() ?? string.Empty,
                    IsNullable = row[2]?.ToString() == "YES",
                    DefaultValue = row[3]?.ToString(),
                    MaxLength = row[4] != null ? int.Parse(row[4]?.ToString() ?? "0") : null,
                    IsPrimaryKey = row[5]?.ToString() == "PRI"
                });
            }
            return columns;
        }

        private async Task ExportSQLServerToStreamAsync(DatabaseInstance instance, StreamWriter writer)
        {
            await using var connection = await CreateSQLServerConnectionAsync(instance);
            
            await writer.WriteLineAsync("-- ZenCloud SQL Export (SQL Server)");
            await writer.WriteLineAsync($"-- Database: [{instance.DatabaseName}]");
            await writer.WriteLineAsync($"-- Generated at: {DateTime.UtcNow:O}");
            await writer.WriteLineAsync("SET NOCOUNT ON;");
            await writer.WriteLineAsync("GO");
            await writer.WriteLineAsync();
            
            // Exportar estructura de tablas
            var tablesQuery = "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE' AND TABLE_SCHEMA = 'dbo'";
            await using var tablesCmd = new SqlCommand(tablesQuery, connection);
            await using var tablesReader = await tablesCmd.ExecuteReaderAsync();
            
            var tableNames = new List<string>();
            while (await tablesReader.ReadAsync())
            {
                tableNames.Add(tablesReader.GetString(0));
            }
            await tablesReader.CloseAsync();
            
            foreach (var tableName in tableNames)
            {
                // Obtener CREATE TABLE
                var createTableQuery = $@"
                    SELECT 
                        'CREATE TABLE [' + t.name + '] (' + STUFF((
                            SELECT ', [' + c.name + '] ' + 
                                   ty.name + 
                                   CASE WHEN ty.name IN ('varchar', 'char', 'nvarchar', 'nchar') THEN '(' + CAST(c.max_length AS VARCHAR) + ')' ELSE '' END +
                                   CASE WHEN c.is_nullable = 0 THEN ' NOT NULL' ELSE ' NULL' END
                            FROM sys.columns c
                            INNER JOIN sys.types ty ON c.user_type_id = ty.user_type_id
                            WHERE c.object_id = t.object_id
                            FOR XML PATH('')
                        ), 1, 2, '') + ');'
                    FROM sys.tables t
                    WHERE t.name = '{tableName}'";
                
                await using var createCmd = new SqlCommand(createTableQuery, connection);
                var createTableScript = await createCmd.ExecuteScalarAsync() as string;
                
                if (!string.IsNullOrEmpty(createTableScript))
                {
                    await writer.WriteLineAsync($"-- Table: [{tableName}]");
                    await writer.WriteLineAsync(createTableScript);
                    await writer.WriteLineAsync("GO");
                    await writer.WriteLineAsync();
                }
                
                // Exportar datos
                var dataQuery = $"SELECT * FROM [{tableName}]";
                await using var dataCmd = new SqlCommand(dataQuery, connection);
                await using var dataReader = await dataCmd.ExecuteReaderAsync();
                
                if (dataReader.HasRows)
                {
                    while (await dataReader.ReadAsync())
                    {
                        var values = new List<string>();
                        for (int i = 0; i < dataReader.FieldCount; i++)
                        {
                            var value = dataReader.IsDBNull(i) ? "NULL" : $"'{dataReader.GetValue(i).ToString()?.Replace("'", "''")}'";
                            values.Add(value);
                        }
                        
                        var columns = string.Join(", ", Enumerable.Range(0, dataReader.FieldCount).Select(i => $"[{dataReader.GetName(i)}]"));
                        await writer.WriteLineAsync($"INSERT INTO [{tableName}] ({columns}) VALUES ({string.Join(", ", values)});");
                    }
                    await writer.WriteLineAsync("GO");
                    await writer.WriteLineAsync();
                }
            }
            
            await writer.WriteLineAsync("-- Export completed");
            await writer.FlushAsync();
        }

        #endregion
    }
}
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using Npgsql;
using System.Data;
using System.Text.RegularExpressions;
using ZenCloud.Data.DbContext;
using ZenCloud.Data.Entities;
using ZenCloud.DTOs.DatabaseManagement;
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

       
        private readonly Regex _validIdentifierRegex = new Regex("^[a-zA-Z_][a-zA-Z0-9_]{0,63}$", RegexOptions.Compiled);
        
        private readonly Regex _sqlInjectionPattern = new Regex(@"(;|\-\-|#|/\*|\*/|union\s+select|insert\s+into|drop\s+table|delete\s+from|update\s+set|create\s+table|alter\s+table|grant\s+.*to|revoke\s+.*from|exec(\s+|ute)|xp_|sp_|load_file|outfile|dumpfile)", 
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        public DatabaseManagementService(
            IMySQLConnectionManager connectionManager,
            IQueryExecutor queryExecutor,
            IAuditService auditService,
            IEncryptionService encryptionService,
            PgDbContext context,
            ILogger<DatabaseManagementService> logger)
        {
            _connectionManager = connectionManager;
            _queryExecutor = queryExecutor;
            _auditService = auditService;
            _encryptionService = encryptionService;
            _context = context;
            _logger = logger;
        }

        public async Task<QueryResult> ExecuteQueryAsync(Guid instanceId, Guid userId, string query)
        {
            await ValidateUserAccessAsync(instanceId, userId);
            
            // ✅ VALIDACIÓN SEGURA
            ValidateCustomQuerySecurity(query);
            
            var instance = await GetDatabaseInstanceAsync(instanceId);
            using var connection = await _connectionManager.GetConnectionAsync(instance);

            var result = await _queryExecutor.ExecuteSafeQueryAsync(connection, query);
            
            await _auditService.LogDatabaseEventAsync(
                userId, 
                instanceId,
                AuditAction.DatabaseUpdated, 
                $"Query executed: {GetQueryType(query)}, Success: {result.Success}, Rows: {result.Rows.Count}"
            );

            return result;
        }

        public async Task<List<TableInfo>> GetTablesAsync(Guid instanceId, Guid userId)
{
    try
    {
        await ValidateUserAccessAsync(instanceId, userId);
        
        var instance = await GetDatabaseInstanceAsync(instanceId);
        
        // ✅ Validar nombre de base de datos
        if (!IsValidDatabaseIdentifier(instance.DatabaseName))
            throw new ArgumentException("Nombre de base de datos contiene caracteres inválidos");
        
        // Detectar el tipo de motor de base de datos
        var engineName = instance.Engine?.EngineName.ToString().ToLower() ?? "mysql";
        
        QueryResult result;
        
        // Manejar PostgreSQL de forma diferente
        if (engineName == "postgresql")
        {
            result = await GetPostgreSQLTablesAsync(instance);
        }
        else
        {
            // MySQL - usar conexión directa sin el método parametrizado problemático
            using var connection = await _connectionManager.GetConnectionAsync(instance);
            
            // Usar consulta directa con escape manual
            var escapedDbName = instance.DatabaseName.Replace("'", "''");
            var query = $@"
                SELECT 
                    TABLE_NAME as TableName,
                    TABLE_TYPE as TableType,
                    COALESCE(TABLE_ROWS, 0) as RowCount
                FROM INFORMATION_SCHEMA.TABLES 
                WHERE TABLE_SCHEMA = '{escapedDbName}'
                ORDER BY TABLE_NAME";
            
            result = await _queryExecutor.ExecuteSafeQueryAsync(connection, query);
        }
        
        if (!result.Success)
        {
            _logger.LogError("Error obteniendo tablas: {ErrorMessage}", result.ErrorMessage);
            throw new Exception($"Error al obtener las tablas: {result.ErrorMessage ?? "Error desconocido"}");
        }
        
        if (result.Rows.Count == 0)
        {
            _logger.LogInformation("No se encontraron tablas en la base de datos {DatabaseName}", instance.DatabaseName);
            return new List<TableInfo>();
        }
        
        return MapToTableInfoList(result);
    }
    catch (UnauthorizedAccessException)
    {
        throw;
    }
    catch (KeyNotFoundException)
    {
        throw;
    }
    catch (ArgumentException)
    {
        throw;
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error inesperado en GetTablesAsync para instanceId: {InstanceId}", instanceId);
        throw;
    }
}
        private async Task<QueryResult> GetPostgreSQLTablesAsync(DatabaseInstance instance)
        {
            var result = new QueryResult();
            
            try
            {
                var decryptedPassword = _encryptionService.Decrypt(instance.DatabasePasswordHash);
                var connectionString = $"Host={instance.ServerIpAddress};Port={instance.AssignedPort};Database={instance.DatabaseName};Username={instance.DatabaseUser};Password={decryptedPassword};Timeout=30;";
                
                await using var connection = new NpgsqlConnection(connectionString);
                await connection.OpenAsync();
                
                // Consulta para PostgreSQL usando pg_catalog
                var query = @"
                    SELECT 
                        tablename as TableName,
                        'BASE TABLE' as TableType,
                        COALESCE(n_live_tup, 0) as RowCount
                    FROM pg_catalog.pg_tables t
                    LEFT JOIN pg_stat_user_tables s ON s.relname = t.tablename
                    WHERE schemaname = 'public'
                    ORDER BY tablename";
                
                await using var command = new NpgsqlCommand(query, connection);
                await using var reader = await command.ExecuteReaderAsync();
                
                // Obtener metadatos de columnas
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    result.Columns.Add(reader.GetName(i));
                }
                
                // Obtener datos
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
                _logger.LogError(ex, "Error obteniendo tablas de PostgreSQL para instanceId: {InstanceId}", instance.InstanceId);
            }
            
            return result;
        }

        public async Task<TableSchema> GetTableSchemaAsync(Guid instanceId, Guid userId, string tableName)
        {
            await ValidateUserAccessAsync(instanceId, userId);
            
            // ✅ Validar nombre de tabla
            if (!IsValidTableName(tableName))
                throw new ArgumentException("Nombre de tabla contiene caracteres inválidos");
            
            var instance = await GetDatabaseInstanceAsync(instanceId);
            
            // ✅ Validar nombre de base de datos
            if (!IsValidDatabaseIdentifier(instance.DatabaseName))
                throw new ArgumentException("Nombre de base de datos contiene caracteres inválidos");
            
            using var connection = await _connectionManager.GetConnectionAsync(instance);

            // ✅ CONSULTA PARAMETRIZADA
            var columnsQuery = @"
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
            var columnsResult = await ExecuteParameterizedQueryAsync(connection, columnsQuery, parameters);

            return new TableSchema
            {
                TableName = tableName,
                Columns = MapToColumnInfoList(columnsResult)
            };
        }

        public async Task<QueryResult> GetTableDataAsync(Guid instanceId, Guid userId, string tableName, int limit = 100)
        {
            await ValidateUserAccessAsync(instanceId, userId);
            
            // ✅ Validar nombre de tabla
            if (!IsValidTableName(tableName))
                throw new ArgumentException("Nombre de tabla contiene caracteres inválidos");
            
            // ✅ Validar y limitar el límite
            if (limit <= 0 || limit > 1000)
                limit = 100;
            
            var instance = await GetDatabaseInstanceAsync(instanceId);
            using var connection = await _connectionManager.GetConnectionAsync(instance);

            // ✅ SOLUCIÓN SEGURA: Verificar que la tabla existe primero
            var tableExistsQuery = @"
                SELECT COUNT(*) 
                FROM INFORMATION_SCHEMA.TABLES 
                WHERE TABLE_SCHEMA = @databaseName 
                AND TABLE_NAME = @tableName";
                
            var tableExistsParams = new { databaseName = instance.DatabaseName, tableName };
            var tableExistsResult = await ExecuteParameterizedQueryAsync(connection, tableExistsQuery, tableExistsParams);
            
            if (tableExistsResult.Rows.Count == 0 || (long)(tableExistsResult.Rows[0][0] ?? 0) == 0)
                throw new ArgumentException($"La tabla '{tableName}' no existe");

            // ✅ Usar el método seguro del QueryExecutor
            var query = $"SELECT * FROM `{EscapeTableName(tableName)}` LIMIT {limit}";
            var result = await _queryExecutor.ExecuteSelectQueryAsync(connection, query, limit);
            
            await _auditService.LogDatabaseEventAsync(
                userId,
                instanceId,
                AuditAction.DatabaseUpdated,
                $"Table data accessed: {tableName}, Rows: {result.Rows.Count}"
            );

            return result;
        }

        public async Task<bool> TestConnectionAsync(Guid instanceId, Guid userId)
        {
            await ValidateUserAccessAsync(instanceId, userId);
            
            var instance = await GetDatabaseInstanceAsync(instanceId);
            var result = await _connectionManager.ValidateConnectionAsync(instance);
            
            await _auditService.LogDatabaseEventAsync(
                userId,
                instanceId,
                AuditAction.DatabaseStatusChanged,
                $"Connection tested, Success: {result}"
            );

            return result;
        }

        public async Task<DatabaseInfo> GetDatabaseInfoAsync(Guid instanceId, Guid userId)
        {
            await ValidateUserAccessAsync(instanceId, userId);
            
            var instance = await GetDatabaseInstanceAsync(instanceId);
            
            // ✅ Validar nombre de base de datos
            if (!IsValidDatabaseIdentifier(instance.DatabaseName))
                throw new ArgumentException("Nombre de base de datos contiene caracteres inválidos");
            
            using var connection = await _connectionManager.GetConnectionAsync(instance);

            // ✅ CONSULTA PARAMETRIZADA
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
            var result = await ExecuteParameterizedQueryAsync(connection, query, parameters);
            
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

        public async Task<List<DatabaseProcess>> GetProcessListAsync(Guid instanceId, Guid userId)
        {
            await ValidateUserAccessAsync(instanceId, userId);
            
            var instance = await GetDatabaseInstanceAsync(instanceId);
            using var connection = await _connectionManager.GetConnectionAsync(instance);

            // ✅ SHOW PROCESSLIST es una consulta interna de MySQL, no necesita parámetros
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
            
            await _auditService.LogDatabaseEventAsync(
                userId,
                instanceId,
                AuditAction.DatabaseStatusChanged,
                $"Process list accessed, Count: {processes.Count}"
            );

            return processes;
        }

        public async Task<bool> KillProcessAsync(Guid instanceId, Guid userId, int processId)
        {
            await ValidateUserAccessAsync(instanceId, userId);
            
            // ✅ Validar processId
            if (processId <= 0)
                throw new ArgumentException("ID de proceso inválido");
            
            var instance = await GetDatabaseInstanceAsync(instanceId);
            using var connection = await _connectionManager.GetConnectionAsync(instance);

            // ✅ Consulta parametrizada
            var query = "KILL @processId";
            
            using var command = new MySqlCommand(query, connection);
            command.Parameters.AddWithValue("@processId", processId);
            await command.ExecuteNonQueryAsync();
            
            await _auditService.LogDatabaseEventAsync(
                userId, 
                instanceId,
                AuditAction.SystemConfigChanged, 
                $"Process killed: {processId}"
            );

            return true;
        }

        #region Métodos de Seguridad

        /// <summary>
        /// ✅ Valida que un identificador de base de datos sea seguro
        /// </summary>
        private bool IsValidDatabaseIdentifier(string identifier)
        {
            if (string.IsNullOrWhiteSpace(identifier))
                return false;
            
            // Solo permitir letras, números, guiones bajos
            if (!_validIdentifierRegex.IsMatch(identifier))
                return false;
            
            // Prevenir palabras reservadas de MySQL
            var reservedWords = new[] { "mysql", "information_schema", "performance_schema", "sys", "test" };
            if (reservedWords.Contains(identifier.ToLower()))
                return false;
            
            return true;
        }

        /// <summary>
        /// ✅ Valida que un nombre de tabla sea seguro
        /// </summary>
        private bool IsValidTableName(string tableName)
        {
            if (string.IsNullOrWhiteSpace(tableName))
                return false;
            
            // Solo permitir letras, números, guiones bajos
            if (!_validIdentifierRegex.IsMatch(tableName))
                return false;
            
            return true;
        }

        /// <summary>
        /// ✅ Escapa un nombre de tabla de forma segura para usar en consultas
        /// </summary>
        private string EscapeTableName(string tableName)
        {
            if (!IsValidTableName(tableName))
                throw new ArgumentException("Nombre de tabla inválido");
            
            // Escapar comillas invertidas duplicándolas
            return tableName.Replace("`", "``");
        }

        /// <summary>
        /// ✅ Validación extra de seguridad para consultas personalizadas
        /// </summary>
        private void ValidateCustomQuerySecurity(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                throw new ArgumentException("La consulta no puede estar vacía");
            
            if (query.Length > 5000) // ✅ Límite más conservador
                throw new ArgumentException("La consulta es demasiado larga");
            
            // ✅ Detección MÁS ESTRICTA de SQL Injection
            if (_sqlInjectionPattern.IsMatch(query))
                throw new InvalidOperationException("La consulta contiene patrones potencialmente peligrosos");
            
            // ✅ Solo permitir consultas SIMPLES de lectura
            var allowedFirstWords = new[] { "SELECT", "SHOW", "DESCRIBE", "EXPLAIN" };
            var firstWord = query.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault()?
                .ToUpper();
            
            if (firstWord == null || !allowedFirstWords.Contains(firstWord))
                throw new InvalidOperationException($"Tipo de consulta no permitido: {firstWord}");

            // ✅ Validación adicional: no permitir múltiples consultas
            if (query.Contains(';') && query.TrimEnd().EndsWith(";"))
                throw new InvalidOperationException("Múltiples consultas no están permitidas");
        }

        /// <summary>
        /// ✅ Ejecuta una consulta parametrizada de forma segura
        /// </summary>
        private async Task<QueryResult> ExecuteParameterizedQueryAsync(MySqlConnection connection, string query, object parameters)
        {
            var result = new QueryResult();
            
            try
            {
                using var command = new MySqlCommand(query, connection);
                command.CommandType = CommandType.Text;
                
                // Agregar parámetros dinámicamente
                foreach (var prop in parameters.GetType().GetProperties())
                {
                    var paramName = "@" + prop.Name;
                    var paramValue = prop.GetValue(parameters) ?? DBNull.Value;
                    command.Parameters.AddWithValue(paramName, paramValue);
                }
                
                using var reader = await command.ExecuteReaderAsync();
                
                // Obtener metadatos de columnas
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    result.Columns.Add(reader.GetName(i));
                }
                
                // Obtener datos
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
                _logger.LogError(ex, "Error executing parameterized query: {Query}", query);
            }
            
            return result;
        }

        #endregion

        #region Métodos Auxiliares

        private async Task ValidateUserAccessAsync(Guid instanceId, Guid userId)
        {
            var instance = await _context.DatabaseInstances
                .FirstOrDefaultAsync(di => di.InstanceId == instanceId && di.UserId == userId);

            if (instance == null)
                throw new UnauthorizedAccessException("User does not have access to this database instance");
        }

        private async Task<DatabaseInstance> GetDatabaseInstanceAsync(Guid instanceId)
        {
            var instance = await _context.DatabaseInstances
                .Include(di => di.Engine)
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

        #endregion
    }
}

using MySqlConnector;
using ZenCloud.DTOs.DatabaseManagement;
using ZenCloud.Services.Interfaces;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace ZenCloud.Services.Implementations
{
    public class MySQLQueryExecutor : IQueryExecutor
    {
        private readonly ILogger<MySQLQueryExecutor> _logger;
        private readonly List<string> _dangerousPatterns = new()
        {
            @"DROP\s+DATABASE",
            @"DROP\s+TABLE",
            @"DELETE\s+FROM\s+\w+\s+WHERE\s+1=1",
            @"ALTER\s+TABLE\s+\w+\s+DROP",
            @"CREATE\s+USER",
            @"GRANT\s+ALL",
            @"REVOKE\s+ALL",
            @"FLUSH\s+PRIVILEGES"
        };

        public MySQLQueryExecutor(ILogger<MySQLQueryExecutor> logger)
        {
            _logger = logger;
        }

        public async Task<QueryResult> ExecuteSafeQueryAsync(MySqlConnection connection, string query)
        {
            var result = new QueryResult();
            var startTime = DateTime.UtcNow;

            try
            {
                ValidateQuerySecurity(query);

                using var command = new MySqlCommand(query, connection);
                
                if (IsSelectQuery(query))
                {
                    using var reader = await command.ExecuteReaderAsync();
                    result = await MapReaderToQueryResult(reader);
                }
                else
                {
                    result.AffectedRows = await command.ExecuteNonQueryAsync();
                    result.Success = true;
                }

                result.ExecutionTime = (int)(DateTime.UtcNow - startTime).TotalMilliseconds;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                _logger.LogWarning(ex, "Error executing query: {Query}", query);
            }

            return result;
        }

        public async Task<QueryResult> ExecuteSelectQueryAsync(MySqlConnection connection, string query, int limit = 1000)
        {
            if (!query.ToUpper().Contains("LIMIT") && limit > 0)
            {
                query += $" LIMIT {limit}";
            }

            return await ExecuteSafeQueryAsync(connection, query);
        }

        public async Task<int> ExecuteNonQueryAsync(MySqlConnection connection, string query)
        {
            ValidateQuerySecurity(query);

            using var command = new MySqlCommand(query, connection);
            return await command.ExecuteNonQueryAsync();
        }

        public async Task<object> ExecuteScalarAsync(MySqlConnection connection, string query)
        {
            ValidateQuerySecurity(query);

            using var command = new MySqlCommand(query, connection);
            return await command.ExecuteScalarAsync();
        }

        private void ValidateQuerySecurity(string query)
        {
            var upperQuery = query.ToUpper().Trim();

            // Validar patrones peligrosos
            foreach (var pattern in _dangerousPatterns)
            {
                if (Regex.IsMatch(upperQuery, pattern, RegexOptions.IgnoreCase))
                {
                    throw new UnauthorizedAccessException($"Consulta no permitida por seguridad: {pattern}");
                }
            }

            // Permitir CREATE TABLE específicamente
            if (upperQuery.StartsWith("CREATE TABLE"))
            {
                return; // Permitir CREATE TABLE
            }

            // Bloquear otros comandos DDL peligrosos
            if (upperQuery.StartsWith("DROP ") || 
                upperQuery.StartsWith("ALTER ") || 
                upperQuery.StartsWith("CREATE "))
            {
                throw new UnauthorizedAccessException("Modificaciones de estructura no permitidas");
            }
        }
        private bool IsSelectQuery(string query)
        {
            return query.Trim().ToUpper().StartsWith("SELECT");
        }

        private async Task<QueryResult> MapReaderToQueryResult(MySqlDataReader reader)
        {
            var result = new QueryResult
            {
                Success = true,
                Columns = new List<string>(),
                Rows = new List<object?[]>()
            };

            for (int i = 0; i < reader.FieldCount; i++)
            {
                result.Columns.Add(reader.GetName(i));
            }

            while (await reader.ReadAsync())
            {
                var row = new object?[reader.FieldCount];
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    row[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                }
                result.Rows.Add(row);
            }

            return result;
        }
    }
}
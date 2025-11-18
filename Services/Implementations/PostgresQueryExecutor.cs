using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Npgsql;
using ZenCloud.DTOs.DatabaseManagement;
using ZenCloud.Services.Interfaces;

namespace ZenCloud.Services.Implementations
{
    public class PostgresQueryExecutor : IPostgresQueryExecutor
    {
        private readonly ILogger<PostgresQueryExecutor> _logger;
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

        public PostgresQueryExecutor(ILogger<PostgresQueryExecutor> logger)
        {
            _logger = logger;
        }

        public async Task<QueryResult> ExecuteSafeQueryAsync(NpgsqlConnection connection, string query)
        {
            var result = new QueryResult();
            var startTime = DateTime.UtcNow;

            try
            {
                ValidateQuerySecurity(query);

                await using var command = new NpgsqlCommand(query, connection)
                {
                    CommandTimeout = 60
                };

                if (IsSelectQuery(query))
                {
                    await using var reader = await command.ExecuteReaderAsync();
                    result = await MapReaderToQueryResultAsync(reader);
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
                _logger.LogWarning(ex, "Error executing PostgreSQL query: {Query}", query);
            }

            return result;
        }

        public async Task<QueryResult> ExecuteSelectQueryAsync(NpgsqlConnection connection, string query, int limit = 1000)
        {
            if (!query.ToUpperInvariant().Contains("LIMIT") && limit > 0)
            {
                query += $" LIMIT {limit}";
            }

            return await ExecuteSafeQueryAsync(connection, query);
        }

        public async Task<int> ExecuteNonQueryAsync(NpgsqlConnection connection, string query)
        {
            ValidateQuerySecurity(query);

            await using var command = new NpgsqlCommand(query, connection)
            {
                CommandTimeout = 60
            };
            return await command.ExecuteNonQueryAsync();
        }

        public async Task<object?> ExecuteScalarAsync(NpgsqlConnection connection, string query)
        {
            ValidateQuerySecurity(query);

            await using var command = new NpgsqlCommand(query, connection)
            {
                CommandTimeout = 60
            };
            return await command.ExecuteScalarAsync();
        }

        private void ValidateQuerySecurity(string query)
        {
            var upperQuery = query.ToUpperInvariant().Trim();

            foreach (var pattern in _dangerousPatterns)
            {
                if (Regex.IsMatch(upperQuery, pattern, RegexOptions.IgnoreCase))
                {
                    throw new UnauthorizedAccessException($"Consulta no permitida por seguridad: {pattern}");
                }
            }

            if (upperQuery.StartsWith("CREATE TABLE"))
            {
                return;
            }

            if (upperQuery.StartsWith("DROP ") ||
                upperQuery.StartsWith("ALTER ") ||
                upperQuery.StartsWith("CREATE "))
            {
                throw new UnauthorizedAccessException("Modificaciones de estructura no permitidas");
            }
        }

        private static bool IsSelectQuery(string query) => query.Trim().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase);

        private static async Task<QueryResult> MapReaderToQueryResultAsync(NpgsqlDataReader reader)
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

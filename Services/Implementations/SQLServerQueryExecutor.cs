using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using ZenCloud.DTOs.DatabaseManagement;
using ZenCloud.Services.Interfaces;

namespace ZenCloud.Services.Implementations
{
    public class SQLServerQueryExecutor : ISQLServerQueryExecutor
    {
        private readonly ILogger<SQLServerQueryExecutor> _logger;
        private readonly List<string> _dangerousPatterns = new()
        {
            @"DROP\s+DATABASE",
            @"DROP\s+TABLE",
            @"DELETE\s+FROM\s+\w+\s+WHERE\s+1=1",
            @"ALTER\s+TABLE\s+\w+\s+DROP",
            @"CREATE\s+LOGIN",
            @"ALTER\s+LOGIN",
            @"DROP\s+LOGIN",
            @"GRANT\s+",
            @"REVOKE\s+",
            @"DENY\s+"
        };

        public SQLServerQueryExecutor(ILogger<SQLServerQueryExecutor> logger)
        {
            _logger = logger;
        }

        public async Task<QueryResult> ExecuteSafeQueryAsync(SqlConnection connection, string query)
        {
            var result = new QueryResult();
            var startTime = DateTime.UtcNow;

            try
            {
                ValidateQuerySecurity(query);

                await using var command = new SqlCommand(query, connection)
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
                _logger.LogWarning(ex, "Error executing SQL Server query: {Query}", query);
            }

            return result;
        }

        public async Task<QueryResult> ExecuteSelectQueryAsync(SqlConnection connection, string query, int limit = 1000)
        {
            if (!query.ToUpperInvariant().Contains("TOP") && limit > 0)
            {
                query = query.Trim().ToUpperInvariant().StartsWith("SELECT")
                    ? query.Insert(6, $" TOP {limit}")
                    : query;
            }

            return await ExecuteSafeQueryAsync(connection, query);
        }

        public async Task<int> ExecuteNonQueryAsync(SqlConnection connection, string query)
        {
            ValidateQuerySecurity(query);

            await using var command = new SqlCommand(query, connection)
            {
                CommandTimeout = 60
            };
            return await command.ExecuteNonQueryAsync();
        }

        public async Task<object?> ExecuteScalarAsync(SqlConnection connection, string query)
        {
            ValidateQuerySecurity(query);

            await using var command = new SqlCommand(query, connection)
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

        private static async Task<QueryResult> MapReaderToQueryResultAsync(SqlDataReader reader)
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

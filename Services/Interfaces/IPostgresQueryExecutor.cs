using Npgsql;
using ZenCloud.DTOs.DatabaseManagement;

namespace ZenCloud.Services.Interfaces
{
    public interface IPostgresQueryExecutor
    {
        Task<QueryResult> ExecuteSafeQueryAsync(NpgsqlConnection connection, string query);
        Task<QueryResult> ExecuteSelectQueryAsync(NpgsqlConnection connection, string query, int limit = 1000);
        Task<int> ExecuteNonQueryAsync(NpgsqlConnection connection, string query);
        Task<object?> ExecuteScalarAsync(NpgsqlConnection connection, string query);
    }
}

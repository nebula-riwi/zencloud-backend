using MySqlConnector;
using ZenCloud.DTOs.DatabaseManagement;

namespace ZenCloud.Services.Interfaces
{
    public interface IQueryExecutor
    {
        Task<QueryResult> ExecuteSafeQueryAsync(MySqlConnection connection, string query);
        Task<QueryResult> ExecuteSelectQueryAsync(MySqlConnection connection, string query, int limit = 1000);
        Task<int> ExecuteNonQueryAsync(MySqlConnection connection, string query);
        Task<object> ExecuteScalarAsync(MySqlConnection connection, string query);
    }
}
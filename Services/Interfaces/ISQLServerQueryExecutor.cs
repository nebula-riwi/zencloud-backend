using Microsoft.Data.SqlClient;
using ZenCloud.DTOs.DatabaseManagement;

namespace ZenCloud.Services.Interfaces
{
    public interface ISQLServerQueryExecutor
    {
        Task<QueryResult> ExecuteSafeQueryAsync(SqlConnection connection, string query);
        Task<QueryResult> ExecuteSelectQueryAsync(SqlConnection connection, string query, int limit = 1000);
        Task<int> ExecuteNonQueryAsync(SqlConnection connection, string query);
        Task<object?> ExecuteScalarAsync(SqlConnection connection, string query);
    }
}

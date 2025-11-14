using ZenCloud.Data.Entities;
using ZenCloud.DTOs.DatabaseManagement;

namespace ZenCloud.Services.Interfaces
{
    public interface IDatabaseManagementService
    {
        Task<QueryResult> ExecuteQueryAsync(Guid instanceId, Guid userId, string query);
        Task<List<TableInfo>> GetTablesAsync(Guid instanceId, Guid userId);
        Task<TableSchema> GetTableSchemaAsync(Guid instanceId, Guid userId, string tableName);
        Task<QueryResult> GetTableDataAsync(Guid instanceId, Guid userId, string tableName, int limit = 100);
        Task<bool> TestConnectionAsync(Guid instanceId, Guid userId);
        Task<DatabaseInfo> GetDatabaseInfoAsync(Guid instanceId, Guid userId);
        Task<List<DatabaseProcess>> GetProcessListAsync(Guid instanceId, Guid userId);
        Task<bool> KillProcessAsync(Guid instanceId, Guid userId, int processId);
        Task<IReadOnlyList<QueryHistoryItemDto>> GetQueryHistoryAsync(Guid instanceId, Guid userId, int limit = 20);
        Task<DatabaseExportResult> ExportDatabaseAsync(Guid instanceId, Guid userId);
    }
}
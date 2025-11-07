using MySqlConnector;
using ZenCloud.Data.Entities;

namespace ZenCloud.Services.Interfaces
{
    public interface IMySQLConnectionManager
    {
        Task<MySqlConnection> GetConnectionAsync(DatabaseInstance instance);
        Task<bool> ValidateConnectionAsync(DatabaseInstance instance);
        Task CloseConnectionAsync(MySqlConnection connection);
    }
}
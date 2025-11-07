using MySqlConnector;
using ZenCloud.Data.Entities;
using ZenCloud.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace ZenCloud.Services.Implementations
{
    public class MySQLConnectionManager : IMySQLConnectionManager
    {
        private readonly IEncryptionService _encryptionService;
        private readonly ILogger<MySQLConnectionManager> _logger;

        public MySQLConnectionManager(IEncryptionService encryptionService, ILogger<MySQLConnectionManager> logger)
        {
            _encryptionService = encryptionService;
            _logger = logger;
        }

        public async Task<MySqlConnection> GetConnectionAsync(DatabaseInstance instance)
        {
            try
            {
                var decryptedPassword = _encryptionService.Decrypt(instance.DatabasePasswordHash);
                var connectionString = $"Server={instance.ServerIpAddress};Port={instance.AssignedPort};Database={instance.DatabaseName};Uid={instance.DatabaseUser};Pwd={decryptedPassword};SslMode=Preferred;";

                var connection = new MySqlConnection(connectionString);
                await connection.OpenAsync();
                
                _logger.LogInformation("MySQL connection established for instance: {InstanceId}", instance.InstanceId);
                return connection;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error establishing MySQL connection for instance: {InstanceId}", instance.InstanceId);
                throw;
            }
        }

        public async Task<bool> ValidateConnectionAsync(DatabaseInstance instance)
        {
            try
            {
                using var connection = await GetConnectionAsync(instance);
                using var command = new MySqlCommand("SELECT 1", connection);
                var result = await command.ExecuteScalarAsync();
                return result?.ToString() == "1";
            }
            catch
            {
                return false;
            }
        }

        public async Task CloseConnectionAsync(MySqlConnection connection)
        {
            try
            {
                if (connection?.State == System.Data.ConnectionState.Open)
                {
                    await connection.CloseAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error closing MySQL connection");
            }
        }
    }
}
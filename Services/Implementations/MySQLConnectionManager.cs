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
                var connectionString = $"Server={instance.ServerIpAddress};Port={instance.AssignedPort};Database={instance.DatabaseName};Uid={instance.DatabaseUser};Pwd={decryptedPassword};SslMode=Preferred;Connection Timeout=30;Default Command Timeout=60;";

                var connection = new MySqlConnection(connectionString);
                
                // Configurar timeout de conexión
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                await connection.OpenAsync(cts.Token);
                
                _logger.LogInformation("MySQL connection established for instance: {InstanceId}", instance.InstanceId);
                return connection;
            }
            catch (MySqlException ex)
            {
                var errorMessage = ex.ErrorCode switch
                {
                    MySqlErrorCode.UnableToConnectToHost => $"No se puede conectar al servidor MySQL en {instance.ServerIpAddress}:{instance.AssignedPort}. Verifica que el servidor esté corriendo y el puerto sea correcto.",
                    MySqlErrorCode.AccessDenied => "Credenciales incorrectas. Verifica el usuario y contraseña de la base de datos.",
                    MySqlErrorCode.UnknownDatabase => $"La base de datos '{instance.DatabaseName}' no existe en el servidor.",
                    _ => $"Error de MySQL: {ex.Message}"
                };
                
                _logger.LogError(ex, "Error establishing MySQL connection for instance: {InstanceId} - {ErrorMessage}", instance.InstanceId, errorMessage);
                throw new InvalidOperationException(errorMessage, ex);
            }
            catch (TimeoutException ex)
            {
                var errorMessage = $"Timeout al conectar al servidor MySQL ({instance.ServerIpAddress}:{instance.AssignedPort}). El servidor no respondió en 30 segundos.";
                _logger.LogError(ex, "Timeout establishing MySQL connection for instance: {InstanceId}", instance.InstanceId);
                throw new InvalidOperationException(errorMessage, ex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error establishing MySQL connection for instance: {InstanceId}", instance.InstanceId);
                throw new InvalidOperationException($"Error inesperado al conectar a MySQL: {ex.Message}", ex);
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
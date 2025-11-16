using ZenCloud.Data.Entities;
using ZenCloud.Data.Repositories.Interfaces;
using ZenCloud.Services.Interfaces;
using System.Security.Cryptography;
using ZenCloud.Exceptions;
using System.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore.Storage;
using ZenCloud.Data.DbContext;

namespace ZenCloud.Services.Implementations;

public class DatabaseInstanceService : IDatabaseInstanceService
{
    private readonly IDatabaseInstanceRepository _databaseRepository;
    private readonly IRepository<User> _userRepository;
    private readonly IRepository<DatabaseEngine> _engineRepository;
    private readonly ICredentialsGeneratorService _credentialsGenerator;
    private readonly IPlanValidationService _planValidationService;
    private readonly IDatabaseEngineService _databaseEngineService;
    private readonly IEmailService _emailService;
    private readonly IEncryptionService _encryptionService;
    private readonly ILogger<DatabaseInstanceService> _logger;
    private readonly PgDbContext _dbContext;

    public DatabaseInstanceService(
        IDatabaseInstanceRepository databaseRepository,
        IRepository<User> userRepository,
        IRepository<DatabaseEngine> engineRepository,
        ICredentialsGeneratorService credentialsGenerator,
        IDatabaseEngineService databaseEngineService,
        IPlanValidationService planValidationService,
        IEmailService emailService,
        IEncryptionService encryptionService,
        ILogger<DatabaseInstanceService> logger,
        PgDbContext dbContext)
    {
        _databaseRepository = databaseRepository;
        _userRepository = userRepository;
        _engineRepository = engineRepository;
        _credentialsGenerator = credentialsGenerator;
        _planValidationService = planValidationService;
        _databaseEngineService = databaseEngineService;
        _emailService = emailService;
        _encryptionService = encryptionService;
        _logger = logger;
        _dbContext = dbContext;
    }

    public async Task<IEnumerable<DatabaseInstance>> GetUserDatabasesAsync(Guid userId)
    {
        return await _databaseRepository.GetByUserIdAsync(userId);
    }

    public async Task<IEnumerable<DatabaseEngine>> GetActiveEnginesAsync()
    {
        var engines = await _engineRepository.GetAllAsync();
        return engines.Where(e => e.IsActive).OrderBy(e => e.EngineName);
    }

    public async Task<DatabaseInstance?> GetDatabaseByIdAsync(Guid instanceId)
    {
        return await _databaseRepository.GetByIdAsync(instanceId);
    }

    public async Task<DatabaseInstance> CreateDatabaseInstanceAsync(Guid userId, Guid engineId, string? databaseName = null)
    {
        var user = await _userRepository.GetByIdAsync(userId);
        if (user == null)
            throw new NotFoundException("Usuario no encontrado");

        var engine = await _engineRepository.GetByIdAsync(engineId);
        if (engine == null || !engine.IsActive)
            throw new BadRequestException("Motor de base de datos no válido o inactivo");

        // Usar transacción para prevenir condiciones de carrera (Serializable isolation level)
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, CancellationToken.None);
        try
        {
            // Validar límites DENTRO de la transacción para garantizar atomicidad
            var (canCreate, errorMessage, currentCount, maxCount) = await _planValidationService.CanCreateDatabaseWithDetailsAsync(userId, engineId);
            if (!canCreate)
            {
                _logger.LogWarning("Límite de bases de datos alcanzado para usuario {UserId}, motor {EngineId}. Actual: {CurrentCount}, Máximo: {MaxCount}", 
                    userId, engineId, currentCount, maxCount);
                await transaction.RollbackAsync();
                throw new ConflictException(errorMessage ?? "Has alcanzado el límite de bases de datos para tu plan actual");
            }

            string finalDatabaseName;

            if (string.IsNullOrWhiteSpace(databaseName))
            {
                // Generar nombre automático completo
                finalDatabaseName = _credentialsGenerator.GenerateDatabaseName(engine.EngineName.ToString(), user.UserId);
            }
            else
            {
                // Validar y normalizar nombre ingresado por usuario
                databaseName = databaseName.ToLower().Trim();
                if (!System.Text.RegularExpressions.Regex.IsMatch(databaseName, @"^[a-z0-9_\-\+]+$"))
                {
                    await transaction.RollbackAsync();
                    throw new BadRequestException("El nombre de la base de datos solo puede contener letras minúsculas, números, guiones y guiones bajos");
                }
                
                // Agregar sufijo automático para diferenciar / estandarizar
                string suffix = GenerateRandomSuffix(6); // función que genera 6 caracteres aleatorios seguro
                finalDatabaseName = $"{databaseName}_{suffix}";
            }

            var username = _credentialsGenerator.GenerateUsername(finalDatabaseName);
            var password = _credentialsGenerator.GeneratePassword();
            var passwordEncrypted = _encryptionService.Encrypt(password);

            // Crear la BD física (fuera de la transacción para evitar bloqueos largos)
            // Pero validar límites de nuevo justo antes de crear
            var (canStillCreate, _, currentCountAfter, _) = await _planValidationService.CanCreateDatabaseWithDetailsAsync(userId, engineId);
            if (!canStillCreate)
            {
                _logger.LogWarning("Límite alcanzado durante creación - condición de carrera detectada para usuario {UserId}, motor {EngineId}. Actual: {CurrentCount}", 
                    userId, engineId, currentCountAfter);
                await transaction.RollbackAsync();
                throw new ConflictException("El límite de bases de datos fue alcanzado por otro proceso simultáneo. Por favor, intenta de nuevo.");
            }

            await _databaseEngineService.CreatePhysicalDatabaseAsync(
                engine.EngineName.ToString(),
                finalDatabaseName,
                username,
                password
            );

            var databaseInstance = new DatabaseInstance
            {
                InstanceId = Guid.NewGuid(),
                UserId = userId,
                EngineId = engine.EngineId,
                DatabaseName = finalDatabaseName,
                DatabaseUser = username,
                DatabasePasswordHash = passwordEncrypted,
                AssignedPort = engine.DefaultPort,
                ConnectionString = BuildConnectionString(engine, finalDatabaseName, username, password),
                Status = DatabaseInstanceStatus.Active,
                ServerIpAddress = "168.119.182.243",
                CreatedAt = DateTime.UtcNow
            };

            await _databaseRepository.CreateAsync(databaseInstance);

            // Confirmar transacción ANTES de enviar el email (para no bloquear)
            await transaction.CommitAsync();

            // Enviar email después de confirmar la transacción
            await _emailService.SendDatabaseCredentialsEmailAsync(
                user.Email,
                user.FullName,
                engine.EngineName.ToString(),
                finalDatabaseName,
                username,
                password,
                databaseInstance.ServerIpAddress,
                databaseInstance.AssignedPort
            );

            _logger.LogInformation("Base de datos creada exitosamente: {DatabaseName} para usuario {UserId}", finalDatabaseName, userId);
            return databaseInstance;
        }
        catch (ConflictException)
        {
            // Re-lanzar ConflictException sin hacer rollback (ya se hizo arriba)
            throw;
        }
        catch (Exception ex)
        {
            // Hacer rollback en caso de cualquier otro error
            try
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Error creando base de datos, transacción revertida para usuario {UserId}", userId);
            }
            catch (Exception rollbackEx)
            {
                _logger.LogError(rollbackEx, "Error al hacer rollback de transacción para usuario {UserId}", userId);
            }
            throw;
        }
    }

// Método auxiliar para generar sufijo aleatorio
private static string GenerateRandomSuffix(int length)
{
    const string chars = "abcdefghijklmnopqrstuvwxyz0123456789";
    var data = new byte[length];
    using var rng = RandomNumberGenerator.Create();
    rng.GetBytes(data);

    var resultChars = new char[length];
    for (int i = 0; i < length; i++)
    {
        resultChars[i] = chars[data[i] % chars.Length];
    }

    return new string(resultChars);
}

    public string BuildConnectionString(DatabaseEngine engine, string databaseName, string userName, string password)
    {
        return engine.EngineName switch
        {
            DatabaseEngineType.MySQL =>
                $"Server=168.119.182.243;Port={engine.DefaultPort};Database={databaseName};User={userName};Password={password};",
            DatabaseEngineType.PostgreSQL =>
                $"Host=168.119.182.243;Port={engine.DefaultPort};Database={databaseName};Username={userName};Password={password};",
            DatabaseEngineType.MongoDB =>
                $"mongodb://{userName}:{password}@168.119.182.243:{engine.DefaultPort}/{databaseName}",
            DatabaseEngineType.SQLServer =>
                $"Server=168.119.182.243,{engine.DefaultPort};Database={databaseName};User Id={userName};Password={password};",
            DatabaseEngineType.Redis => $"168.119.182.243:{engine.DefaultPort},password={password}",
            DatabaseEngineType.Cassandra =>
                $"Contact Points=168.119.182.243;Port={engine.DefaultPort};Username={userName};Password={password};",
            _ => throw new BadRequestException("Motor de base de datos no soportado")
        };
    }
    
    public async Task DeleteDatabaseInstanceAsync(Guid instanceId, Guid userId)
    {
        var instance = await _databaseRepository.GetByIdWithEngineAsync(instanceId);
        
        if (instance == null)
        {
            throw new NotFoundException("Base de datos no encontrada");
        }

        if (instance.UserId != userId)
        {
            throw new ForbiddenException("No tienes permisos para eliminar esta base de datos");
        }

        if (instance.Status == DatabaseInstanceStatus.Deleted)
        {
            throw new BadRequestException("La base de datos ya está eliminada");
        }

        // Solo se puede eliminar físicamente si está desactivada
        if (instance.Status == DatabaseInstanceStatus.Active)
        {
            throw new BadRequestException("Debes desactivar la base de datos antes de eliminarla");
        }
        
        var user = await _userRepository.GetByIdAsync(userId);
        if (user == null || !user.IsActive)
            throw new NotFoundException("Usuario no válido o inactivo");
        
        var engineName = instance.Engine?.EngineName.ToString() ?? throw new NotFoundException("Motor no encontrado");
        var databaseName = instance.DatabaseName;
        var deletionDate = DateTime.UtcNow; // Usaremos esta fecha para el correo
        
        await _databaseEngineService.DeletePhysicalDatabaseAsync(
            instance.Engine?.EngineName.ToString() ?? throw new NotFoundException("Motor no encontrado"),
            instance.DatabaseName,
            instance.DatabaseUser
        );
        
        instance.Status = DatabaseInstanceStatus.Deleted;
        instance.DeletedAt = DateTime.UtcNow;
        
        // 2. Llamada al servicio de correo electrónico después de la eliminación exitosa
        // (Asumiendo que el método SendDatabaseDeletionEmailAsync está en _emailService)
        await _emailService.SendDatabaseDeletionEmailAsync(
            toEmail: user.Email, // Asume que el objeto user tiene una propiedad Email
            userName: user.FullName, // Asume que el objeto user tiene el nombre
            databaseName: databaseName,
            engineName: engineName,
            deletionDate: deletionDate
        );
        
        await _databaseRepository.UpdateAsync(instance);
    }

    public async Task<(DatabaseInstance database, string newPassword)> RotateCredentialsAsync(Guid instanceId, Guid userId)
    {
        var instance = await _databaseRepository.GetByIdWithEngineAsync(instanceId);
        
        if (instance == null)
        {
            throw new NotFoundException("Base de datos no encontrada");
        }

        if (instance.UserId != userId)
        {
            throw new ForbiddenException("No tienes permisos para rotar las credenciales de esta base de datos");
        }

        if (instance.Status != DatabaseInstanceStatus.Active)
        {
            throw new BadRequestException("La base de datos se encuentra inactiva");
        }
        
        var user = await _userRepository.GetByIdAsync(userId);
        if (user == null || !user.IsActive)
            throw new NotFoundException("Usuario no válido o inactivo");
        
        var engineName = instance.Engine?.EngineName.ToString() ?? throw new NotFoundException("Motor no encontrado");
        var databaseName = instance.DatabaseName;
        var oldUsername = instance.DatabaseUser;

        // Generar nuevas credenciales
        var newUsername = _credentialsGenerator.GenerateUsername(databaseName);
        var newPassword = _credentialsGenerator.GeneratePassword();
        var newPasswordEncrypted = _encryptionService.Encrypt(newPassword);

        // Rotar credenciales en la base de datos física
        await _databaseEngineService.RotateCredentialsAsync(
            engineName,
            databaseName,
            oldUsername,
            newUsername,
            newPassword
        );

        // Actualizar en la base de datos de ZenCloud
        instance.DatabaseUser = newUsername;
        instance.DatabasePasswordHash = newPasswordEncrypted;
        instance.ConnectionString = BuildConnectionString(instance.Engine!, databaseName, newUsername, newPassword);
        instance.UpdatedAt = DateTime.UtcNow;

        await _databaseRepository.UpdateAsync(instance);

        // Enviar email con las nuevas credenciales
        await _emailService.SendDatabaseCredentialsEmailAsync(
            user.Email,
            user.FullName,
            engineName,
            databaseName,
            newUsername,
            newPassword,
            instance.ServerIpAddress,
            instance.AssignedPort
        );

        return (instance, newPassword);
    }

    public async Task<DatabaseInstance> ActivateDatabaseAsync(Guid instanceId, Guid userId)
    {
        var instance = await _databaseRepository.GetByIdWithEngineAsync(instanceId);
        
        if (instance == null)
        {
            throw new NotFoundException("Base de datos no encontrada");
        }

        if (instance.UserId != userId)
        {
            throw new ForbiddenException("No tienes permisos para activar esta base de datos");
        }

        if (instance.Status == DatabaseInstanceStatus.Active)
        {
            throw new BadRequestException("La base de datos ya está activa");
        }

        if (instance.Status == DatabaseInstanceStatus.Deleted)
        {
            throw new BadRequestException("No se puede reactivar una base de datos eliminada");
        }

        // Validar límite de bases activas según el plan
        var engine = instance.Engine;
        if (engine == null)
        {
            throw new NotFoundException("Motor de base de datos no encontrado");
        }

        var canActivate = await _planValidationService.CanCreateDatabaseAsync(userId, engine.EngineId);
        if (!canActivate)
        {
            throw new ConflictException("Has alcanzado el límite de bases de datos activas para tu plan actual. Desactiva otra base de datos primero.");
        }

        instance.Status = DatabaseInstanceStatus.Active;
        instance.UpdatedAt = DateTime.UtcNow;
        await _databaseRepository.UpdateAsync(instance);

        return instance;
    }

    public async Task<DatabaseInstance> DeactivateDatabaseAsync(Guid instanceId, Guid userId)
    {
        var instance = await _databaseRepository.GetByIdWithEngineAsync(instanceId);
        
        if (instance == null)
        {
            throw new NotFoundException("Base de datos no encontrada");
        }

        if (instance.UserId != userId)
        {
            throw new ForbiddenException("No tienes permisos para desactivar esta base de datos");
        }

        if (instance.Status == DatabaseInstanceStatus.Inactive)
        {
            throw new BadRequestException("La base de datos ya está desactivada");
        }

        if (instance.Status == DatabaseInstanceStatus.Deleted)
        {
            throw new BadRequestException("La base de datos ya está eliminada");
        }

        instance.Status = DatabaseInstanceStatus.Inactive;
        instance.UpdatedAt = DateTime.UtcNow;
        await _databaseRepository.UpdateAsync(instance);

        return instance;
    }
}
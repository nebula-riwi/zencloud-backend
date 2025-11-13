using ZenCloud.Data.Entities;
using ZenCloud.Data.Repositories.Interfaces;
using ZenCloud.Services.Interfaces;
using ZenCloud.Exceptions;

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

    public DatabaseInstanceService(
        IDatabaseInstanceRepository databaseRepository,
        IRepository<User> userRepository,
        IRepository<DatabaseEngine> engineRepository,
        ICredentialsGeneratorService credentialsGenerator,
        IDatabaseEngineService databaseEngineService,
        IPlanValidationService planValidationService,
        IEmailService emailService)
    {
        _databaseRepository = databaseRepository;
        _userRepository = userRepository;
        _engineRepository = engineRepository;
        _credentialsGenerator = credentialsGenerator;
        _planValidationService = planValidationService;
        _databaseEngineService = databaseEngineService;
        _emailService = emailService;
    }

    public async Task<IEnumerable<DatabaseInstance>> GetUserDatabasesAsync(Guid userId)
    {
        return await _databaseRepository.GetByUserIdAsync(userId);
    }

    public async Task<DatabaseInstance?> GetDatabaseByIdAsync(Guid instanceId)
    {
        return await _databaseRepository.GetByIdAsync(instanceId);
    }

    public async Task<DatabaseInstance> CreateDatabaseInstanceAsync(Guid userId, Guid engineId, string? databaseName = null)
    {
        var user = await _userRepository.GetByIdAsync(userId);
        if (user == null)
        {
            throw new NotFoundException("Usuario no encontrado");
        }

        var engine = await _engineRepository.GetByIdAsync(engineId);
        if (engine == null || !engine.IsActive)
        {
            throw new BadRequestException("Motor de base de datos no válido o inactivo");
        }
        
        var canCreate = await _planValidationService.CanCreateDatabaseAsync(userId, engineId);
        if (!canCreate)
        {
            throw new ConflictException("Has alcanzado el límite de bases de datos para tu plan actual");
        }
        
        // Si no se proporciona un nombre, generar uno automáticamente
        if (string.IsNullOrWhiteSpace(databaseName))
        {
            databaseName = _credentialsGenerator.GenerateDatabaseName(
            engine.EngineName.ToString(),
            user.UserId);
        }
        else
        {
            // Validar y normalizar el nombre proporcionado
            databaseName = databaseName.ToLower().Trim();
            if (!System.Text.RegularExpressions.Regex.IsMatch(databaseName, @"^[a-z0-9_-]+$"))
            {
                throw new BadRequestException("El nombre de la base de datos solo puede contener letras minúsculas, números, guiones y guiones bajos");
            }
        }
        
        var username = _credentialsGenerator.GenerateUsername(databaseName);

        var password = _credentialsGenerator.GeneratePassword();

        var passwordHash = _credentialsGenerator.HashPassword(password);
        
        await _databaseEngineService.CreatePhysicalDatabaseAsync(
            engine.EngineName.ToString(),
            databaseName,
            username,
            password
        );

        var databaseInstance = new DatabaseInstance
        {
            InstanceId = Guid.NewGuid(),
            UserId = userId,
            EngineId = engine.EngineId,
            DatabaseName = databaseName,
            DatabaseUser = username,
            DatabasePasswordHash = passwordHash,
            AssignedPort = engine.DefaultPort,
            ConnectionString = BuildConnectionString(engine, databaseName, username, password),
            Status = DatabaseInstanceStatus.Active,
            ServerIpAddress = "localhost", // TODO: Configurar IP del servidor
            CreatedAt = DateTime.UtcNow

        };
        
        await _databaseRepository.CreateAsync(databaseInstance);
        
        // Enviar correo con credenciales
        await _emailService.SendDatabaseCredentialsEmailAsync(
            user.Email,
            user.FullName,
            engine.EngineName.ToString(),
            databaseName,
            username,
            password,
            databaseInstance.ServerIpAddress,
            databaseInstance.AssignedPort
        );
        
        return databaseInstance;
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

        if (instance.Status != DatabaseInstanceStatus.Active)
        {
            throw new BadRequestException("La base de datos se encuentra inactiva");
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
}
using ZenCloud.Data.Entities;
using ZenCloud.Data.Repositories.Interfaces;
using ZenCloud.Services.Interfaces;

namespace ZenCloud.Services.Implementations;

public class DatabaseInstanceService : IDatabaseInstanceService
{
    private readonly IDatabaseInstanceRepository _databaseRepository;
    private readonly IRepository<User> _userRepository;
    private readonly IRepository<DatabaseEngine> _engineRepository;
    private readonly ICredentialsGeneratorService _credentialsGenerator;
    private readonly IPlanValidationService _planValidationService;

    public DatabaseInstanceService(
        IDatabaseInstanceRepository databaseRepository,
        IRepository<User> userRepository,
        IRepository<DatabaseEngine> engineRepository,
        ICredentialsGeneratorService credentialsGenerator,
        IPlanValidationService planValidationService)
    {
        _databaseRepository = databaseRepository;
        _userRepository = userRepository;
        _engineRepository = engineRepository;
        _credentialsGenerator = credentialsGenerator;
        _planValidationService = planValidationService;
    }

    public async Task<IEnumerable<DatabaseInstance>> GetUserDatabasesAsync(Guid userId)
    {
        return await _databaseRepository.GetByUserIdAsync(userId);
    }

    public async Task<DatabaseInstance?> GetDatabaseByIdAsync(Guid instanceId)
    {
        return await _databaseRepository.GetByIdAsync(instanceId);
    }

    public async Task<DatabaseInstance> CreateDatabaseInstanceAsync(Guid userId, Guid engineId)
    {
        var user = await _userRepository.GetByIdAsync(userId);
        if (user == null)
        {
            throw new Exception("Usuario no encontrado");
        }

        var engine = await _engineRepository.GetByIdAsync(engineId);
        if (engine == null || !engine.IsActive)
        {
            throw new Exception("Motor de base de datos no válido");
        }
        
        var canCreate = await _planValidationService.CanCreateDatabaseAsync(userId, engineId);
        if (!canCreate)
        {
            throw new Exception("Has alcanzado el límite de bases de datos para tu plan");
        }
        
        var databaseName = _credentialsGenerator.GenerateDatabaseName(
            engine.EngineName.ToString(),
            user.UserId);
        
        var username = _credentialsGenerator.GenerateUsername(databaseName);

        var password = _credentialsGenerator.GeneratePassword();

        var passwordHash = _credentialsGenerator.HashPassword(password);

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
        return databaseInstance;
    }

    public string BuildConnectionString(DatabaseEngine engine, string databaseName, string userName, string password)
    {
        return engine.EngineName switch
        {
            DatabaseEngineType.MySQL =>
                $"Server=localhost;Port={engine.DefaultPort};Database={databaseName};User={userName};Password={password};",
            DatabaseEngineType.PostgreSQL =>
                $"Host=localhost;Port={engine.DefaultPort};Database={databaseName};Username={userName};Password={password};",
            DatabaseEngineType.MongoDB =>
                $"mongodb://{userName}:{password}@localhost:{engine.DefaultPort}/{databaseName}",
            DatabaseEngineType.SQLServer =>
                $"Server=localhost,{engine.DefaultPort};Database={databaseName};User Id={userName};Password={password};",
            DatabaseEngineType.Redis => $"localhost:{engine.DefaultPort},password={password}",
            DatabaseEngineType.Cassandra =>
                $"Contact Points=localhost;Port={engine.DefaultPort};Username={userName};Password={password};",
            _ => throw new Exception("Motor de base de datos no soportado")
        };
    }
    
    public async Task DeleteDatabaseInstanceAsync(Guid instanceId, Guid userId)
    {
        var instance = await _databaseRepository.GetByIdAsync(instanceId);
        if (instance == null)
        {
            throw new Exception("Base de datos no encontrada");
        }

        if (instance.UserId != userId)
        {
            throw new Exception("No tienes permisos para eliminar esta base de datos");
        }
        instance.Status = DatabaseInstanceStatus.Deleted;
        instance.DeletedAt = DateTime.UtcNow;
        
        await _databaseRepository.UpdateAsync(instance);
    }
}
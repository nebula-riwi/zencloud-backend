using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ZenCloud.Data.DbContext;
using ZenCloud.Data.Entities;

namespace ZenCloud.Data.Seed;

public static class DataSeeder
{
    private const int MaxRetries = 5;
    private const int RetryDelaySeconds = 5;

    public static async Task SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        var context = scope.ServiceProvider.GetRequiredService<PgDbContext>();

        // Intentar conectar a la base de datos con retry logic
        for (int attempt = 1; attempt <= MaxRetries; attempt++)
        {
            try
            {
                // Verificar conectividad
                if (!await context.Database.CanConnectAsync())
                {
                    logger.LogWarning("Intento {Attempt}/{MaxRetries}: No se pudo conectar a la base de datos. Reintentando en {Delay}s...", 
                        attempt, MaxRetries, RetryDelaySeconds);
                    
                    if (attempt < MaxRetries)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(RetryDelaySeconds));
                        continue;
                    }
                    else
                    {
                        logger.LogError("No se pudo conectar a la base de datos después de {MaxRetries} intentos. El seeding se omitirá.", MaxRetries);
                        return;
                    }
                }

                logger.LogInformation("Conexión a la base de datos establecida. Iniciando seeding...");
                
                await SeedPlansAsync(context, logger);
                await SeedDatabaseEnginesAsync(context, logger);
                
                logger.LogInformation("Seeding completado exitosamente.");
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Error en intento {Attempt}/{MaxRetries} durante el seeding. Reintentando en {Delay}s...", 
                    attempt, MaxRetries, RetryDelaySeconds);
                
                if (attempt < MaxRetries)
                {
                    await Task.Delay(TimeSpan.FromSeconds(RetryDelaySeconds));
                }
                else
                {
                    logger.LogError(ex, "Error crítico durante el seeding después de {MaxRetries} intentos. La aplicación continuará sin seeding.", MaxRetries);
                    // No lanzar excepción para permitir que la aplicación inicie aunque el seeding falle
                    return;
                }
            }
        }
    }

    private static async Task SeedPlansAsync(PgDbContext context, ILogger logger)
    {
        try
        {
            if (await context.Plans.AnyAsync())
            {
                logger.LogInformation("Los planes ya existen en la base de datos. Omitiendo seeding de planes.");
                return;
            }

            var plans = new[]
            {
                new Plan
                {
                    PlanId = 1,
                    PlanName = PlanType.Free,
                    MaxDatabasesPerEngine = 2,
                    PriceInCOP = 0,
                    DurationInDays = 0,
                    Description = "Plan gratuito con 2 bases por motor"
                },
                new Plan
                {
                    PlanId = 2,
                    PlanName = PlanType.Intermediate,
                    MaxDatabasesPerEngine = 5,
                    PriceInCOP = 5000,
                    DurationInDays = 30,
                    Description = "Plan intermedio con soporte prioritario"
                },
                new Plan
                {
                    PlanId = 3,
                    PlanName = PlanType.Advanced,
                    MaxDatabasesPerEngine = 10,
                    PriceInCOP = 10000,
                    DurationInDays = 30,
                    Description = "Plan avanzado con soporte 24/7"
                }
            };

            await context.Plans.AddRangeAsync(plans);
            await context.SaveChangesAsync();
            logger.LogInformation("Seeding de planes completado exitosamente.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error durante el seeding de planes.");
            throw;
        }
    }

    private static async Task SeedDatabaseEnginesAsync(PgDbContext context, ILogger logger)
    {
        try
        {
            var engines = new[]
            {
                (DatabaseEngineType.MySQL, 3307, "Instancias MySQL administradas"),
                (DatabaseEngineType.PostgreSQL, 5432, "Instancias PostgreSQL administradas"),
                (DatabaseEngineType.MongoDB, 27017, "Clusters MongoDB administrados"),
                (DatabaseEngineType.SQLServer, 1433, "Instancias SQL Server administradas"),
                (DatabaseEngineType.Redis, 6379, "Caches Redis administradas"),
                (DatabaseEngineType.Cassandra, 9042, "Clusters Cassandra administrados")
            };

            var existingEngines = await context.DatabaseEngines.ToListAsync();

            foreach (var (engineType, port, description) in engines)
            {
                if (existingEngines.Any(e => e.EngineName == engineType))
                {
                    continue;
                }

                await context.DatabaseEngines.AddAsync(new DatabaseEngine
                {
                    EngineId = Guid.NewGuid(),
                    EngineName = engineType,
                    DefaultPort = port,
                    Description = description,
                    IsActive = true
                });
            }

            await context.SaveChangesAsync();
            logger.LogInformation("Seeding de motores de base de datos completado exitosamente.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error durante el seeding de motores de base de datos.");
            throw;
        }
    }
}


using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ZenCloud.Data.DbContext;
using ZenCloud.Data.Entities;

namespace ZenCloud.Data.Seed;

public static class DataSeeder
{
    public static async Task SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<PgDbContext>();

        await SeedPlansAsync(context);
        await SeedDatabaseEnginesAsync(context);
    }

    private static async Task SeedPlansAsync(PgDbContext context)
    {
        if (await context.Plans.AnyAsync())
        {
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
    }

    private static async Task SeedDatabaseEnginesAsync(PgDbContext context)
    {
        var engines = new[]
        {
            (DatabaseEngineType.MySQL, 3306, "Instancias MySQL administradas"),
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
    }
}


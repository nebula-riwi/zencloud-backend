using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using ZenCloud.Data.DbContext;
using Microsoft.OpenApi.Models;
using DotNetEnv;
using ZenCloud.Data.Repositories.Interfaces;
using ZenCloud.Data.Repositories.Implementations;
using ZenCloud.Services.Interfaces;
using ZenCloud.Services.Implementations;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using ZenCloud.Services;
using FluentValidation;
using FluentValidation.AspNetCore;
using ZenCloud.Validators;
using ZenCloud.Middleware;
using AspNetCoreRateLimit;
using System.Reflection;
using ZenCloud.Data.Seed;
using System.Data.Common;

// Cargar variables de entorno desde .env si existe
try
{
    var envPath = Path.Combine(Directory.GetCurrentDirectory(), ".env");
    if (File.Exists(envPath))
    {
        // Leer el archivo y filtrar líneas inválidas antes de cargar
        var envContent = File.ReadAllLines(envPath);
        var validLines = envContent
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Where(line => line.Trim().StartsWith("#") || line.Contains("="))
            .Where(line => !line.Trim().StartsWith("=") && !line.Trim().All(c => c == '=' || c == '-'))
            .ToList();
        
        // Crear un archivo temporal con solo las líneas válidas
        var tempEnvPath = Path.Combine(Path.GetTempPath(), $"zencloud_env_{Guid.NewGuid()}.env");
        File.WriteAllLines(tempEnvPath, validLines);
        
        try
        {
            Env.Load(tempEnvPath);
        }
        finally
        {
            // Limpiar archivo temporal
            try { File.Delete(tempEnvPath); } catch { }
        }
    }
    else
    {
        // Intentar cargar desde el directorio raíz del proyecto
        var rootEnvPath = Path.Combine(Directory.GetCurrentDirectory(), "..", ".env");
        if (File.Exists(rootEnvPath))
        {
            Env.Load(rootEnvPath);
        }
    }
}
catch (Exception ex)
{
    // Si hay un error cargando .env, continuar sin él (las variables pueden estar en el sistema)
    Console.WriteLine($"Advertencia: No se pudo cargar el archivo .env: {ex.Message}");
    Console.WriteLine("   Continuando con variables de entorno del sistema...");
}

var builder = WebApplication.CreateBuilder(args);

// Configurar FluentValidation
builder.Services.AddFluentValidationAutoValidation();
builder.Services.AddFluentValidationClientsideAdapters();
builder.Services.AddValidatorsFromAssemblyContaining<LoginRequestValidator>();

builder.Services.AddControllers(options =>
{
    // Asegurar que las rutas se resuelvan correctamente
    options.SuppressAsyncSuffixInActionNames = false;
})
    .AddNewtonsoftJson(options =>
    {
        options.SerializerSettings.ReferenceLoopHandling = Newtonsoft.Json.ReferenceLoopHandling.Ignore;
    });

// Configurar Rate Limiting
builder.Services.AddMemoryCache();
builder.Services.Configure<IpRateLimitOptions>(builder.Configuration.GetSection("IpRateLimiting"));
builder.Services.AddInMemoryRateLimiting();
builder.Services.AddSingleton<IRateLimitConfiguration, RateLimitConfiguration>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy
            .WithOrigins(
                "http://localhost:3000",
                "http://localhost:5173",
                "https://localhost:3000",
                "https://localhost:5173",
                "https://nebula.andrescortes.dev",
                "http://nebula.andrescortes.dev",
                "https://n8n.nebula.andrescortes.dev")
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials()
            .SetPreflightMaxAge(TimeSpan.FromMinutes(10))
            .WithExposedHeaders("Content-Type", "Authorization", "X-Requested-With");
    });
    
    // Política adicional para desarrollo (más permisiva)
    if (builder.Environment.IsDevelopment())
    {
        options.AddPolicy("AllowAll", policy =>
        {
            policy
                .AllowAnyOrigin()
                .AllowAnyHeader()
                .AllowAnyMethod();
    });
    }
});
builder.Services.AddEndpointsApiExplorer();

// HttpContextAccessor para AuditService
builder.Services.AddHttpContextAccessor();

// Repositories (Data Access)
builder.Services.AddScoped<IPaymentRepository, PaymentRepository>();
builder.Services.AddScoped<IWebhookRepository, WebhookRepository>();
builder.Services.AddScoped<IDatabaseQueryHistoryRepository, DatabaseQueryHistoryRepository>();

// Services (Business Logic)
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<IJwtService, JwtService>();
// HttpClientFactory para WebhookService con configuración optimizada
builder.Services.AddHttpClient("WebhookClient")
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        MaxConnectionsPerServer = 10,
        AllowAutoRedirect = false
    })
    .SetHandlerLifetime(TimeSpan.FromMinutes(5));
builder.Services.AddScoped<IEmailVerificationService, EmailVerificationService>();
builder.Services.AddScoped<ISubscriptionRepository, SubscriptionRepository>();
builder.Services.AddScoped<IPlanRepository, PlanRepository>();
builder.Services.AddScoped<PasswordHasher>();
builder.Services.AddScoped<MercadoPagoService>();

builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo 
    { 
        Title = "ZenCloud API - CrudCloudDb Platform", 
        Version = "v1",
        Description = @"
## Descripción
API REST para la plataforma ZenCloud (CrudCloudDb Platform), un servicio de gestión automatizada de bases de datos en la nube.

## Autenticación
Esta API utiliza autenticación JWT (JSON Web Tokens). Para acceder a los endpoints protegidos, incluye el token en el header:
```
Authorization: Bearer {tu_token_jwt}
```

## Límites de Rate Limiting
- **General**: 60 solicitudes por minuto, 1000 por hora
- **Login**: 5 intentos por minuto
- **Registro**: 3 intentos por minuto
- **Recuperación de contraseña**: 3 intentos por hora
- **Creación de bases de datos**: 10 por minuto
- **Eliminación de bases de datos**: 5 por minuto

## Códigos de Estado
- **200 OK**: Solicitud exitosa
- **400 Bad Request**: Solicitud inválida
- **401 Unauthorized**: No autenticado
- **403 Forbidden**: No autorizado
- **404 Not Found**: Recurso no encontrado
- **409 Conflict**: Conflicto de negocio
- **422 Unprocessable Entity**: Error de validación
- **429 Too Many Requests**: Límite de solicitudes excedido
- **500 Internal Server Error**: Error del servidor

## Soporte
Para más información, visita: https://nebula.andrescortes.dev
        ",
        Contact = new OpenApiContact
        {
            Name = "ZenCloud Support",
            Email = "support@zencloud.dev"
        },
        License = new OpenApiLicense
        {
            Name = "MIT License"
        }
    });

    // Incluir comentarios XML si existen
    var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
    {
        options.IncludeXmlComments(xmlPath);
    }

    // Agregar la definición del esquema de seguridad (Authorization)
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "Ingrese 'Bearer' [espacio] seguido de su token JWT. Ejemplo: \"Bearer {token}\"",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer",
        BearerFormat = "JWT"
    });

    // Requerir la seguridad en todas las operaciones de la API
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });

    // Configurar ejemplos y descripciones
    options.EnableAnnotations();
    options.UseInlineDefinitionsForEnums();
});

var connectionString = ResolveConnectionString(builder.Configuration);

builder.Services.AddDbContext<PgDbContext>(options =>
    options.UseNpgsql(connectionString));

// JWT Configuration
var jwtKey = Environment.GetEnvironmentVariable("JWT_KEY") ?? 
             throw new ArgumentNullException("JWT_KEY no está configurada");
var jwtIssuer = Environment.GetEnvironmentVariable("JWT_ISSUER") ?? "zencloud-api";
var jwtAudience = Environment.GetEnvironmentVariable("JWT_AUDIENCE") ?? "zencloud-users";

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ClockSkew = TimeSpan.Zero
        };
    });

builder.Services.AddAuthorization();

//Repositories
builder.Services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
builder.Services.AddScoped<IDatabaseInstanceRepository, DatabaseInstanceRepository>();
builder.Services.AddScoped<IUserRepository, UserRepository>();


//Services
builder.Services.AddScoped<IDatabaseInstanceService, DatabaseInstanceService>();
builder.Services.AddScoped<ICredentialsGeneratorService, CredentialsGeneratorService>();
builder.Services.AddScoped<IPlanValidationService, PlanValidationService>();
builder.Services.AddScoped<IDatabaseManagementService, DatabaseManagementService>();
builder.Services.AddScoped<IMySQLConnectionManager, MySQLConnectionManager>();
builder.Services.AddScoped<IQueryExecutor, MySQLQueryExecutor>();
builder.Services.AddScoped<IPostgresQueryExecutor, PostgresQueryExecutor>();
builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddScoped<IEncryptionService, EncryptionService>();
builder.Services.AddScoped<IDatabaseEngineService, DatabaseEngineService>();
builder.Services.AddScoped<IWebhookService, WebhookService>();
builder.Services.AddHostedService<SubscriptionLifecycleService>();

var app = builder.Build();

await DataSeeder.SeedAsync(app.Services);

// Configure the HTTP request pipeline.

// CORS debe ir PRIMERO, antes de UseRouting y cualquier otro middleware
// El orden correcto es: CORS -> Routing -> Authentication/Authorization -> Rate Limiting
if (app.Environment.IsDevelopment())
{
    app.UseCors("AllowAll");
}
else
{
    app.UseCors("AllowFrontend");
}

// Routing debe ir después de CORS
app.UseRouting();

// Autenticación y autorización (debe ir después de UseRouting, antes de Rate Limiting)
// Esto permite que CORS responda a OPTIONS antes de cualquier limitación
app.UseAuthentication();
app.UseAuthorization();

// Middleware de manejo global de excepciones
app.UseMiddleware<GlobalExceptionHandlerMiddleware>();

// Rate Limiting (después de CORS, Routing, Authentication y Authorization)
// Las peticiones OPTIONS (preflight) están en la whitelist, así que no deberían bloquearse
app.UseIpRateLimiting();

app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "ZenCloud API v1");
    options.RoutePrefix = "swagger";
    options.DocumentTitle = "ZenCloud API Documentation";
    options.DefaultModelsExpandDepth(-1); // Ocultar modelos por defecto
    options.DocExpansion(Swashbuckle.AspNetCore.SwaggerUI.DocExpansion.List);
});

// HTTPS redirection puede causar problemas en algunos entornos de producción
// Comentado temporalmente para debugging
// app.UseHttpsRedirection();

// Mapear controladores
app.MapControllers();

// Endpoint raíz de verificación
app.MapGet("/", () => Results.Json(new
{
    status = "OK",
    service = "ZenCloud API",
    environment = app.Environment.EnvironmentName,
    timestamp = DateTime.UtcNow
}));

// Health check mejorado con métricas
app.MapGet("/health", async (HttpContext context, PgDbContext dbContext) =>
{
    var health = new
    {
        status = "Healthy",
        timestamp = DateTime.UtcNow,
        version = "1.0.0",
        services = new Dictionary<string, object>()
    };
    
    var services = new Dictionary<string, object>();
    
    // Verificar base de datos principal
    try
    {
        await dbContext.Database.CanConnectAsync();
        services["database"] = new { status = "OK", responseTime = "<100ms" };
    }
    catch (Exception ex)
    {
        services["database"] = new { status = "ERROR", error = ex.Message };
        return Results.Json(new { status = "Unhealthy", timestamp = DateTime.UtcNow, services }, statusCode: 503);
    }
    
    // Verificar memoria disponible
    var memoryInfo = GC.GetGCMemoryInfo();
    services["memory"] = new
    {
        status = "OK",
        totalMemoryMB = memoryInfo.TotalAvailableMemoryBytes / 1024 / 1024,
        heapMemoryMB = memoryInfo.HeapSizeBytes / 1024 / 1024
    };
    
    return Results.Json(new { health.status, health.timestamp, health.version, services });
});

app.Run();

static string ResolveConnectionString(ConfigurationManager configuration)
{
    var raw = Environment.GetEnvironmentVariable("CONNECTION_STRING");

    if (string.IsNullOrWhiteSpace(raw))
    {
        raw = configuration.GetConnectionString("DefaultConnection");
    }

    if (string.IsNullOrWhiteSpace(raw))
    {
        raw = BuildConnectionStringFromPostgresEnv();
    }

    if (string.IsNullOrWhiteSpace(raw))
    {
        throw new InvalidOperationException("Debe configurar la variable de entorno CONNECTION_STRING o ConnectionStrings:DefaultConnection.");
    }

    raw = NormalizeConnectionString(raw);

    var forceInternalHost = Environment.GetEnvironmentVariable("FORCE_INTERNAL_CONNECTION");
    if (!string.Equals(forceInternalHost, "false", StringComparison.OrdinalIgnoreCase))
    {
        var internalHost = Environment.GetEnvironmentVariable("POSTGRES_HOST");
        var internalPort = Environment.GetEnvironmentVariable("POSTGRES_INTERNAL_PORT")
                           ?? Environment.GetEnvironmentVariable("POSTGRES_PORT");

        raw = TryOverrideConnectionSegment(raw, "Host", internalHost);
        raw = TryOverrideConnectionSegment(raw, "Server", internalHost);
        raw = TryOverrideConnectionSegment(raw, "Port", internalPort);
    }

    return raw;
}

static string NormalizeConnectionString(string raw)
{
    raw = raw.Trim();
    raw = raw.Replace("\r", string.Empty)
             .Replace("\n", string.Empty);

    if (raw.StartsWith("\"") && raw.EndsWith("\"") && raw.Length > 1)
    {
        raw = raw[1..^1];
    }

    return raw;
}

static string BuildConnectionStringFromPostgresEnv()
{
    var host = Environment.GetEnvironmentVariable("POSTGRES_HOST");
    var port = Environment.GetEnvironmentVariable("POSTGRES_INTERNAL_PORT")
               ?? Environment.GetEnvironmentVariable("POSTGRES_PORT");
    var database = Environment.GetEnvironmentVariable("POSTGRES_APP_DATABASE")
                   ?? Environment.GetEnvironmentVariable("POSTGRES_DEFAULT_DATABASE");
    var username = Environment.GetEnvironmentVariable("POSTGRES_APP_USER")
                   ?? Environment.GetEnvironmentVariable("POSTGRES_ADMIN_USER");
    var password = Environment.GetEnvironmentVariable("POSTGRES_APP_PASSWORD")
                   ?? Environment.GetEnvironmentVariable("POSTGRES_ADMIN_PASSWORD");

    if (string.IsNullOrWhiteSpace(host) ||
        string.IsNullOrWhiteSpace(port) ||
        string.IsNullOrWhiteSpace(database) ||
        string.IsNullOrWhiteSpace(username) ||
        string.IsNullOrWhiteSpace(password))
    {
        return string.Empty;
    }

    return $"Host={host};Port={port};Database={database};Username={username};Password={password};SSL Mode=Disable;";
}

static string TryOverrideConnectionSegment(string connectionString, string key, string? value)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        return connectionString;
    }

    // Usar regex para reemplazar la clave en la cadena de conexión directamente
    // Esto evita problemas con DbConnectionStringBuilder que no puede parsear "SSL Mode" con espacio
    var pattern = $@"({key}\s*=\s*)([^;]+)(;|\s*$)";
    var replacement = $@"${{1}}{value}${{3}}";
    
    if (System.Text.RegularExpressions.Regex.IsMatch(connectionString, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase))
    {
        return System.Text.RegularExpressions.Regex.Replace(connectionString, pattern, replacement, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }
    
    // Si la clave no existe, no modificamos la cadena
    return connectionString;
}

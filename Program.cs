using Microsoft.EntityFrameworkCore;
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

// Services (Business Logic)
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<IJwtService, JwtService>();
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

builder.Services.AddDbContext<PgDbContext>(options =>
    options.UseNpgsql(Environment.GetEnvironmentVariable("CONNECTION_STRING")));

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
builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddScoped<IEncryptionService, EncryptionService>();
builder.Services.AddScoped<IDatabaseEngineService, DatabaseEngineService>();
builder.Services.AddScoped<IWebhookService, WebhookService>();

// HttpClientFactory para WebhookService
builder.Services.AddHttpClient();

var app = builder.Build();

// Configure the HTTP request pipeline.

// CORS debe ir PRIMERO, antes de cualquier otro middleware
// En desarrollo, usar política más permisiva
if (app.Environment.IsDevelopment())
{
    app.UseCors("AllowAll");
}
else
{
    app.UseCors("AllowFrontend");
}

// Routing debe ir antes de otros middlewares
app.UseRouting();

// Middleware de manejo global de excepciones
app.UseMiddleware<GlobalExceptionHandlerMiddleware>();

// Rate Limiting (después de CORS pero antes de autenticación)
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

// Autenticación y autorización
app.UseAuthentication();
app.UseAuthorization();

// Endpoints
app.UseEndpoints(endpoints =>
{
    endpoints.MapControllers();
});

// Endpoint raíz de verificación
app.MapGet("/", () => Results.Json(new
{
    status = "OK",
    service = "ZenCloud API",
    environment = app.Environment.EnvironmentName,
    timestamp = DateTime.UtcNow
}));

// Health check simple
app.MapGet("/health", () => Results.Ok("Healthy"));

app.Run();

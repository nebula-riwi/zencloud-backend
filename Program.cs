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

Env.Load();

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// 🔥 AGREGAR ESTA LÍNEA (HttpContextAccessor para AuditService)
builder.Services.AddHttpContextAccessor();

// Repositories (Data Access)
builder.Services.AddScoped<IPaymentRepository, PaymentRepository>();

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
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "Your API", Version = "v1" });
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

var app = builder.Build();

// Configure the HTTP request pipeline.

app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "Your API V1");
    options.RoutePrefix = "swagger";
});


app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// Endpoint raíz de verificación
app.MapGet("/", () => Results.Json(new
{
    status = "OK",
    service = "ZenCloud API",
    environment = app.Environment.EnvironmentName,
    timestamp = DateTime.UtcNow
}));

// Health check simple
app.MapGet("/health", () => Results.Ok("Healthy ✅"));

app.Run();

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

// Repositories (Data Access)
builder.Services.AddScoped<IPaymentRepository, PaymentRepository>();

// Services (Business Logic)
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<IJwtService, JwtService>();
builder.Services.AddScoped<IEmailVerificationService, EmailVerificationService>();
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

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Your API V1");
        options.RoutePrefix = "swagger";
    });
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
using Microsoft.EntityFrameworkCore;
using ZenCloud.Data.DbContext;

using Microsoft.OpenApi.Models;
using DotNetEnv; // Add this
using ZenCloud.Data.Repositories.Interfaces; // Add this
using ZenCloud.Data.Repositories.Implementations; // Add this
using ZenCloud.Services;

// Load environment variables from .env file
Env.Load();

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();



builder.Services.AddScoped<IPaymentRepository, PaymentRepository>();
builder.Services.AddScoped<ZenCloud.Services.MercadoPagoService>();

builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "Your API", Version = "v1" });
});


builder.Services.AddDbContext<PgDbContext>(options =>
    options.UseNpgsql(System.Environment.GetEnvironmentVariable("CONNECTION_STRING"))); // Use environment variable

// Register the services:
builder.Services.AddScoped<ZenCloud.Data.Repositories.Interfaces.IEmailService, ZenCloud.Data.Repositories.Implementations.EmailService>();
builder.Services.AddScoped<ZenCloud.Data.Repositories.Interfaces.IAuthService, ZenCloud.Data.Repositories.Implementations.AuthServiceImpl>();
builder.Services.AddScoped<PasswordHasher>(); // Corrected

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Your API V1");
        options.RoutePrefix = "swagger"; // Esto es opcional, pero recomendable
    });
}

app.UseHttpsRedirection();
app.MapControllers();

app.Run();
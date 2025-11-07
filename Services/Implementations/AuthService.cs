using Microsoft.EntityFrameworkCore;
using ZenCloud.Data.Entities;
using ZenCloud.DTOs;
using System.Threading.Tasks;
using System;
using ZenCloud.Data.DbContext;
using ZenCloud.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace ZenCloud.Services.Implementations;

public class AuthService : IAuthService
{
    private readonly PgDbContext _dbContext;
    private readonly PasswordHasher _passwordHasher;
    private readonly IEmailService _emailService;
    private readonly IJwtService _jwtService;
    private readonly IEmailVerificationService _emailVerificationService;
    private readonly ILogger<AuthService> _logger;

    public AuthService(PgDbContext dbContext, PasswordHasher passwordHasher, IEmailService emailService, IJwtService jwtService, IEmailVerificationService emailVerificationService, ILogger<AuthService> logger) 
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
        _emailService = emailService;
        _jwtService = jwtService;
        _emailVerificationService = emailVerificationService;
        _logger = logger;
    }

    public async Task<bool> RegisterAsync(RegisterRequest model)
    {
        if (string.IsNullOrEmpty(model.Email) || string.IsNullOrEmpty(model.Password) || string.IsNullOrEmpty(model.ConfirmPassword))
        {
            return false; 
        }

        if (model.Password != model.ConfirmPassword)
        {
            return false; 
        }

        if (await _dbContext.Users.AnyAsync(u => u.Email == model.Email))
        {
            return false; 
        }

        string hashedPassword = _passwordHasher.HashPassword(model.Password);
        string emailVerificationToken = _emailVerificationService.GenerateVerificationToken(model.Email);

        var newUser = new User
        {
            UserId = Guid.NewGuid(),
            Email = model.Email,
            PasswordHash = hashedPassword,
            EmailVerificationToken = emailVerificationToken,
            IsEmailVerified = false,
            FullName = model.FullName
        };

        _dbContext.Users.Add(newUser);
        await _dbContext.SaveChangesAsync();
        

        await _emailService.SendVerificationEmailAsync(newUser.Email, emailVerificationToken);
        return true; 
    }
    
    public async Task<bool> VerifyEmailAsync(string email, string token)
    {
        _logger.LogInformation($"Verificando email: {email}");

        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Email == email);

        if (user == null)
        {
            _logger.LogWarning($"Usuario no encontrado: {email}");
            return false;
        }

        _logger.LogInformation($"Usuario encontrado, token BD: {user.EmailVerificationToken}");
        _logger.LogInformation($"Token recibido: {token}");

        var (isValid, tokenEmail) = _emailVerificationService.ValidateVerificationToken(token);
    
        _logger.LogInformation($"Validación JWT: {isValid}, Email del token: {tokenEmail}");

        if (!isValid)
        {
            _logger.LogWarning("Token JWT inválido");
            return false;
        }

        if (tokenEmail != email)
        {
            _logger.LogWarning($"Email no coincide: {tokenEmail} != {email}");
            return false;
        }

        if (user.EmailVerificationToken != token)
        {
            _logger.LogWarning("Token no coincide con BD");
            return false;
        }

        user.IsEmailVerified = true;
        user.EmailVerificationToken = null;
        user.UpdatedAt = DateTime.UtcNow;
    
        await _dbContext.SaveChangesAsync();
        _logger.LogInformation("Email verificado exitosamente");
        return true;
    }
    public async Task<string> LoginAsync(LoginRequest model)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(model.Email) || string.IsNullOrWhiteSpace(model.Password))
            {
                return null;
            }

            var user = await _dbContext.Users
                .FirstOrDefaultAsync(u => u.Email.ToLower() == model.Email.ToLower().Trim());

            if (user == null || !user.IsActive)
            {
                return null;
            }

            if (!user.IsEmailVerified)
            {
                return null;
            }

            bool isPasswordValid = _passwordHasher.VerifyPassword(model.Password, user.PasswordHash);
            if (!isPasswordValid)
            {
                return null;
            }

            user.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();

            string token = _jwtService.GenerateToken(user);
            return token;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error en LoginAsync");
            return null;
        }
    }

    public async Task<bool> RequestPasswordResetAsync(string email)
    {
        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (user == null)
        {
            return true;
        }

        string resetToken = _emailVerificationService.GenerateVerificationToken(email);
        
        user.PasswordResetToken = resetToken;
        user.PasswordResetExpiry = DateTime.UtcNow.AddHours(1);
        user.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();

        await _emailService.SendPasswordResetEmailAsync(email, resetToken);
        
        _logger.LogInformation($"Solicitud de restablecimiento de contraseña para: {email}");
        return true;
    }
    
    public async Task<bool> ResetPasswordAsync(string email, string token, string newPassword)
    {
        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (user == null)
        {
            return false;
        }

        if (user.PasswordResetToken != token || user.PasswordResetExpiry == null || user.PasswordResetExpiry < DateTime.UtcNow)
        {
            return false;
        }

        var (isValid, tokenEmail) = _emailVerificationService.ValidateVerificationToken(token);
        if (!isValid || tokenEmail != email)
        {
            return false;
        }

        string hashedPassword = _passwordHasher.HashPassword(newPassword);
        user.PasswordHash = hashedPassword;
        user.PasswordResetToken = null;
        user.PasswordResetExpiry = null;
        user.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation($"Contraseña restablecida exitosamente para: {email}");
        return true;
    }
}

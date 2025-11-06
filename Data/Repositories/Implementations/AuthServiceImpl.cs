using Microsoft.EntityFrameworkCore;
using ZenCloud.Data.Entities;
using ZenCloud.Data.Repositories.Interfaces;
using System.Threading.Tasks;
using System;
using ZenCloud.Services;
using ZenCloud.Data.DbContext;

namespace ZenCloud.Data.Repositories.Implementations;

public class AuthServiceImpl : IAuthService
{
    private readonly PgDbContext _dbContext;
    private readonly PasswordHasher _passwordHasher;
    private readonly IEmailService _emailService;

    public AuthServiceImpl(PgDbContext dbContext, PasswordHasher passwordHasher, IEmailService emailService) 
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
        _emailService = emailService; 
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

       
        string emailVerificationToken = Guid.NewGuid().ToString();

    
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


        await _emailService.SendVerificationEmailAsync(newUser.Email, emailVerificationToken); // Envía el correo

        return true; 
    }
    
    public async Task<bool> VerifyEmailAsync(string email, string token)
    {
        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Email == email);

        if (user == null)
        {
            return false;
        }

 
        if (user.EmailVerificationToken != token)
        {
            return false;
        }


        user.IsEmailVerified = true;
        user.EmailVerificationToken = null; 
        
        await _dbContext.SaveChangesAsync();

        return true;
    }
    public async Task<string> LoginAsync(LoginRequest model)
    {
        
        return null;
    }

    public async Task<bool> RequestPasswordResetAsync(string email)
    {
        
        return true;
    }

    public async Task<bool> ResetPasswordAsync(string email, string token, string newPassword)
    {
        
        return true;
    }
}
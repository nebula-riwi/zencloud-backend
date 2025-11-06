using ZenCloud.DTOs;

namespace ZenCloud.Services.Interfaces;

public interface IAuthService
{
    Task<bool> RegisterAsync(RegisterRequest model);
    Task<bool> VerifyEmailAsync(string email, string token);
    Task<string> LoginAsync(LoginRequest model);
    Task<bool> RequestPasswordResetAsync(string email);
    Task<bool> ResetPasswordAsync(string email, string token, string newPassword);
}

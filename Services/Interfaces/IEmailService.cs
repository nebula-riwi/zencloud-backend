using System.Threading.Tasks;

namespace ZenCloud.Services.Interfaces;

public interface IEmailService
{
    Task SendVerificationEmailAsync(string email, string verificationToken);
    Task SendPasswordResetEmailAsync(string email, string token);
}
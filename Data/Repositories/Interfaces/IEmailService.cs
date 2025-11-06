using System.Threading.Tasks;

namespace ZenCloud.Data.Repositories.Interfaces;

public interface IEmailService
{
    Task SendVerificationEmailAsync(string email, string verificationToken);
}
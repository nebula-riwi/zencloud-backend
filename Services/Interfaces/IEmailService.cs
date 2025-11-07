using System.Threading.Tasks;

namespace ZenCloud.Services.Interfaces;

public interface IEmailService
{
    Task SendVerificationEmailAsync(string email, string verificationToken);
    Task SendPasswordResetEmailAsync(string email, string resetToken);
    Task SendPaymentConfirmationEmailAsync(string email,
        string userName,
        string paymentType,
        decimal amount,
        string status,
        DateTime date);
    Task SendDatabaseCredentialsEmailAsync(
        string toEmail,
        string userName,
        string engineName,
        string databaseName,
        string dbUser,
        string dbPassword,
        string host,
        int port);

}
using System.Threading.Tasks;

namespace ZenCloud.Services.Interfaces;

public interface IEmailService
{
    Task SendVerificationEmailAsync(string email, string verificationToken);
    Task SendPasswordResetEmailAsync(string email, string resetToken);
    Task SendPaymentConfirmationEmailAsync(string email, string userName, string paymentType, decimal amount, string status, DateTime date);
    Task SendPlanChangeEmailAsync(string email, string userName, string planName, DateTime effectiveDate);
    Task SendDatabaseCredentialsEmailAsync(string toEmail, string userName, string engineName, string databaseName, string dbUser, string dbPassword, string host, int port);
    Task SendDatabaseDeletionEmailAsync(string toEmail, string userName, string databaseName, string engineName, DateTime deletionDate);
    Task SendSubscriptionExpiringEmailAsync(string email, string userName, string planName, DateTime expiryDate);
    Task SendSubscriptionExpiredEmailAsync(string email, string userName, string planName);
}
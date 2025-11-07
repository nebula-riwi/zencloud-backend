using MailKit.Net.Smtp;
using MimeKit;
using System.Threading.Tasks;
using ZenCloud.Data.Repositories.Interfaces;
using System.IO;
using ZenCloud.Services.Interfaces;

namespace ZenCloud.Services.Implementations;

public class EmailService : IEmailService 
{
    private readonly string _smtpServer;
    private readonly int _smtpPort;
    private readonly string _smtpUsername;
    private readonly string _smtpPassword;
    private readonly string _fromEmail;
    private readonly string _fromName;
    private readonly string _verificationEmailTemplate;

    public EmailService()
    {
        _smtpServer = System.Environment.GetEnvironmentVariable("SMTP_SERVER");
        _smtpPort = int.Parse(System.Environment.GetEnvironmentVariable("SMTP_PORT") ?? "587");
        _smtpUsername = System.Environment.GetEnvironmentVariable("SMTP_USERNAME");
        _smtpPassword = System.Environment.GetEnvironmentVariable("SMTP_PASSWORD");
        _fromEmail = System.Environment.GetEnvironmentVariable("SMTP_FROMEMAIL");
        _fromName = System.Environment.GetEnvironmentVariable("SMTP_FROMNAME");

        _verificationEmailTemplate = File.ReadAllText("Templates/VerificationEmailTemplate.html");
    }

    public async Task SendVerificationEmailAsync(string email, string verificationToken)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_fromName, _fromEmail));
        message.To.Add(new MailboxAddress(email, email));
        message.Subject = "Verify Your Email";

        string body = _verificationEmailTemplate
            .Replace("[Nombre del Usuario]", email)
            .Replace("[Enlace de Verificación]", $"https://service.nebula.andrescortes.dev/Auth/verify?email={email}&token={verificationToken}");

        message.Body = new TextPart("html")
        {
            Text = body
        };

        using (var client = new SmtpClient())
        {
            await client.ConnectAsync(_smtpServer, _smtpPort, false);
            await client.AuthenticateAsync(_smtpUsername, _smtpPassword);
            await client.SendAsync(message);
            await client.DisconnectAsync(true);
        }
    }

    public async Task SendPasswordResetEmailAsync(string email, string resetToken)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_fromName, _fromEmail));
        message.To.Add(new MailboxAddress(email, email));
        message.Subject = "Restablecer tu contraseña - ZenCloud";

        // Cargar plantilla de restablecimiento de contraseña
        string passwordResetTemplate = File.ReadAllText("Templates/PasswordResetEmailTemplate.html");
        string body = passwordResetTemplate
            .Replace("[Nombre del Usuario]", email)
            .Replace("[Enlace de Restablecimiento]", $"http://localhost:5089/Auth/reset-password?email={email}&token={resetToken}");

        message.Body = new TextPart("html")
        {
            Text = body
        };

        using (var client = new SmtpClient())
        {
            await client.ConnectAsync(_smtpServer, _smtpPort, false);
            await client.AuthenticateAsync(_smtpUsername, _smtpPassword);
            await client.SendAsync(message);
            await client.DisconnectAsync(true);
        }
    }
    
    public async Task SendPaymentConfirmationEmailAsync(
        string email,
        string userName,
        string paymentType,
        decimal amount,
        string status,
        DateTime date)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_fromName, _fromEmail));
        message.To.Add(new MailboxAddress(userName, email));
        message.Subject = "Confirmación de tu pago - ZenCloud";

        string templatePath = "Templates/PaymentConfirmationEmailTemplate.html";
        string bodyTemplate = File.ReadAllText(templatePath);

        string body = bodyTemplate
            .Replace("[Nombre del Usuario]", userName)
            .Replace("[Tipo de Pago]", paymentType)
            .Replace("[Monto]", $"${amount:N0} COP")
            .Replace("[Estado]", status)
            .Replace("[Fecha]", date.ToString("dd/MM/yyyy HH:mm"));

        message.Body = new TextPart("html") { Text = body };

        using (var client = new SmtpClient())
        {
            await client.ConnectAsync(_smtpServer, _smtpPort, false);
            await client.AuthenticateAsync(_smtpUsername, _smtpPassword);
            await client.SendAsync(message);
            await client.DisconnectAsync(true);
        }
    }
    
    public async Task SendDatabaseCredentialsEmailAsync(
        string toEmail,
        string userName,
        string engineName,
        string databaseName,
        string dbUser,
        string dbPassword,
        string host,
        int port)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_fromName, _fromEmail));
        message.To.Add(new MailboxAddress(userName ?? toEmail, toEmail));
        message.Subject = "Credenciales de tu nueva base de datos - ZenCloud";

        string templatePath = "Templates/DatabaseCredentialsTemplate.html";
        string htmlTemplate = await File.ReadAllTextAsync(templatePath);

        string body = htmlTemplate
            .Replace("[Nombre del Usuario]", System.Net.WebUtility.HtmlEncode(userName ?? toEmail))
            .Replace("[Motor]", System.Net.WebUtility.HtmlEncode(engineName))
            .Replace("[Nombre de la Base]", System.Net.WebUtility.HtmlEncode(databaseName))
            .Replace("[Usuario DB]", System.Net.WebUtility.HtmlEncode(dbUser))
            .Replace("[Contraseña DB]", System.Net.WebUtility.HtmlEncode(dbPassword))
            .Replace("[Host]", System.Net.WebUtility.HtmlEncode(host))
            .Replace("[Puerto]", port.ToString());

        message.Body = new TextPart("html")
        {
            Text = body
        };

        using (var client = new SmtpClient())
        {
            await client.ConnectAsync(_smtpServer, _smtpPort, false);
            await client.AuthenticateAsync(_smtpUsername, _smtpPassword);
            await client.SendAsync(message);
            await client.DisconnectAsync(true);
        }
    }

}

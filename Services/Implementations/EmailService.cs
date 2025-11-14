using MailKit.Net.Smtp;
using MailKit.Security;
using System;
using MimeKit;
using System.Threading.Tasks;
using ZenCloud.Data.Repositories.Interfaces;
using System.IO;
using ZenCloud.Data.Entities;
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

        var encodedEmail = Uri.EscapeDataString(email);
        var encodedToken = Uri.EscapeDataString(verificationToken);
        var verificationUrl = $"https://service.nebula.andrescortes.dev/api/Auth/verify?email={encodedEmail}&token={encodedToken}";

        string body = _verificationEmailTemplate
            .Replace("[Nombre del Usuario]", email)
            .Replace("[Enlace de Verificación]", verificationUrl);

        message.Body = new TextPart("html")
        {
            Text = body
        };

        using (var client = new SmtpClient())
        {
            await client.ConnectAsync(_smtpServer, _smtpPort, SecureSocketOptions.StartTls);
            await client.AuthenticateAsync(_smtpUsername, _smtpPassword);
            await client.SendAsync(message);
            await client.DisconnectAsync(true);
        }
    }

    public async Task SendPasswordResetEmailAsync(string email, string resetToken)
    {
        try
        {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_fromName, _fromEmail));
        message.To.Add(new MailboxAddress(email, email));
        message.Subject = "Restablecer tu contraseña - ZenCloud";

        // Cargar plantilla de restablecimiento de contraseña
            string passwordResetTemplate;
            var templatePath = Path.Combine(Directory.GetCurrentDirectory(), "Templates", "PasswordResetEmailTemplate.html");
            
            if (File.Exists(templatePath))
            {
                passwordResetTemplate = await File.ReadAllTextAsync(templatePath);
            }
            else
            {
                // Plantilla simple si no existe el archivo
                passwordResetTemplate = @"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <title>Restablecer Contraseña</title>
</head>
<body style='font-family: Arial, sans-serif; line-height: 1.6; color: #333;'>
    <div style='max-width: 600px; margin: 0 auto; padding: 20px;'>
        <h2 style='color: #e78a53;'>Restablecer tu Contraseña</h2>
        <p>Hola [Nombre del Usuario],</p>
        <p>Has solicitado restablecer tu contraseña. Haz clic en el siguiente enlace para continuar:</p>
        <p style='text-align: center; margin: 30px 0;'>
            <a href='[Enlace de Restablecimiento]' style='background-color: #e78a53; color: white; padding: 12px 24px; text-decoration: none; border-radius: 5px; display: inline-block;'>Restablecer Contraseña</a>
        </p>
        <p>Si no solicitaste este cambio, puedes ignorar este correo.</p>
        <p>Este enlace expirará en 1 hora.</p>
        <hr style='border: none; border-top: 1px solid #eee; margin: 20px 0;'>
        <p style='font-size: 12px; color: #999;'>ZenCloud - Gestión de Bases de Datos en la Nube</p>
    </div>
</body>
</html>";
            }

        string body = passwordResetTemplate
            .Replace("[Nombre del Usuario]", email)
                .Replace("[Enlace de Restablecimiento]", $"https://nebula.andrescortes.dev/reset-password?email={Uri.EscapeDataString(email)}&token={Uri.EscapeDataString(resetToken)}");

        message.Body = new TextPart("html")
        {
            Text = body
        };

        using (var client = new SmtpClient())
        {
            await client.ConnectAsync(_smtpServer, _smtpPort, SecureSocketOptions.StartTls);
            await client.AuthenticateAsync(_smtpUsername, _smtpPassword);
            await client.SendAsync(message);
            await client.DisconnectAsync(true);
            }

            Console.WriteLine($"Email de restablecimiento de contraseña enviado a: {email}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error enviando email de restablecimiento de contraseña: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
            throw;
        }
    }
    
    private async Task SendEmailAsync(string toEmail, string subject, string body)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_fromName, _fromEmail));
        message.To.Add(new MailboxAddress(toEmail, toEmail));
        message.Subject = subject;
        message.Body = new TextPart("html") { Text = body };

        using (var client = new SmtpClient())
        {
            await client.ConnectAsync(_smtpServer, _smtpPort, SecureSocketOptions.StartTls);
            await client.AuthenticateAsync(_smtpUsername, _smtpPassword);
            await client.SendAsync(message);
            await client.DisconnectAsync(true);
        }
    }

    // ✅ Confirmación de pago 
    public async Task SendPaymentConfirmationEmailAsync(
        string email,
        string userName,
        string paymentType,
        decimal amount,
        string status,
        DateTime date)
    {
        var templatePath = "Templates/PaymentConfirmationEmailTemplate.html";
        var template = await File.ReadAllTextAsync(templatePath);

        var body = template
            .Replace("[Nombre del Usuario]", userName)
            .Replace("[Tipo de Pago]", paymentType)
            .Replace("[Monto]", $"${amount:N0} COP")
            .Replace("[Estado]", status)
            .Replace("[Fecha]", date.ToString("dd/MM/yyyy HH:mm"));

        var subject = "Confirmación de tu pago - ZenCloud";

        await SendEmailAsync(email, subject, body);
    }

    // Email de cambio de plan
    public async Task SendPlanChangeEmailAsync(
        string email,
        string userName,
        string planName,
        DateTime effectiveDate)
    {
        var templatePath = "Templates/PlanChangeEmailTemplate.html";
        var template = await File.ReadAllTextAsync(templatePath);

        // Determinar el límite de bases de datos según el plan
        var databaseLimit = planName switch
        {
            "Free" => 2,
            "Intermediate" => 5,
            "Advanced" => 10,
            _ => 2
        };

        var body = template
            .Replace("[Nombre del Usuario]", userName)
            .Replace("[Nombre del Plan]", planName)
            .Replace("[Fecha de Cambio]", effectiveDate.ToString("dd/MM/yyyy"))
            .Replace("[Límite de Bases de Datos]", databaseLimit.ToString());

        var subject = $"Cambio a Plan {planName} - ZenCloud";

        await SendEmailAsync(email, subject, body);
    }

    // Envío de credenciales de base de datos
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
        var templatePath = "Templates/DatabaseCredentialsTemplate.html";
        var template = await File.ReadAllTextAsync(templatePath);

        var body = template
            .Replace("[Nombre del Usuario]", System.Net.WebUtility.HtmlEncode(userName ?? toEmail))
            .Replace("[Motor]", System.Net.WebUtility.HtmlEncode(engineName))
            .Replace("[Nombre de la Base]", System.Net.WebUtility.HtmlEncode(databaseName))
            .Replace("[Usuario DB]", System.Net.WebUtility.HtmlEncode(dbUser))
            .Replace("[Contraseña DB]", System.Net.WebUtility.HtmlEncode(dbPassword))
            .Replace("[Host]", System.Net.WebUtility.HtmlEncode(host))
            .Replace("[Puerto]", port.ToString());

        var subject = $"Credenciales de tu nueva base de datos - ZenCloud";

        await SendEmailAsync(toEmail, subject, body);
    }

    // Email de eliminación de base de datos
    public async Task SendDatabaseDeletionEmailAsync(
        string toEmail,
        string userName,
        string databaseName,
        string engineName,
        DateTime deletionDate)
    {
        var templatePath = "Templates/DatabaseDeletionEmailTemplate.html";
        var template = await File.ReadAllTextAsync(templatePath);

        var body = template
            .Replace("[Nombre del Usuario]", System.Net.WebUtility.HtmlEncode(userName ?? toEmail))
            .Replace("[Nombre de la Base]", System.Net.WebUtility.HtmlEncode(databaseName))
            .Replace("[Motor]", System.Net.WebUtility.HtmlEncode(engineName))
            .Replace("[Fecha de Eliminación]", deletionDate.ToString("dd/MM/yyyy HH:mm"));

        var subject = $"Base de datos {databaseName} eliminada - ZenCloud";

        await SendEmailAsync(toEmail, subject, body);
    }
    
    // Email de suscripción por expirar
    public async Task SendSubscriptionExpiringEmailAsync(
        string email,
        string userName,
        string planName,
        DateTime expiryDate)
    {
        var templatePath = "Templates/SubscriptionExpiringEmailTemplate.html";
        var template = await File.ReadAllTextAsync(templatePath);

        var daysRemaining = (expiryDate - DateTime.UtcNow).Days;

        var body = template
            .Replace("[Nombre del Usuario]", userName)
            .Replace("[Nombre del Plan]", planName)
            .Replace("[Fecha de Expiración]", expiryDate.ToString("dd/MM/yyyy"))
            .Replace("[Días Restantes]", daysRemaining.ToString())
            .Replace("[Enlace de Renovación]", "https://zencloud.com/plans"); // Actualiza con tu URL real

        var subject = $"Tu suscripción a {planName} está por expirar - ZenCloud";

        await SendEmailAsync(email, subject, body);
    }
    
    // Email de suscripción expirada
    public async Task SendSubscriptionExpiredEmailAsync(
        string email,
        string userName,
        string planName)
    {
        var templatePath = "Templates/SubscriptionExpiredEmailTemplate.html";
        var template = await File.ReadAllTextAsync(templatePath);

        var body = template
            .Replace("[Nombre del Usuario]", userName)
            .Replace("[Nombre del Plan]", planName)
            .Replace("[Enlace de Renovación]", "https://zencloud.com/plans");

        var subject = $"Tu suscripción a {planName} ha expirado - ZenCloud";

        await SendEmailAsync(email, subject, body);
    }
}

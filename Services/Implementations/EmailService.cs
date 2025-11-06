using MailKit.Net.Smtp;
using MimeKit;
using System.Threading.Tasks;
using ZenCloud.Data.Repositories.Interfaces;
using System.IO;
using ZenCloud.Services.Interfaces;

namespace ZenCloud.Services.Implementations;

public class EmailService : IEmailService // Solo una vez IEmailService
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
            .Replace("[Enlace de Verificación]", $"http://localhost:5089/Auth/verify?email={email}&token={verificationToken}");

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

    public async Task SendPasswordResetEmailAsync(string email, string token)
    {
        await Task.CompletedTask;
    }
}
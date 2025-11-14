namespace ZenCloud.Data.Entities;

public class User
{
    public Guid UserId { get; set; }
    public string Email { get; set; } = null!;
    public string PasswordHash { get; set; } = null!;
    public string? FullName { get; set; }
    public bool IsEmailVerified { get; set; } = false;
    public string? EmailVerificationToken { get; set; }
    public string? PasswordResetToken { get; set; }
    public DateTime? PasswordResetExpiry { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;

    // Navigation properties
    public ICollection<DatabaseInstance> DatabaseInstances { get; set; } = new List<DatabaseInstance>();
    public ICollection<ErrorLog> ErrorLogs { get; set; } = new List<ErrorLog>();
    public ICollection<AuditLog> AuditLogs { get; set; } = new List<AuditLog>();
    public ICollection<WebhookConfiguration> WebhookConfigurations { get; set; } = new List<WebhookConfiguration>();
    public ICollection<EmailLog> EmailLogs { get; set; } = new List<EmailLog>();
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
    public ICollection<Subscription> Subscriptions { get; set; } = new List<Subscription>();
    public ICollection<DatabaseQueryHistory> QueryHistory { get; set; } = new List<DatabaseQueryHistory>();
}
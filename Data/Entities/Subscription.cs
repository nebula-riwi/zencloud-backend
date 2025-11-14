namespace ZenCloud.Data.Entities;

public class Subscription
{
    public Guid SubscriptionId { get; set; }
    public Guid UserId { get; set; }
    public int PlanId { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public bool IsActive { get; set; } = true;
    public string? MercadoPagoSubscriptionId { get; set; }
    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Pending;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public bool AutoRenewEnabled { get; set; }
    public DateTime? LastAutoRenewAttemptAt { get; set; }
    public string? LastAutoRenewError { get; set; }
    public DateTime? LastExpirationReminderSentAt { get; set; }
    public int ExpirationReminderCount { get; set; }

    // Navigation properties
    public User User { get; set; } = null!;
    public Plan Plan { get; set; } = null!;
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
}
namespace ZenCloud.Data.Entities;

public class Payment
{
    public Guid PaymentId { get; set; }
    public Guid UserId { get; set; }
    public Guid? SubscriptionId { get; set; }
    public string? MercadoPagoPaymentId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "COP";
    public PaymentStatusType PaymentStatus { get; set; } = PaymentStatusType.Pending;
    public string? PaymentMethod { get; set; }
    public string? PaymentMethodId { get; set; }
    public string? PayerId { get; set; }
    public string? CardId { get; set; }
    public DateTime TransactionDate { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public User User { get; set; } = null!;
    public Subscription? Subscription { get; set; } = null!;
}
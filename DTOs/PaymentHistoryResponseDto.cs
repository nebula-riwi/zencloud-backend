namespace ZenCloud.DTOs;

public class PaymentHistoryResponseDto
{
    public Guid PaymentId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "COP";
    public string Status { get; set; } = null!;
    public string? PaymentMethod { get; set; }
    public DateTime TransactionDate { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? MercadoPagoPaymentId { get; set; }
    public string? PlanName { get; set; }
    public Guid? SubscriptionId { get; set; }
}


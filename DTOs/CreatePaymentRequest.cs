namespace ZenCloud.DTOs;

public class CreatePaymentRequest
{
    public Guid UserId { get; set; }
    public decimal Amount { get; set; }
    public string PaymentType { get; set; } = "subscription"; // Ej: "subscription" o "plan"
}
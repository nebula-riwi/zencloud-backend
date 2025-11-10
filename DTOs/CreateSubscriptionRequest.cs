namespace ZenCloud.DTOs;

public class CreateSubscriptionRequest
{
    public Guid UserId { get; set; }
    public int PlanId { get; set; }
}
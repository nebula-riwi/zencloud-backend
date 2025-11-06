namespace ZenCloud.Data.Entities;

public class Plan
{
    public int PlanId { get; set; }
    public PlanType PlanName { get; set; }
    public int MaxDatabasesPerEngine { get; set; }
    public decimal PriceInCOP { get; set; }
    public int DurationInDays { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Description { get; set; }

    // Navigation properties
    public ICollection<Subscription> Subscriptions { get; set; } = new List<Subscription>();
}
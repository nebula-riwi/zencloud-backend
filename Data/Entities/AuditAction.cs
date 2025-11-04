namespace ZenCloud.Data.Entities;

public enum AuditAction
{
    // User actions
    UserCreated = 1,
    UserUpdated = 2,
    UserDeleted = 3,
    UserLogin = 4,
    UserLogout = 5,
    PasswordChanged = 6,
    EmailVerified = 7,

    // Database actions
    DatabaseCreated = 10,
    DatabaseUpdated = 11,
    DatabaseDeleted = 12,
    DatabaseStatusChanged = 13,

    // Subscription actions
    SubscriptionCreated = 20,
    SubscriptionUpdated = 21,
    SubscriptionCancelled = 22,
    PlanChanged = 23,

    // Payment actions
    PaymentCreated = 30,
    PaymentApproved = 31,
    PaymentFailed = 32,

    // Webhook actions
    WebhookCreated = 40,
    WebhookUpdated = 41,
    WebhookDeleted = 42,

    // System actions
    SystemConfigChanged = 50

}
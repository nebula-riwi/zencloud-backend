namespace ZenCloud.Data.Entities;

public enum WebhookEventType
{
    AccountCreated = 1,
    AccountUpdated = 2,
    DatabaseCreated = 3,
    DatabaseDeleted = 4,
    DatabaseStatusChanged = 5,
    SubscriptionCreated = 6,
    SubscriptionExpired = 7,
    PaymentReceived = 8,
    PaymentFailed = 9,
    UserLogin = 10,
    UserLogout = 11
}
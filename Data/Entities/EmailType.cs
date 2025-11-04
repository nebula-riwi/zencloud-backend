namespace ZenCloud.Data.Entities;

public enum EmailType
{
    Registration = 1,
    EmailVerification = 2,
    PasswordReset = 3,
    DatabaseCreated = 4,
    DatabaseDeleted = 5,
    SubscriptionCreated = 6,
    SubscriptionExpiring = 7,
    SubscriptionExpired = 8,
    PaymentReceived = 9,
    PaymentFailed = 10
}
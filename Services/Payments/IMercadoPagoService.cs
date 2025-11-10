using System.Text.Json;

namespace ZenCloud.Services;

public interface IMercadoPagoService
{
    Task<string> CreateSubscriptionPreferenceAsync(Guid userId, int planId, string successUrl, string failureUrl, string notificationUrl);
    Task<string> CreateRecurringSubscriptionAsync(Guid userId, int planId, string payerEmail, string cardToken);
    Task ProcessWebhookAsync(JsonElement data);
}
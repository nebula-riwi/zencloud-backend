using System.Text;
using System.Text.Json;
using ZenCloud.Data.Entities;
using ZenCloud.Data.Repositories.Interfaces;
using ZenCloud.Services.Interfaces;

namespace ZenCloud.Services.Implementations;

public class WebhookService : IWebhookService
{
    private readonly IWebhookRepository _webhookRepository;
    private readonly IRepository<WebhookLog> _webhookLogRepository;
    private readonly HttpClient _httpClient;
    private readonly ILogger<WebhookService> _logger;

    public WebhookService(
        IWebhookRepository webhookRepository,
        IRepository<WebhookLog> webhookLogRepository,
        IHttpClientFactory httpClientFactory,
        ILogger<WebhookService> logger)
    {
        _webhookRepository = webhookRepository;
        _webhookLogRepository = webhookLogRepository;
        _httpClient = httpClientFactory.CreateClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
        _logger = logger;
    }

    public async Task<WebhookConfiguration> CreateWebhookAsync(Guid userId, string name, string webhookUrl, WebhookEventType eventType)
    {
        var secretToken = GenerateSecretToken();
        
        var webhook = new WebhookConfiguration
        {
            WebhookId = Guid.NewGuid(),
            UserId = userId,
            Name = name,
            WebhookUrl = webhookUrl,
            EventType = eventType,
            IsActive = true,
            SecretToken = secretToken,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _webhookRepository.CreateAsync(webhook);
        return webhook;
    }

    public async Task<WebhookConfiguration?> GetWebhookByIdAsync(Guid webhookId)
    {
        return await _webhookRepository.GetByIdAsync(webhookId);
    }

    public async Task<IEnumerable<WebhookConfiguration>> GetUserWebhooksAsync(Guid userId)
    {
        return await _webhookRepository.GetByUserIdAsync(userId);
    }

    public async Task<WebhookConfiguration> UpdateWebhookAsync(Guid webhookId, Guid userId, string? webhookUrl = null, WebhookEventType? eventType = null, bool? isActive = null, string? name = null)
    {
        var webhook = await _webhookRepository.GetByIdAsync(webhookId);
        if (webhook == null)
        {
            throw new KeyNotFoundException("Webhook no encontrado");
        }

        if (webhook.UserId != userId)
        {
            throw new UnauthorizedAccessException("No tienes permisos para modificar este webhook");
        }

        if (name != null) webhook.Name = name;
        if (webhookUrl != null) webhook.WebhookUrl = webhookUrl;
        if (eventType.HasValue) webhook.EventType = eventType.Value;
        if (isActive.HasValue) webhook.IsActive = isActive.Value;
        webhook.UpdatedAt = DateTime.UtcNow;

        await _webhookRepository.UpdateAsync(webhook);
        return webhook;
    }

    public async Task<bool> DeleteWebhookAsync(Guid webhookId, Guid userId)
    {
        var webhook = await _webhookRepository.GetByIdAsync(webhookId);
        if (webhook == null)
        {
            return false;
        }

        if (webhook.UserId != userId)
        {
            throw new UnauthorizedAccessException("No tienes permisos para eliminar este webhook");
        }

        await _webhookRepository.DeleteAsync(webhook);
        return true;
    }

    public async Task TriggerWebhookAsync(WebhookEventType eventType, object payload, Guid? userId = null)
    {
        var webhooks = userId.HasValue
            ? await _webhookRepository.GetActiveByUserIdAsync(userId.Value)
            : (await _webhookRepository.GetByEventTypeAsync(eventType)).ToList();

        if (!webhooks.Any())
        {
            return;
        }

        var payloadJson = JsonSerializer.Serialize(payload);
        var tasks = webhooks.Select(webhook => SendWebhookAsync(webhook, eventType, payloadJson));
        await Task.WhenAll(tasks);
    }

    private async Task SendWebhookAsync(WebhookConfiguration webhook, WebhookEventType eventType, string payloadJson)
    {
        var log = new WebhookLog
        {
            WebhookLogId = Guid.NewGuid(),
            WebhookId = webhook.WebhookId,
            EventType = eventType,
            PayloadJson = payloadJson,
            Status = WebhookLogStatus.Pending,
            AttemptCount = 1,
            CreatedAt = DateTime.UtcNow
        };

        try
        {
            var content = new StringContent(payloadJson, Encoding.UTF8, "application/json");
            content.Headers.Add("X-Webhook-Event", eventType.ToString());
            content.Headers.Add("X-Webhook-Signature", GenerateSignature(payloadJson, webhook.SecretToken));

            var response = await _httpClient.PostAsync(webhook.WebhookUrl, content);
            var responseBody = await response.Content.ReadAsStringAsync();

            log.ResponseStatusCode = (int)response.StatusCode;
            log.ResponseBody = responseBody;
            log.Status = response.IsSuccessStatusCode ? WebhookLogStatus.Success : WebhookLogStatus.Failed;
            log.SentAt = DateTime.UtcNow;

            _logger.LogInformation("Webhook enviado: {WebhookUrl} - Status: {StatusCode}", webhook.WebhookUrl, response.StatusCode);
        }
        catch (Exception ex)
        {
            log.Status = WebhookLogStatus.Failed;
            log.ResponseBody = ex.Message;
            _logger.LogError(ex, "Error enviando webhook: {WebhookUrl}", webhook.WebhookUrl);
        }
        finally
        {
            await _webhookLogRepository.CreateAsync(log);
        }
    }

    private string GenerateSecretToken()
    {
        return Convert.ToBase64String(Guid.NewGuid().ToByteArray()).Replace("=", "").Replace("+", "").Replace("/", "");
    }

    private string GenerateSignature(string payload, string secret)
    {
        using var hmac = new System.Security.Cryptography.HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToBase64String(hash);
    }
}


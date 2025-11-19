using System.Text;
using System.Text.Json;
using ZenCloud.Data.Entities;
using ZenCloud.Data.Repositories.Interfaces;
using ZenCloud.Services.Interfaces;

namespace ZenCloud.Services.Implementations;

public class WebhookService : IWebhookService, IDisposable
{
    private readonly IWebhookRepository _webhookRepository;
    private readonly IRepository<WebhookLog> _webhookLogRepository;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<WebhookService> _logger;
    private bool _disposed = false;

    public WebhookService(
        IWebhookRepository webhookRepository,
        IRepository<WebhookLog> webhookLogRepository,
        IHttpClientFactory httpClientFactory,
        ILogger<WebhookService> logger)
    {
        _webhookRepository = webhookRepository;
        _webhookLogRepository = webhookLogRepository;
        _httpClientFactory = httpClientFactory;
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
        
        // Ejecutar webhooks secuencialmente para evitar conflictos de DbContext
        foreach (var webhook in webhooks)
        {
            // Fire and forget - no esperar respuesta para no bloquear
            _ = Task.Run(async () =>
            {
                try
                {
                    await SendWebhookAsync(webhook, eventType, payloadJson);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error enviando webhook {WebhookId}", webhook.WebhookId);
                }
            });
        }
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

        // Usar HttpClientFactory para crear clientes que se liberen automáticamente
        using var httpClient = _httpClientFactory.CreateClient();
        httpClient.Timeout = TimeSpan.FromSeconds(30);

        try
        {
            // Detectar si es Discord y convertir el payload
            var isDiscord = webhook.WebhookUrl.Contains("discord.com", StringComparison.OrdinalIgnoreCase);
            var finalPayload = isDiscord ? ConvertToDiscordFormat(eventType, payloadJson) : payloadJson;
            
            var content = new StringContent(finalPayload, Encoding.UTF8, "application/json");
            if (!isDiscord)
            {
                content.Headers.Add("X-Webhook-Event", eventType.ToString());
                content.Headers.Add("X-Webhook-Signature", GenerateSignature(payloadJson, webhook.SecretToken));
            }

            var response = await httpClient.PostAsync(webhook.WebhookUrl, content);
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

    private string ConvertToDiscordFormat(WebhookEventType eventType, string payloadJson)
    {
        var payload = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(payloadJson);
        if (payload == null) return payloadJson;

        // Mapeo de colores por tipo de evento
        var eventColors = new Dictionary<WebhookEventType, int>
        {
            { WebhookEventType.DatabaseCreated, 0x43a047 }, // Verde
            { WebhookEventType.DatabaseDeleted, 0xe53935 }, // Rojo
            { WebhookEventType.DatabaseStatusChanged, 0xfb8c00 }, // Naranja
            { WebhookEventType.UserLogin, 0x1e88e5 }, // Azul
            { WebhookEventType.UserLogout, 0x757575 }, // Gris
            { WebhookEventType.AccountUpdated, 0x8e24aa }, // Morado
            { WebhookEventType.SubscriptionCreated, 0x00acc1 }, // Cyan
            { WebhookEventType.SubscriptionExpired, 0xff6f00 }, // Naranja oscuro
            { WebhookEventType.PaymentReceived, 0x43a047 }, // Verde
            { WebhookEventType.PaymentFailed, 0xe53935 }, // Rojo
            { WebhookEventType.PaymentRejected, 0xc62828 }, // Rojo oscuro
        };

        // Obtener color para el evento
        var color = eventColors.ContainsKey(eventType) ? eventColors[eventType] : 0xe78a53;

        // Obtener título y descripción según el evento
        var (title, description) = GetEventTitleAndDescription(eventType, payload);

        // Construir los fields del embed
        var fields = new List<object>();
        foreach (var kvp in payload)
        {
            if (kvp.Key == "timestamp" || kvp.Key.EndsWith("At")) continue; // Skip timestamps
            
            var value = kvp.Value.ValueKind == JsonValueKind.String 
                ? kvp.Value.GetString() 
                : kvp.Value.ToString();
            
            if (!string.IsNullOrEmpty(value) && value.Length < 1000)
            {
                fields.Add(new
                {
                    name = FormatFieldName(kvp.Key),
                    value = $"`{value}`",
                    inline = true
                });
            }
        }

        var discordPayload = new
        {
            embeds = new[]
            {
                new
                {
                    title = $"🔔 {title}",
                    description = description,
                    color = color,
                    fields = fields.Take(10).ToArray(), // Máximo 10 campos
                    footer = new
                    {
                        text = "ZenCloud · Database Platform",
                        icon_url = "https://nebula.andrescortes.dev/logo.png"
                    },
                    timestamp = DateTime.UtcNow.ToString("o")
                }
            }
        };

        return JsonSerializer.Serialize(discordPayload);
    }

    private (string title, string description) GetEventTitleAndDescription(WebhookEventType eventType, Dictionary<string, JsonElement> payload)
    {
        return eventType switch
        {
            WebhookEventType.DatabaseCreated => 
                ("Base de Datos Creada", $"Se ha creado una nueva base de datos **{GetValue(payload, "databaseName")}** ({GetValue(payload, "engine")})"),
            
            WebhookEventType.DatabaseDeleted => 
                ("Base de Datos Eliminada", $"La base de datos **{GetValue(payload, "databaseName")}** ha sido eliminada"),
            
            WebhookEventType.DatabaseStatusChanged => 
                ("Estado de Base de Datos Actualizado", $"**{GetValue(payload, "databaseName")}** cambió de {GetValue(payload, "previousStatus")} a **{GetValue(payload, "newStatus")}**"),
            
            WebhookEventType.UserLogin => 
                ("Inicio de Sesión", $"Usuario **{GetValue(payload, "email")}** ha iniciado sesión"),
            
            WebhookEventType.UserLogout => 
                ("Cierre de Sesión", "Un usuario ha cerrado sesión correctamente"),
            
            WebhookEventType.AccountUpdated => 
                ("Perfil Actualizado", $"El perfil del usuario ha sido actualizado. Nuevo nombre: **{GetValue(payload, "newFullName")}**"),
            
            WebhookEventType.SubscriptionCreated => 
                ("Nueva Suscripción", $"Suscripción al plan **{GetValue(payload, "planName")}** creada exitosamente"),
            
            WebhookEventType.SubscriptionExpired => 
                ("Suscripción Expirada", $"La suscripción al plan **{GetValue(payload, "planName")}** ha expirado"),
            
            WebhookEventType.PaymentReceived => 
                ("Pago Recibido", $"Pago de **${GetValue(payload, "amount")} {GetValue(payload, "currency")}** recibido exitosamente"),
            
            WebhookEventType.PaymentFailed => 
                ("Pago Fallido", "El intento de pago automático ha fallado"),
            
            WebhookEventType.PaymentRejected => 
                ("Pago Rechazado", $"El pago de **${GetValue(payload, "amount")}** ha sido rechazado"),
            
            _ => ("Evento de ZenCloud", "Se ha generado un nuevo evento")
        };
    }

    private string GetValue(Dictionary<string, JsonElement> payload, string key)
    {
        if (payload.TryGetValue(key, out var value))
        {
            return value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.ToString();
        }
        return "";
    }

    private string FormatFieldName(string fieldName)
    {
        // Convertir camelCase a Title Case con espacios
        var result = System.Text.RegularExpressions.Regex.Replace(fieldName, "([a-z])([A-Z])", "$1 $2");
        return char.ToUpper(result[0]) + result.Substring(1);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            // HttpClientFactory maneja la liberación de recursos automáticamente
            // No necesitamos liberar nada aquí ya que ahora usamos CreateClient() en cada método
        }
    }
}


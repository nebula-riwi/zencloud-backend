using System.Text;
using System.Text.Json;
using ZenCloud.Data.Entities;
using ZenCloud.Data.Repositories.Interfaces;
using ZenCloud.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace ZenCloud.Services.Implementations;

public class WebhookService : IWebhookService, IDisposable
{
    private readonly IWebhookRepository _webhookRepository;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<WebhookService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private bool _disposed = false;

    public WebhookService(
        IWebhookRepository webhookRepository,
        IHttpClientFactory httpClientFactory,
        ILogger<WebhookService> logger,
        IServiceScopeFactory scopeFactory)
    {
        _webhookRepository = webhookRepository;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _scopeFactory = scopeFactory;
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
        try
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
            _logger.LogInformation("Webhook {WebhookId} eliminado correctamente", webhookId);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error eliminando webhook {WebhookId}", webhookId);
            throw;
        }
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
        
        // Ejecutar webhooks en background sin bloquear
        foreach (var webhook in webhooks)
        {
            var webhookId = webhook.WebhookId;
            var webhookUrl = webhook.WebhookUrl;
            var secretToken = webhook.SecretToken;
            
            // Fire and forget con scope propio
            _ = Task.Run(async () =>
            {
                using var scope = _scopeFactory.CreateScope();
                var logRepository = scope.ServiceProvider.GetRequiredService<IRepository<WebhookLog>>();
                
                try
                {
                    await SendWebhookAsync(webhookId, webhookUrl, secretToken, eventType, payloadJson, logRepository);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error enviando webhook {WebhookId}", webhookId);
                }
            });
        }
    }

    private async Task SendWebhookAsync(Guid webhookId, string webhookUrl, string secretToken, WebhookEventType eventType, string payloadJson, IRepository<WebhookLog> logRepository)
    {
        var log = new WebhookLog
        {
            WebhookLogId = Guid.NewGuid(),
            WebhookId = webhookId,
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
            var isDiscord = webhookUrl.Contains("discord.com", StringComparison.OrdinalIgnoreCase);
            var finalPayload = isDiscord ? ConvertToDiscordFormat(eventType, payloadJson) : payloadJson;
            
            var content = new StringContent(finalPayload, Encoding.UTF8, "application/json");
            if (!isDiscord)
            {
                content.Headers.Add("X-Webhook-Event", eventType.ToString());
                content.Headers.Add("X-Webhook-Signature", GenerateSignature(payloadJson, secretToken));
            }

            var response = await httpClient.PostAsync(webhookUrl, content);
            var responseBody = await response.Content.ReadAsStringAsync();

            log.ResponseStatusCode = (int)response.StatusCode;
            log.ResponseBody = responseBody;
            log.Status = response.IsSuccessStatusCode ? WebhookLogStatus.Success : WebhookLogStatus.Failed;
            log.SentAt = DateTime.UtcNow;

            _logger.LogInformation("Webhook enviado: {WebhookUrl} - Status: {StatusCode}", webhookUrl, response.StatusCode);
        }
        catch (Exception ex)
        {
            log.Status = WebhookLogStatus.Failed;
            log.ResponseBody = ex.Message;
            _logger.LogError(ex, "Error enviando webhook: {WebhookUrl}", webhookUrl);
        }
        finally
        {
            await logRepository.CreateAsync(log);
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

        // Construir los fields del embed (solo los más importantes)
        var fields = GetRelevantFields(eventType, payload);

        var discordPayload = new
        {
            embeds = new[]
            {
                new
                {
                    author = new
                    {
                        name = "ZenCloud Database Platform",
                        icon_url = "https://nebula.andrescortes.dev/favicon.svg"
                    },
                    title = GetEventEmoji(eventType) + " " + title,
                    description = description,
                    color = color,
                    fields = fields.ToArray(),
                    thumbnail = new
                    {
                        url = "https://nebula.andrescortes.dev/favicon.svg"
                    },
                    footer = new
                    {
                        text = "ZenCloud"
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

    private string GetEventEmoji(WebhookEventType eventType)
    {
        return eventType switch
        {
            WebhookEventType.DatabaseCreated => "✅",
            WebhookEventType.DatabaseDeleted => "🗑️",
            WebhookEventType.DatabaseStatusChanged => "⚠️",
            WebhookEventType.UserLogin => "🔐",
            WebhookEventType.UserLogout => "👋",
            WebhookEventType.AccountUpdated => "👤",
            WebhookEventType.SubscriptionCreated => "💎",
            WebhookEventType.SubscriptionExpired => "⏰",
            WebhookEventType.PaymentReceived => "💰",
            WebhookEventType.PaymentFailed => "❌",
            WebhookEventType.PaymentRejected => "🚫",
            _ => "📢"
        };
    }

    private List<object> GetRelevantFields(WebhookEventType eventType, Dictionary<string, JsonElement> payload)
    {
        var fields = new List<object>();

        // Campos específicos por tipo de evento
        switch (eventType)
        {
            case WebhookEventType.DatabaseCreated:
            case WebhookEventType.DatabaseDeleted:
                AddField(fields, "Database Name", GetValue(payload, "databaseName"));
                AddField(fields, "Engine", GetValue(payload, "engine"));
                break;

            case WebhookEventType.DatabaseStatusChanged:
                AddField(fields, "Database Name", GetValue(payload, "databaseName"));
                AddField(fields, "New Status", GetValue(payload, "newStatus"));
                AddField(fields, "Previous Status", GetValue(payload, "previousStatus"));
                break;

            case WebhookEventType.UserLogin:
                AddField(fields, "Email", GetValue(payload, "email"));
                break;

            case WebhookEventType.AccountUpdated:
                AddField(fields, "New Name", GetValue(payload, "newFullName"));
                break;

            case WebhookEventType.SubscriptionCreated:
            case WebhookEventType.SubscriptionExpired:
                AddField(fields, "Plan", GetValue(payload, "planName"));
                AddField(fields, "Price", "$" + GetValue(payload, "price"));
                break;

            case WebhookEventType.PaymentReceived:
            case WebhookEventType.PaymentRejected:
                AddField(fields, "Amount", "$" + GetValue(payload, "amount") + " " + GetValue(payload, "currency"));
                AddField(fields, "Plan", GetValue(payload, "planName"));
                break;

            case WebhookEventType.PaymentFailed:
                AddField(fields, "Plan", GetValue(payload, "planName"));
                break;
        }

        return fields;
    }

    private void AddField(List<object> fields, string name, string value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            fields.Add(new
            {
                name = name,
                value = $"**{value}**",
                inline = true
            });
        }
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


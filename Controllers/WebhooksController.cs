using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.IdentityModel.Tokens.Jwt;
using ZenCloud.DTOs;
using ZenCloud.Services.Interfaces;
using ZenCloud.Exceptions;
using Swashbuckle.AspNetCore.Annotations;

namespace ZenCloud.Controllers;

/// <summary>
/// Controlador para gestión de webhooks
/// </summary>
[Authorize]
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
[SwaggerTag("Endpoints para crear, listar, actualizar y eliminar webhooks")]
public class WebhooksController : ControllerBase
{
    private readonly IWebhookService _webhookService;
    private readonly ILogger<WebhooksController> _logger;

    public WebhooksController(IWebhookService webhookService, ILogger<WebhooksController> logger)
    {
        _webhookService = webhookService;
        _logger = logger;
    }

    private Guid GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst("userId")?.Value ?? User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
        {
            throw new UnauthorizedException("Usuario no autenticado");
        }
        return userId;
    }

    /// <summary>
    /// Obtiene todos los webhooks del usuario actual
    /// </summary>
    /// <returns>Lista de webhooks</returns>
    [HttpGet]
    [SwaggerOperation(Summary = "Obtener webhooks del usuario", Description = "Retorna todos los webhooks configurados por el usuario autenticado")]
    [SwaggerResponse(200, "Lista de webhooks")]
    [SwaggerResponse(401, "No autenticado")]
    public async Task<IActionResult> GetWebhooks()
    {
        var userId = GetCurrentUserId();
        var webhooks = await _webhookService.GetUserWebhooksAsync(userId);
        
        var response = webhooks.Select(w => new WebhookResponse
        {
            WebhookId = w.WebhookId,
            Name = w.Name,
            WebhookUrl = w.WebhookUrl,
            EventType = w.EventType.ToString(),
            IsActive = w.IsActive,
            CreatedAt = w.CreatedAt,
            UpdatedAt = w.UpdatedAt
        });

        return Ok(new { data = response });
    }

    /// <summary>
    /// Crea un nuevo webhook
    /// </summary>
    /// <param name="request">Datos del webhook</param>
    /// <returns>Webhook creado</returns>
    [HttpPost]
    [SwaggerOperation(Summary = "Crear webhook", Description = "Crea un nuevo webhook para recibir notificaciones de eventos")]
    [SwaggerResponse(200, "Webhook creado exitosamente")]
    [SwaggerResponse(400, "Solicitud inválida")]
    [SwaggerResponse(401, "No autenticado")]
    [SwaggerResponse(422, "Errores de validación")]
    public async Task<IActionResult> CreateWebhook([FromBody] CreateWebhookRequest request)
    {
        try
        {
            if (request == null)
            {
                throw new BadRequestException("La solicitud no puede estar vacía");
            }

            if (string.IsNullOrWhiteSpace(request.Name))
            {
                throw new BadRequestException("El nombre del webhook es obligatorio");
            }

            if (string.IsNullOrWhiteSpace(request.Url))
            {
                throw new BadRequestException("La URL del webhook es obligatoria");
            }

            if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                throw new BadRequestException("La URL del webhook no es válida");
            }

            var userId = GetCurrentUserId();
            var webhook = await _webhookService.CreateWebhookAsync(userId, request.Name, request.Url, request.EventType);
            
            var response = new WebhookResponse
            {
                WebhookId = webhook.WebhookId,
                Name = webhook.Name,
                WebhookUrl = webhook.WebhookUrl,
                EventType = webhook.EventType.ToString(),
                IsActive = webhook.IsActive,
                CreatedAt = webhook.CreatedAt,
                UpdatedAt = webhook.UpdatedAt
            };

            return Ok(new { message = "Webhook creado exitosamente", data = response });
        }
        catch (BadRequestException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (UnauthorizedException ex)
        {
            return Unauthorized(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creando webhook");
            return StatusCode(500, new { message = "Error al crear el webhook: " + ex.Message });
        }
    }

    /// <summary>
    /// Actualiza un webhook existente
    /// </summary>
    /// <param name="id">ID del webhook</param>
    /// <param name="request">Datos a actualizar</param>
    /// <returns>Webhook actualizado</returns>
    [HttpPut("{id}")]
    [SwaggerOperation(Summary = "Actualizar webhook", Description = "Actualiza la configuración de un webhook existente")]
    [SwaggerResponse(200, "Webhook actualizado exitosamente")]
    [SwaggerResponse(400, "Solicitud inválida")]
    [SwaggerResponse(401, "No autenticado")]
    [SwaggerResponse(404, "Webhook no encontrado")]
    public async Task<IActionResult> UpdateWebhook(Guid id, [FromBody] UpdateWebhookRequest request)
    {
        var userId = GetCurrentUserId();
        var webhook = await _webhookService.UpdateWebhookAsync(id, userId, request.Url, request.EventType, request.Active, request.Name);
        
        var response = new WebhookResponse
        {
            WebhookId = webhook.WebhookId,
            Name = webhook.Name,
            WebhookUrl = webhook.WebhookUrl,
            EventType = webhook.EventType.ToString(),
            IsActive = webhook.IsActive,
            CreatedAt = webhook.CreatedAt,
            UpdatedAt = webhook.UpdatedAt
        };

        return Ok(new { message = "Webhook actualizado exitosamente", data = response });
    }

    /// <summary>
    /// Elimina un webhook
    /// </summary>
    /// <param name="id">ID del webhook</param>
    /// <returns>Confirmación de eliminación</returns>
    [HttpDelete("{id}")]
    [SwaggerOperation(Summary = "Eliminar webhook", Description = "Elimina un webhook del usuario")]
    [SwaggerResponse(200, "Webhook eliminado exitosamente")]
    [SwaggerResponse(401, "No autenticado")]
    [SwaggerResponse(404, "Webhook no encontrado")]
    public async Task<IActionResult> DeleteWebhook(Guid id)
    {
        var userId = GetCurrentUserId();
        var deleted = await _webhookService.DeleteWebhookAsync(id, userId);
        
        if (!deleted)
        {
            throw new NotFoundException("Webhook no encontrado");
        }

        return Ok(new { message = "Webhook eliminado exitosamente" });
    }

    /// <summary>
    /// Prueba un webhook enviando un evento de prueba
    /// </summary>
    /// <param name="id">ID del webhook</param>
    /// <returns>Resultado de la prueba</returns>
    [HttpPost("{id}/test")]
    [SwaggerOperation(Summary = "Probar webhook", Description = "Envía un evento de prueba al webhook")]
    [SwaggerResponse(200, "Prueba enviada exitosamente")]
    [SwaggerResponse(401, "No autenticado")]
    [SwaggerResponse(404, "Webhook no encontrado")]
    public async Task<IActionResult> TestWebhook(Guid id)
    {
        var userId = GetCurrentUserId();
        var webhook = await _webhookService.GetWebhookByIdAsync(id);
        
        if (webhook == null || webhook.UserId != userId)
        {
            throw new NotFoundException("Webhook no encontrado");
        }

        var testPayload = new
        {
            @event = "test",
            timestamp = DateTime.UtcNow,
            message = "Esta es una prueba del webhook"
        };

        await _webhookService.TriggerWebhookAsync(webhook.EventType, testPayload, userId);

        return Ok(new { message = "Prueba de webhook enviada exitosamente" });
    }
}

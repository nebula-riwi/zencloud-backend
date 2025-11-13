using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using System.IdentityModel.Tokens.Jwt;
using ZenCloud.Data.Repositories.Interfaces;
using ZenCloud.DTOs;
using ZenCloud.Services;

namespace ZenCloud.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PaymentsController : ControllerBase
{
    private readonly MercadoPagoService _mpService;
    private readonly IPlanRepository _planRepository;
    private readonly ISubscriptionRepository _subscriptionRepository;

    public PaymentsController(MercadoPagoService mpService, IPlanRepository planRepository, ISubscriptionRepository subscriptionRepository)
    {
        _mpService = mpService;
        _planRepository = planRepository;
        _subscriptionRepository = subscriptionRepository;
    }

    // Endpoint para crear pagos de suscripción
    [HttpPost("create")]
    [Authorize]
    public async Task<IActionResult> Create([FromBody] CreateSubscriptionRequest request)
    {
        try
        {
        if (request == null || request.PlanId <= 0)
                return BadRequest(new { message = "PlanId inválido." });

            // Obtener userId del token si no viene en el request
            Guid userId;
            if (request.UserId == Guid.Empty)
            {
                var userIdClaim = User.FindFirst("userId")?.Value ?? 
                                 User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value;
                if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out userId))
                {
                    return Unauthorized(new { message = "Usuario no autenticado" });
                }
            }
            else
            {
                userId = request.UserId;
            }

            // Verificar que el plan existe
            var plan = await _planRepository.GetByIdAsync(request.PlanId);
            if (plan == null)
                return BadRequest(new { message = "Plan no encontrado." });

            var paymentUrl = await _mpService.CreateSubscriptionPreferenceAsync(
                userId,
                request.PlanId,
                successUrl: "https://nebula.andrescortes.dev/payment/success",
                failureUrl: "https://nebula.andrescortes.dev/payment/failure", 
                notificationUrl: "https://service.nebula.andrescortes.dev/api/Payments/webhook"
            );

            return Ok(new { 
                payment_url = paymentUrl,
                plan_name = plan.PlanName.ToString(),
                amount = plan.PriceInCOP
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error creando pago: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
            return StatusCode(500, new { message = "Error al crear el pago: " + ex.Message });
        }
    }

    // Obtener planes disponibles
    [HttpGet("plans")]
    public async Task<IActionResult> GetPlans()
    {
        try
        {
            var plans = await _planRepository.GetAllAsync();
            var activePlans = plans.Where(p => p.IsActive).ToList();
            
            return Ok(activePlans.Select(p => new
            {
                p.PlanId,
                PlanName = p.PlanName.ToString(),
                p.MaxDatabasesPerEngine,
                p.PriceInCOP,
                p.DurationInDays,
                p.Description
            }));
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    // Obtener suscripción actual del usuario
    [HttpGet("current-subscription")]
    [Authorize]
    public async Task<IActionResult> GetCurrentSubscription()
    {
        try
        {
            var userIdClaim = User.FindFirst("userId")?.Value ?? 
                             User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            {
                return Unauthorized(new { message = "Usuario no autenticado" });
            }

            var subscription = await _subscriptionRepository.GetActiveByUserIdAsync(userId);
            
            if (subscription == null)
            {
                // Si no hay suscripción activa, devolver el plan gratuito
                var freePlan = await _planRepository.GetAllAsync();
                var defaultPlan = freePlan.FirstOrDefault(p => p.PlanName.ToString().ToLower() == "free");
                
                if (defaultPlan != null)
                {
                    return Ok(new
                    {
                        planId = defaultPlan.PlanId,
                        planName = defaultPlan.PlanName.ToString(),
                        maxDatabasesPerEngine = defaultPlan.MaxDatabasesPerEngine,
                        priceInCOP = defaultPlan.PriceInCOP,
                        durationInDays = defaultPlan.DurationInDays,
                        description = defaultPlan.Description,
                        isActive = true
                    });
                }
                
                return Ok(new { message = "No hay suscripción activa" });
            }

            return Ok(new
            {
                planId = subscription.Plan.PlanId,
                planName = subscription.Plan.PlanName.ToString(),
                maxDatabasesPerEngine = subscription.Plan.MaxDatabasesPerEngine,
                priceInCOP = subscription.Plan.PriceInCOP,
                durationInDays = subscription.Plan.DurationInDays,
                description = subscription.Plan.Description,
                isActive = subscription.IsActive,
                startDate = subscription.StartDate,
                endDate = subscription.EndDate,
                paymentStatus = subscription.PaymentStatus.ToString()
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error obteniendo suscripción actual: {ex.Message}");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    // Webhook para procesar pagos
    [AllowAnonymous]
    [HttpPost("webhook")]
    public async Task<IActionResult> Webhook()
    {
        try
        {
            // Leer el body del request
            using var reader = new StreamReader(HttpContext.Request.Body);
            var body = await reader.ReadToEndAsync();
            
            Console.WriteLine("Webhook recibido:");
            Console.WriteLine(body);

            var data = JsonDocument.Parse(body).RootElement;
            await _mpService.ProcessWebhookAsync(data);
            
            return Ok(new { message = "Webhook procesado correctamente" });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error procesando webhook: {ex.Message}");
            return StatusCode(500, new { error = ex.Message });
        }
    }
}
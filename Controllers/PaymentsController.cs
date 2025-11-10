using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
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

    public PaymentsController(MercadoPagoService mpService, IPlanRepository planRepository)
    {
        _mpService = mpService;
        _planRepository = planRepository;
    }

    // ✅ ÚNICO endpoint para crear pagos de suscripción
    [HttpPost("create")]
    public async Task<IActionResult> Create([FromBody] CreateSubscriptionRequest request)
    {
        if (request == null || request.PlanId <= 0)
            return BadRequest("PlanId inválido.");

        try
        {
            // Verificar que el plan existe
            var plan = await _planRepository.GetByIdAsync(request.PlanId);
            if (plan == null)
                return BadRequest("Plan no encontrado.");

            var paymentUrl = await _mpService.CreateSubscriptionPreferenceAsync(
                request.UserId,
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
            Console.WriteLine($"❌ Error creando pago: {ex.Message}");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    // ✅ Obtener planes disponibles
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

    // ✅ Webhook para procesar pagos
    [AllowAnonymous]
    [HttpPost("webhook")]
    public async Task<IActionResult> Webhook()
    {
        try
        {
            // Leer el body del request
            using var reader = new StreamReader(HttpContext.Request.Body);
            var body = await reader.ReadToEndAsync();
            
            Console.WriteLine("📬 Webhook recibido:");
            Console.WriteLine(body);

            var data = JsonDocument.Parse(body).RootElement;
            await _mpService.ProcessWebhookAsync(data);
            
            return Ok(new { message = "Webhook procesado correctamente ✅" });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error procesando webhook: {ex.Message}");
            return StatusCode(500, new { error = ex.Message });
        }
    }
}
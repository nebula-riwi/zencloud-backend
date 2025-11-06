using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using ZenCloud.DTOs;
using ZenCloud.Services;

namespace ZenCloud.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PaymentsController : ControllerBase
    {
        private readonly MercadoPagoService _mpService;

        public PaymentsController(MercadoPagoService mpService)
        {
            _mpService = mpService;
        }

        // ✅ Crear preferencia de pago
        [HttpPost("create")]
        public async Task<IActionResult> Create([FromBody] CreatePaymentRequest request)
        {
            if (request == null || request.Amount <= 0)
                return BadRequest("El monto debe ser mayor a 0.");

            var result = await _mpService.CrearPreferenciaAsync(
                request.UserId,
                request.Amount,
                request.PaymentType,
                successUrl: "https://nebula.andrescortes.dev/success",
                failureUrl: "https://nebula.andrescortes.dev/failure",
                notificationUrl: "http://localhost:5089/api/payments/webhook"
            );

            return Content(result, "application/json");
        }

        // ✅ Webhook para procesar pagos
        [AllowAnonymous]
        [HttpPost("webhook")]
        public async Task<IActionResult> Webhook([FromBody] JsonElement data)
        {
            Console.WriteLine("📬 Webhook recibido:");
            Console.WriteLine(data.ToString());
        
            await _mpService.ProcesarWebhookAsync(data);
            return Ok();
        }
    }
}

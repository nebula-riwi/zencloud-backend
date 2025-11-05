using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using ZenCloud.Data.Entities;
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

        [HttpPost("create")]
        public async Task<IActionResult> Create([FromBody] Payment request)
        {
            var result = await _mpService.CrearPreferenciaAsync(
                request,
                successUrl: "https://nebula.andrescortes.dev/success",
                failureUrl: "https://nebula.andrescortes.dev/failure",
                notificationUrl: "https://service.nebula.andrescortes.dev/api/payments/webhook"
            );

            return Content(result, "application/json");
        }

        [HttpPost("webhook")]
        public async Task<IActionResult> Webhook([FromBody] JsonElement data)
        {
            await _mpService.ProcesarWebhookAsync(data);
            return Ok();
        }
    }
}
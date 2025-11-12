using System.ComponentModel.DataAnnotations;
using ZenCloud.Data.Entities;

namespace ZenCloud.DTOs;

public class CreateWebhookRequest
{
    [Required(ErrorMessage = "El nombre es obligatorio")]
    [MaxLength(200, ErrorMessage = "El nombre no puede exceder 200 caracteres")]
    public string Name { get; set; } = null!;

    [Required(ErrorMessage = "La URL es obligatoria")]
    [Url(ErrorMessage = "La URL no es válida")]
    [MaxLength(1000, ErrorMessage = "La URL no puede exceder 1000 caracteres")]
    public string Url { get; set; } = null!;

    [Required(ErrorMessage = "El tipo de evento es obligatorio")]
    public WebhookEventType EventType { get; set; }

    public bool Active { get; set; } = true;
}

public class UpdateWebhookRequest
{
    [MaxLength(200, ErrorMessage = "El nombre no puede exceder 200 caracteres")]
    public string? Name { get; set; }

    [Url(ErrorMessage = "La URL no es válida")]
    [MaxLength(1000, ErrorMessage = "La URL no puede exceder 1000 caracteres")]
    public string? Url { get; set; }

    public WebhookEventType? EventType { get; set; }

    public bool? Active { get; set; }
}

public class WebhookResponse
{
    public Guid WebhookId { get; set; }
    public string Name { get; set; } = null!;
    public string WebhookUrl { get; set; } = null!;
    public string EventType { get; set; } = null!;
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}


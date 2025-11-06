using System.ComponentModel.DataAnnotations;

namespace ZenCloud.DTOs;

public class ForgotPasswordRequest
{
    [Required(ErrorMessage = "El email es obligatorio.")]
    [EmailAddress(ErrorMessage = "El email no es válido.")]
    public string Email { get; set; } = null!;
}
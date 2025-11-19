using System.ComponentModel.DataAnnotations;

namespace ZenCloud.DTOs;

public class UpdateProfileRequest
{
    [Required(ErrorMessage = "El nombre completo es obligatorio.")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "El nombre debe tener entre 2 y 100 caracteres.")]
    public string FullName { get; set; } = string.Empty;
}

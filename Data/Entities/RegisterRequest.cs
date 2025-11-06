using System.ComponentModel.DataAnnotations;

namespace ZenCloud.Data.Entities;

public class RegisterRequest
{
    [Required(ErrorMessage = "El correo electrónico es obligatorio.")]
    [EmailAddress(ErrorMessage = "El correo electrónico no es válido.")]
    public string Email { get; set; } = null!;

    [Required(ErrorMessage = "La contraseña es obligatoria.")]
    [MinLength(8, ErrorMessage = "La contraseña debe tener al menos 8 caracteres.")]
    [RegularExpression("^(?=.*[a-z])(?=.*[A-Z])(?=.*\\d)(?=.*[^a-zA-Z\\d]).*$",
        ErrorMessage = "La contraseña debe contener al menos una letra minúscula, una letra mayúscula, un número y un carácter especial.")]
    public string Password { get; set; } = null!;

    [Required(ErrorMessage = "La confirmación de la contraseña es obligatoria.")]
    [Compare("Password", ErrorMessage = "La contraseña y la confirmación de la contraseña no coinciden.")]
    public string ConfirmPassword { get; set; } = null!;

    [Required(ErrorMessage = "El nombre completo es obligatorio.")]
    public string FullName { get; set; } = null!;
}
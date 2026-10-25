using System.ComponentModel.DataAnnotations;

namespace SigpaBackend.Models;

public sealed class CambiarContrasenaDto
{
    [Required, StringLength(128, MinimumLength = 12)]
    [RegularExpression(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^A-Za-z0-9]).+$")]
    public string Contrasena { get; set; } = string.Empty;
}

using System.ComponentModel.DataAnnotations;

namespace SigpaBackend.Models;

public sealed class ActualizarUsuarioDto
{
    [Required, StringLength(200, MinimumLength = 2)]
    public string NombreCompleto { get; set; } = string.Empty;

    [Required, RegularExpression("^(CC|CE|TI|PA|OTRO)$")]
    public string TipoDocumento { get; set; } = string.Empty;

    [Required, StringLength(40, MinimumLength = 4)]
    public string NumeroIdentificacion { get; set; } = string.Empty;

    [Required, EmailAddress, StringLength(254)]
    [RegularExpression(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    public string CorreoInstitucional { get; set; } = string.Empty;

    [StringLength(40)]
    public string? CodigoIdentificacion { get; set; }

    [Range(1, 20)]
    public int? Semestre { get; set; }

    [StringLength(160)]
    public string? ProgramaAcademico { get; set; }

    [StringLength(160)]
    public string? DepartamentoAcademico { get; set; }

    public bool Activo { get; set; }
}

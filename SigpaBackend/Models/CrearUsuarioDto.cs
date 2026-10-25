using System.ComponentModel.DataAnnotations;

namespace SigpaBackend.Models;

public sealed class CrearUsuarioDto
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

    [Required, RegularExpression("^(Estudiante|Profesor)$")]
    public string Rol { get; set; } = string.Empty;

    [StringLength(128, MinimumLength = 12)]
    [RegularExpression(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^A-Za-z0-9]).+$")]
    public string? Contrasena { get; set; }

    [StringLength(40)]
    public string? CodigoIdentificacion { get; set; }

    [Range(1, 20)]
    public int? Semestre { get; set; }

    [StringLength(160)]
    public string? ProgramaAcademico { get; set; }

    [StringLength(160)]
    public string? DepartamentoAcademico { get; set; }

    public List<Guid> CursoIds { get; set; } = [];

    public bool EnviarCorreoBienvenida { get; set; }
}

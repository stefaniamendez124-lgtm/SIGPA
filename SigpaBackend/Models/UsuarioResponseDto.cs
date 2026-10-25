namespace SigpaBackend.Models;

public sealed class UsuarioResponseDto
{
    public Guid Id { get; init; }
    public string NombreCompleto { get; init; } = string.Empty;
    public string CorreoInstitucional { get; init; } = string.Empty;
    public string Rol { get; init; } = string.Empty;
    public string TipoDocumento { get; init; } = string.Empty;
    public string NumeroIdentificacion { get; init; } = string.Empty;
    public string? CodigoIdentificacion { get; init; }
    public int? Semestre { get; init; }
    public string? ProgramaAcademico { get; init; }
    public string? DepartamentoAcademico { get; init; }
    public bool Activo { get; init; }
    public bool DebeCambiarContrasena { get; init; }
    public IReadOnlyList<CursoResumenDto> Cursos { get; init; } = [];
}

public sealed class CursoResumenDto
{
    public Guid Id { get; init; }
    public string Codigo { get; init; } = string.Empty;
    public string Nombre { get; init; } = string.Empty;
    public int? Semestre { get; init; }
    public string? ProgramaAcademico { get; init; }
    public string? PeriodoAcademico { get; init; }
}

public sealed class PaginaUsuariosDto
{
    public IReadOnlyList<UsuarioResponseDto> Items { get; init; } = [];
    public int Pagina { get; init; }
    public int TamanoPagina { get; init; }
    public int Total { get; init; }
}

public sealed class CursoAdministracionDto
{
    public Guid Id { get; init; }
    public string Codigo { get; init; } = string.Empty;
    public string Nombre { get; init; } = string.Empty;
    public int? Semestre { get; init; }
    public string? ProgramaAcademico { get; init; }
    public string? PeriodoAcademico { get; init; }
    public bool Activo { get; init; }
}

public sealed class AsignarCursosDto
{
    public List<Guid> CursoIds { get; set; } = [];
}

public sealed class ValidarCargaDto
{
    public IReadOnlyList<CargaUsuarioFilaDto> Filas { get; init; } = [];
}

public sealed class CargaUsuarioFilaDto
{
    public int Fila { get; init; }
    public CrearUsuarioDto Usuario { get; init; } = new();
    public IReadOnlyList<string> Errores { get; init; } = [];
}

public sealed class ResultadoCargaUsuariosDto
{
    public int Creados { get; init; }
    public int Fallidos { get; init; }
    public IReadOnlyList<ErrorCargaUsuarioDto> Errores { get; init; } = [];
    public IReadOnlyList<ErrorCargaUsuarioDto> CorreosPendientes { get; init; } = [];
}

public sealed class ErrorCargaUsuarioDto
{
    public int Fila { get; init; }
    public string Correo { get; init; } = string.Empty;
    public string Error { get; init; } = string.Empty;
}

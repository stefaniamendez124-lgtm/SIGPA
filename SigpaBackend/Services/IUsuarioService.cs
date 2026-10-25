using SigpaBackend.Models;

namespace SigpaBackend.Services;

public interface IUsuarioService
{
    Task<PaginaUsuariosDto> ListarAsync(
        int pagina,
        int tamanoPagina,
        string? rol,
        string? programa,
        Guid? cursoId,
        bool? activo,
        string? busqueda,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<CursoAdministracionDto>> ObtenerCursosAsync(CancellationToken cancellationToken);
    Task<UsuarioCreadoResultadoDto> CrearAsync(
        CrearUsuarioDto request,
        Guid coordinadorId,
        CancellationToken cancellationToken,
        bool generarContrasenaSiFalta = false);
    Task<UsuarioResponseDto?> ActualizarAsync(Guid id, ActualizarUsuarioDto request, Guid coordinadorId, CancellationToken cancellationToken);
    Task<UsuarioResponseDto?> AsignarCursosAsync(Guid id, AsignarCursosDto request, Guid coordinadorId, CancellationToken cancellationToken);
    Task<IReadOnlyList<CargaUsuarioFilaDto>> ValidarCargaAsync(IReadOnlyList<CargaUsuarioFilaDto> filas, CancellationToken cancellationToken);
    Task<ResultadoCargaUsuariosDto> ImportarAsync(IReadOnlyList<CargaUsuarioFilaDto> filas, Guid coordinadorId, CancellationToken cancellationToken);
}

public sealed class UsuarioCreadoResultadoDto
{
    public UsuarioResponseDto Usuario { get; init; } = new();
    public bool CorreoBienvenidaEnviado { get; init; }
    public string? Advertencia { get; init; }
}

public sealed class UsuarioManagementException : Exception
{
    public UsuarioManagementException(int statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}

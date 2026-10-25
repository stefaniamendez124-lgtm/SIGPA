using Microsoft.EntityFrameworkCore;
using SigpaBackend.Models;

namespace SigpaBackend.Repositories;

public interface IUsuarioRepository
{
    IQueryable<Usuario> Usuarios { get; }
    IQueryable<Curso> Cursos { get; }
    Task<Usuario?> ObtenerAsync(Guid id, CancellationToken cancellationToken);
    Task GuardarAsync(CancellationToken cancellationToken);
    Task IniciarTransaccionAsync(CancellationToken cancellationToken);
    Task ConfirmarTransaccionAsync(CancellationToken cancellationToken);
    Task RevertirTransaccionAsync(CancellationToken cancellationToken);
}

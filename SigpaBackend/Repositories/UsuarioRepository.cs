using Microsoft.EntityFrameworkCore;
using SigpaBackend.Data;
using SigpaBackend.Models;

namespace SigpaBackend.Repositories;

public sealed class UsuarioRepository : IUsuarioRepository, IAsyncDisposable
{
    private readonly SigpaContext _context;
    private Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? _transaction;

    public UsuarioRepository(SigpaContext context)
    {
        _context = context;
    }

    public IQueryable<Usuario> Usuarios => _context.Usuarios.AsNoTracking();
    public IQueryable<Curso> Cursos => _context.Cursos.AsNoTracking();

    public Task<Usuario?> ObtenerAsync(Guid id, CancellationToken cancellationToken) =>
        _context.Usuarios.FirstOrDefaultAsync(user => user.Id == id, cancellationToken);

    public Task GuardarAsync(CancellationToken cancellationToken) =>
        _context.SaveChangesAsync(cancellationToken);

    public async Task IniciarTransaccionAsync(CancellationToken cancellationToken)
    {
        _transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
    }

    public async Task ConfirmarTransaccionAsync(CancellationToken cancellationToken)
    {
        if (_transaction is not null)
            await _transaction.CommitAsync(cancellationToken);
    }

    public async Task RevertirTransaccionAsync(CancellationToken cancellationToken)
    {
        if (_transaction is not null)
            await _transaction.RollbackAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_transaction is not null)
            await _transaction.DisposeAsync();
    }
}

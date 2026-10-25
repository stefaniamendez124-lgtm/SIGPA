namespace SigpaBackend.Services;

public interface ISupabaseAdminUserService
{
    Task<Guid> CreateAsync(string email, string temporaryPassword, CancellationToken cancellationToken);
    Task UpdateAsync(Guid userId, string? email, bool active, bool mustChangePassword, CancellationToken cancellationToken);
    Task ChangePasswordAsync(Guid userId, string password, CancellationToken cancellationToken);
    Task DeleteAsync(Guid userId, CancellationToken cancellationToken);
}

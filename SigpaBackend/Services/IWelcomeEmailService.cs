namespace SigpaBackend.Services;

public interface IWelcomeEmailService
{
    Task SendTemporaryPasswordAsync(
        string email,
        string fullName,
        string temporaryPassword,
        CancellationToken cancellationToken);
}

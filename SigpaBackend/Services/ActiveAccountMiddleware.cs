using Microsoft.EntityFrameworkCore;
using Npgsql;
using SigpaBackend.Data;

namespace SigpaBackend.Services;

public sealed class ActiveAccountMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ActiveAccountMiddleware> _logger;

    public ActiveAccountMiddleware(
        RequestDelegate next,
        ILogger<ActiveAccountMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, SigpaContext dbContext)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var userId = UserAccessService.GetUserId(context.User);
            AccountStatus? account = null;
            if (userId.HasValue)
            {
                try
                {
                    account = await GetAccountStatusAsync(dbContext, userId.Value, context.RequestAborted);
                }
                catch (Exception exception) when (IsDatabaseConnectionFailure(exception))
                {
                    _logger.LogError(exception,
                        "No se pudo conectar a PostgreSQL al validar una cuenta autenticada.");
                    context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                    await context.Response.WriteAsJsonAsync(
                        new
                        {
                            detail = "La base de datos no está disponible. Verifica la conexión PostgreSQL del backend."
                        },
                        context.RequestAborted);
                    return;
                }
            }

            if (account is null || !account.Activo)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(
                    new { detail = "La cuenta no existe o está deshabilitada." },
                    context.RequestAborted);
                return;
            }

            var isPasswordChangeRequest =
                HttpMethods.IsPost(context.Request.Method) &&
                string.Equals(
                    context.Request.Path.Value,
                    "/api/auth/password-changed",
                    StringComparison.OrdinalIgnoreCase);
            if (account.DebeCambiarContrasena && !isPasswordChangeRequest)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(
                    new { detail = "Debes cambiar la contraseña temporal antes de usar SIGPA." },
                    context.RequestAborted);
                return;
            }
        }

        await _next(context);
    }

    private static bool IsDatabaseConnectionFailure(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is NpgsqlException && current is not PostgresException)
                return true;
        }

        return false;
    }

    private static async Task<AccountStatus?> GetAccountStatusAsync(
        SigpaContext dbContext,
        Guid userId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await dbContext.Usuarios
                .AsNoTracking()
                .Where(user => user.Id == userId)
                .Select(user => new AccountStatus(user.Activo, user.DebeCambiarContrasena))
                .FirstOrDefaultAsync(cancellationToken);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UndefinedColumn)
        {
            var legacyProfileExists = await dbContext.Usuarios
                .AsNoTracking()
                .AnyAsync(user => user.Id == userId, cancellationToken);
            return legacyProfileExists ? new AccountStatus(true, false) : null;
        }
    }

    private sealed record AccountStatus(bool Activo, bool DebeCambiarContrasena);
}

using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace SigpaBackend.Services;

public sealed class PostgresSchemaErrorMiddleware
{
    private const string UndefinedTable = "42P01";
    private const string UndefinedColumn = "42703";
    private readonly RequestDelegate _next;
    private readonly ILogger<PostgresSchemaErrorMiddleware> _logger;

    public PostgresSchemaErrorMiddleware(
        RequestDelegate next,
        ILogger<PostgresSchemaErrorMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (PostgresException exception) when (
            exception.SqlState is UndefinedTable or UndefinedColumn)
        {
            _logger.LogWarning(
                "La solicitud {Path} requiere un objeto PostgreSQL que no existe en el esquema actual. " +
                "SQLSTATE {SqlState}, tabla {TableName}, columna {ColumnName}.",
                context.Request.Path,
                exception.SqlState,
                exception.TableName,
                exception.ColumnName);

            if (context.Response.HasStarted)
                throw;

            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsJsonAsync(new ProblemDetails
            {
                Status = StatusCodes.Status503ServiceUnavailable,
                Title = "La función solicitada no está disponible en el esquema actual de la base de datos."
            }, context.RequestAborted);
        }
    }
}

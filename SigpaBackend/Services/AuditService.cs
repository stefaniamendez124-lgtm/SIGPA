using System.Text.Json;
using Microsoft.AspNetCore.Http;
using SigpaBackend.Data;
using SigpaBackend.Models;

namespace SigpaBackend.Services;

public sealed class AuditService
{
    private readonly SigpaContext _context;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public AuditService(SigpaContext context, IHttpContextAccessor httpContextAccessor)
    {
        _context = context;
        _httpContextAccessor = httpContextAccessor;
    }

    public void Record(Guid? actorId, string action, string resourceType, Guid resourceId)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        _context.EventosAuditoria.Add(new EventoAuditoria
        {
            ActorId = actorId,
            Accion = action,
            RecursoTipo = resourceType,
            RecursoId = resourceId,
            CorrelationId = httpContext?.TraceIdentifier ?? string.Empty,
            Datos = JsonSerializer.Serialize(new { path = httpContext?.Request.Path.Value })
        });
    }
}

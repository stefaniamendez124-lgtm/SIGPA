namespace SigpaBackend.Models;

public class EventoAuditoria
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? ActorId { get; set; }
    public string Accion { get; set; } = string.Empty;
    public string RecursoTipo { get; set; } = string.Empty;
    public Guid RecursoId { get; set; }
    public DateTime OcurridoAt { get; set; } = DateTime.UtcNow;
    public string CorrelationId { get; set; } = string.Empty;
    public string Datos { get; set; } = "{}";
}

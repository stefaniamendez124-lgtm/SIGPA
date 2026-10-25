namespace SigpaBackend.Models;

public sealed class ProyectoListadoDto
{
    public Guid Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public string Problema { get; set; } = string.Empty;
    public string ObjetivoGeneral { get; set; } = string.Empty;
    public string Curso { get; set; } = string.Empty;
    public string Estado { get; set; } = string.Empty;
    public int Progreso { get; set; }
    public DateTime? FechaInicio { get; set; }
    public DateTime? FechaFin { get; set; }
    public DateOnly? FechaEntrega { get; set; }
    public Guid CreadorId { get; set; }
    public string? ComentariosEvaluacion { get; set; }
}

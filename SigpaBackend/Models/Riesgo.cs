namespace SigpaBackend.Models;

public class Riesgo
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProyectoId { get; set; }
    public Guid ReportadoPor { get; set; }
    public string Titulo { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public string Severidad { get; set; } = "Media";
    public string Estado { get; set; } = "Abierto";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ResueltoAt { get; set; }
}

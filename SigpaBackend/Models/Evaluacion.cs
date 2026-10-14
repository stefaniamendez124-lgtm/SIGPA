namespace SigpaBackend.Models;

public class Evaluacion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EntregableId { get; set; }
    public Guid EvaluadorId { get; set; }
    public decimal PuntajeFinal { get; set; }
    public string ObservacionGeneral { get; set; } = string.Empty;
    public bool Publicada { get; set; }
    public DateTime EvaluadaAt { get; set; } = DateTime.UtcNow;
    public DateTime? PublicadaAt { get; set; }
}

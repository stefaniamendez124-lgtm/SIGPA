namespace SigpaBackend.Models;

public class EvaluacionCriterio
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EvaluacionId { get; set; }
    public Guid CriterioId { get; set; }
    public Guid NivelId { get; set; }
    public decimal PuntajeObtenido { get; set; }
    public string Observacion { get; set; } = string.Empty;
}

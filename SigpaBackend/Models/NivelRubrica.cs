namespace SigpaBackend.Models;

public class NivelRubrica
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CriterioId { get; set; }
    public int Nivel { get; set; }
    public string Etiqueta { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public decimal Puntaje { get; set; }
}

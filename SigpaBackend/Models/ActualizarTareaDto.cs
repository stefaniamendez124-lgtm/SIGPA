namespace SigpaBackend.Models;

public sealed class ActualizarTareaDto
{
    public string Titulo { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public Guid? ResponsableId { get; set; }
    public DateTime? FechaVencimiento { get; set; }
    public string Estado { get; set; } = "Por Hacer";
}

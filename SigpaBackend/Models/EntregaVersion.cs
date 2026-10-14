namespace SigpaBackend.Models;

public class EntregaVersion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EntregableId { get; set; }
    public Guid EnviadaPor { get; set; }
    public string ArchivoUrl { get; set; } = string.Empty;
    public DateTime EnviadaAt { get; set; } = DateTime.UtcNow;
    public string Estado { get; set; } = "Entregado a Tiempo";
    public decimal PenalizacionAplicadaPct { get; set; }
}

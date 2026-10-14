namespace SigpaBackend.Models;

public class ObjetivoEspecifico
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProyectoId { get; set; }
    public string Descripcion { get; set; } = string.Empty;
    public int Orden { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

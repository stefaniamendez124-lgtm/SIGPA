namespace SigpaBackend.Models;

public sealed class Notificacion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UsuarioId { get; set; }
    public Guid? ProyectoId { get; set; }
    public string Tipo { get; set; } = string.Empty;
    public string Mensaje { get; set; } = string.Empty;
    public DateTime CreadaAt { get; set; } = DateTime.UtcNow;
    public DateTime? LeidaAt { get; set; }
}

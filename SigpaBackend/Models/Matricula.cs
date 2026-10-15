namespace SigpaBackend.Models;

public sealed class Matricula
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CursoId { get; set; }
    public Guid EstudianteId { get; set; }
    public string Estado { get; set; } = "Activa";
    public DateTime CreadaAt { get; set; } = DateTime.UtcNow;
}

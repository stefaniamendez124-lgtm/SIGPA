namespace SigpaBackend.Models;

public sealed class DocenteCurso
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UsuarioId { get; set; }
    public Guid CursoId { get; set; }
    public string RolDocente { get; set; } = "Titular";
    public DateTime AsignadoAt { get; set; } = DateTime.UtcNow;
}

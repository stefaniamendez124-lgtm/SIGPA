namespace SigpaBackend.Models;

public sealed class Curso
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Codigo { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public Guid? DocenteId { get; set; }
    public bool Activo { get; set; } = true;
}

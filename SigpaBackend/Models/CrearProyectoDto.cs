namespace SigpaBackend.Models;

public sealed class CrearProyectoDto
{
    public string Nombre { get; set; } = string.Empty;
    public string Problema { get; set; } = string.Empty;
    public string ObjetivoGeneral { get; set; } = string.Empty;
    public List<string> ObjetivosEspecificos { get; set; } = [];
    public Guid CursoId { get; set; }
    public List<Guid> IntegrantesIds { get; set; } = [];
}

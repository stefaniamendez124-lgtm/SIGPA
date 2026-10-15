using Microsoft.AspNetCore.Http;

namespace SigpaBackend.Models;

public sealed class SubirEntregableProyectoDto
{
    public Guid HitoId { get; set; }
    public IFormFile? Archivo { get; set; }
    public string? Url { get; set; }
}

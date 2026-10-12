using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SigpaBackend.Data;
using SigpaBackend.Models;
using System.Security.Claims;

namespace SigpaBackend.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize] 
    public class ProyectosController : ControllerBase
    {
        private readonly SigpaContext _context;

        public ProyectosController(SigpaContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> ObtenerProyectos()
        {
            var proyectos = await _context.Proyectos.ToListAsync(); // <-- Aquí es donde te marcaba error
            return Ok(proyectos);
        }

        [HttpPost]
        public async Task<IActionResult> CrearProyecto([FromBody] Proyecto proyecto)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (userIdClaim == null) return Unauthorized();

            proyecto.FechaInicio = proyecto.FechaInicio.ToUniversalTime();
            proyecto.FechaFin = proyecto.FechaFin.ToUniversalTime();
            proyecto.CreadorId = Guid.Parse(userIdClaim);

            _context.Proyectos.Add(proyecto); // <-- Y aquí también
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(ObtenerProyectos), new { id = proyecto.Id }, proyecto);
        }

        [HttpPost("{id}/integrantes")]
public async Task<IActionResult> AgregarIntegrante(Guid id, [FromBody] AgregarIntegranteDto request)
{
    // 1. Verificamos la regla de negocio RN-02: Máximo 5 integrantes
    var cantidadIntegrantes = await _context.ProyectoIntegrantes
        .CountAsync(pi => pi.ProyectoId == id);
        
    if (cantidadIntegrantes >= 5)
    {
        return BadRequest("El equipo ya alcanzó el límite máximo de 5 integrantes (RN-02).");
    }

    // 2. Verificamos que el estudiante no esté duplicado en este equipo
    var yaExiste = await _context.ProyectoIntegrantes
        .AnyAsync(pi => pi.ProyectoId == id && pi.UsuarioId == request.UsuarioId);

    if (yaExiste)
    {
        return BadRequest("El estudiante ya pertenece a este proyecto.");
    }

    // 3. Agregamos el integrante
    var nuevoIntegrante = new ProyectoIntegrante
    {
        ProyectoId = id,
        UsuarioId = request.UsuarioId
    };

    _context.ProyectoIntegrantes.Add(nuevoIntegrante);
    await _context.SaveChangesAsync();

    return Ok(new { mensaje = "Estudiante agregado exitosamente al proyecto." });
}

[HttpPatch("{id}/evaluar")]
public async Task<IActionResult> EvaluarProyecto(Guid id, [FromBody] EvaluarProyectoDto request)
{
    var proyecto = await _context.Proyectos.FindAsync(id);
    if (proyecto == null) return NotFound("Proyecto no encontrado.");

    var estadosValidos = new[] { "Aprobado", "Rechazado" };
    if (!estadosValidos.Contains(request.Estado))
    {
        return BadRequest("Estado de evaluación no válido. Solo se permite 'Aprobado' o 'Rechazado'.");
    }

    proyecto.Estado = request.Estado;
    proyecto.ComentariosEvaluacion = request.Comentarios;

    await _context.SaveChangesAsync();

    return Ok(new { 
        mensaje = $"El proyecto ha sido {request.Estado.ToLower()}.", 
        estadoActual = proyecto.Estado 
    });
}
    }
}
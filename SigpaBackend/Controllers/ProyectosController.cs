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
            var proyectos = await _context.Proyectos
                .OrderByDescending(p => p.FechaInicio ?? p.FechaEntrega ?? DateTime.UtcNow)
                .ToListAsync();

            return Ok(proyectos);
        }

        [HttpGet("mis-proyectos")]
        public async Task<IActionResult> ObtenerMisProyectos()
        {
            var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
            if (string.IsNullOrWhiteSpace(userIdClaim))
            {
                return Unauthorized();
            }

            var userId = Guid.Parse(userIdClaim);

            var proyectos = await _context.Proyectos
                .Where(p => p.CreadorId == userId)
                .OrderByDescending(p => p.FechaInicio ?? p.FechaEntrega ?? DateTime.UtcNow)
                .ToListAsync();

            return Ok(proyectos);
        }

        [HttpPost]
        public async Task<IActionResult> CrearProyecto([FromBody] Proyecto proyecto)
        {
            var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
            if (string.IsNullOrWhiteSpace(userIdClaim))
            {
                return Unauthorized();
            }

            proyecto.Id = Guid.NewGuid();
            proyecto.CreadorId = Guid.Parse(userIdClaim);
            proyecto.Estado = string.IsNullOrWhiteSpace(proyecto.Estado) ? "Pendiente" : proyecto.Estado;
            proyecto.Curso = string.IsNullOrWhiteSpace(proyecto.Curso) ? "Ingeniería de requisitos" : proyecto.Curso;
            proyecto.Progreso = proyecto.Progreso <= 0 ? 15 : Math.Clamp(proyecto.Progreso, 0, 100);

            if (proyecto.FechaInicio == null)
            {
                proyecto.FechaInicio = DateTime.UtcNow;
            }

            if (proyecto.FechaEntrega != null)
            {
                proyecto.FechaEntrega = DateTime.SpecifyKind(proyecto.FechaEntrega.Value, DateTimeKind.Utc);
            }

            _context.Proyectos.Add(proyecto);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(ObtenerProyectos), new { id = proyecto.Id }, proyecto);
        }

        [HttpPost("{id}/integrantes")]
        public async Task<IActionResult> AgregarIntegrante(Guid id, [FromBody] AgregarIntegranteDto request)
        {
            var cantidadIntegrantes = await _context.ProyectoIntegrantes
                .CountAsync(pi => pi.ProyectoId == id);

            if (cantidadIntegrantes >= 5)
            {
                return BadRequest("El equipo ya alcanzó el límite máximo de 5 integrantes (RN-02).");
            }

            var yaExiste = await _context.ProyectoIntegrantes
                .AnyAsync(pi => pi.ProyectoId == id && pi.UsuarioId == request.UsuarioId);

            if (yaExiste)
            {
                return BadRequest("El estudiante ya pertenece a este proyecto.");
            }

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

            return Ok(new
            {
                mensaje = $"El proyecto ha sido {request.Estado.ToLower()}.",
                estadoActual = proyecto.Estado
            });
        }
    }

    public class AgregarIntegranteDto
    {
        public Guid UsuarioId { get; set; }
    }

    public class EvaluarProyectoDto
    {
        public string Estado { get; set; } = string.Empty;
        public string? Comentarios { get; set; }
    }
}
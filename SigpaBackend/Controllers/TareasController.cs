using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SigpaBackend.Data;
using SigpaBackend.Models;
using SigpaBackend.Services;
using System.Linq;

namespace SigpaBackend.Controllers
{
    [Route("api")]
    [ApiController]
    [Authorize]
    public class TareasController : ControllerBase
    {
        private readonly SigpaContext _context;
        private readonly UserAccessService _access;
        private readonly AuditService _audit;

        public TareasController(SigpaContext context, UserAccessService access, AuditService audit)
        {
            _context = context;
            _access = access;
            _audit = audit;
        }

        [HttpGet("proyectos/{proyectoId:guid}/tareas")]
        [HttpGet("/api/projects/{proyectoId:guid}/tasks")]
        public async Task<IActionResult> ObtenerTareas(Guid proyectoId)
        {
            var userId = UserAccessService.GetUserId(User);
            if (userId is null) return Unauthorized();
            if (!await _access.CanReadProjectAsync(proyectoId, userId.Value)) return NotFound();

            var tasks = await _context.TareasKanban
                .Where(task => task.ProyectoId == proyectoId)
                .OrderBy(task => task.Estado == "Por Hacer" ? 0 : task.Estado == "En Proceso" ? 1 : 2)
                .ThenBy(task => task.FechaVencimiento)
                .ToListAsync();
            return Ok(tasks);
        }

        // 1. Crear una nueva tarjeta en el tablero del proyecto
        [HttpPost("proyectos/{proyectoId}/tareas")]
        [HttpPost("/api/projects/{proyectoId:guid}/tasks")]
        public async Task<IActionResult> CrearTarea(Guid proyectoId, [FromBody] CrearTareaDto request)
        {
            var userId = UserAccessService.GetUserId(User);
            if (userId is null) return Unauthorized();
            if (!await _access.IsProjectMemberAsync(proyectoId, userId.Value)) return NotFound("Proyecto no encontrado.");
            if (request.ResponsableId is null || request.ResponsableId == Guid.Empty)
                return BadRequest("La tarea debe tener un responsable asignado.");
            if (!await _access.IsProjectMemberAsync(proyectoId, request.ResponsableId.Value))
                return BadRequest("El responsable debe pertenecer al equipo del proyecto.");
            if (string.IsNullOrWhiteSpace(request.Titulo))
                return BadRequest("El título de la tarea es obligatorio.");

            var nuevaTarea = new TareaKanban
            {
                ProyectoId = proyectoId,
                Titulo = request.Titulo,
                Descripcion = request.Descripcion ?? string.Empty,
                ResponsableId = request.ResponsableId.Value,
                FechaVencimiento = request.FechaVencimiento?.ToUniversalTime(),
                Estado = "Por Hacer" // Siempre inicia en la primera columna
            };

            _context.TareasKanban.Add(nuevaTarea);
            _audit.Record(userId, "kanban.tarea_creada", "tarea_kanban", nuevaTarea.Id);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(ObtenerTareas), new { proyectoId }, nuevaTarea);
        }

        // 2. Simular el "Arrastrar y Soltar" (Drag & Drop)
        [HttpPatch("tareas/{id}/estado")]
        public async Task<IActionResult> ActualizarEstado(Guid id, [FromBody] ActualizarEstadoTareaDto request)
        {
            var tarea = await _context.TareasKanban.FindAsync(id);
            if (tarea == null) return NotFound("Tarea no encontrada.");
            var userId = UserAccessService.GetUserId(User);
            if (userId is null) return Unauthorized();
            if (!await _access.IsProjectMemberAsync(tarea.ProyectoId, userId.Value))
                return NotFound("Tarea no encontrada.");
            if (string.IsNullOrWhiteSpace(request.Estado))
                return BadRequest("El estado de la tarea es obligatorio.");

            var estadoNormalizado = request.Estado.Trim() == "Completado"
                ? "Hecho"
                : request.Estado.Trim();
            var estadosValidos = new[] { "Por Hacer", "En Proceso", "Hecho" };
            if (!estadosValidos.Contains(estadoNormalizado))
            {
                return BadRequest("Estado no válido. Debe ser 'Por Hacer', 'En Proceso' o 'Hecho'.");
            }
            if (estadoNormalizado == "En Proceso" && tarea.ResponsableId is null)
                return BadRequest("No se puede mover una tarea a 'En Proceso' sin responsable.");

            tarea.Estado = estadoNormalizado;
            _audit.Record(userId, "kanban.tarea_movida", "tarea_kanban", tarea.Id);
            await _context.SaveChangesAsync();

            return Ok(new { mensaje = "Tarjeta movida exitosamente", estadoActual = tarea.Estado });
        }

        [HttpPut("/api/kanban/tasks/{id:guid}")]
        public async Task<IActionResult> ActualizarTarea(Guid id, [FromBody] ActualizarTareaDto request)
        {
            var tarea = await _context.TareasKanban.FindAsync(id);
            if (tarea is null) return NotFound("Tarea no encontrada.");

            var userId = UserAccessService.GetUserId(User);
            if (userId is null) return Unauthorized();
            if (!await _access.IsProjectMemberAsync(tarea.ProyectoId, userId.Value))
                return NotFound("Tarea no encontrada.");

            if (string.IsNullOrWhiteSpace(request.Titulo))
                return BadRequest("El título de la tarea es obligatorio.");
            if (string.IsNullOrWhiteSpace(request.Estado))
                return BadRequest("El estado de la tarea es obligatorio.");
            var estado = request.Estado.Trim() == "Completado" ? "Hecho" : request.Estado.Trim();
            if (estado is not ("Por Hacer" or "En Proceso" or "Hecho"))
                return BadRequest("Estado no válido. Debe ser 'Por Hacer', 'En Proceso' o 'Hecho'.");
            if (request.ResponsableId is null || request.ResponsableId == Guid.Empty)
                return BadRequest("La tarea debe tener un responsable asignado.");
            if (!await _access.IsProjectMemberAsync(tarea.ProyectoId, request.ResponsableId.Value))
                return BadRequest("El responsable debe pertenecer al equipo del proyecto.");
            tarea.Titulo = request.Titulo.Trim();
            tarea.Descripcion = request.Descripcion?.Trim() ?? string.Empty;
            tarea.ResponsableId = request.ResponsableId.Value;
            tarea.FechaVencimiento = request.FechaVencimiento?.ToUniversalTime();
            tarea.Estado = estado;
            _audit.Record(userId, "kanban.tarea_actualizada", "tarea_kanban", tarea.Id);
            await _context.SaveChangesAsync();

            return Ok(tarea);
        }

        [HttpDelete("/api/kanban/tasks/{id:guid}")]
        public async Task<IActionResult> EliminarTarea(Guid id)
        {
            var tarea = await _context.TareasKanban.FindAsync(id);
            if (tarea is null) return NotFound("Tarea no encontrada.");

            var userId = UserAccessService.GetUserId(User);
            if (userId is null) return Unauthorized();
            if (!await _access.IsProjectMemberAsync(tarea.ProyectoId, userId.Value))
                return NotFound("Tarea no encontrada.");

            _context.TareasKanban.Remove(tarea);
            _audit.Record(userId, "kanban.tarea_eliminada", "tarea_kanban", tarea.Id);
            await _context.SaveChangesAsync();
            return NoContent();
        }
    }
}
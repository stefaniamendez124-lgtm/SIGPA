using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SigpaBackend.Data;
using SigpaBackend.Models;
using System.Linq;

namespace SigpaBackend.Controllers
{
    [Route("api")]
    [ApiController]
    [Authorize]
    public class TareasController : ControllerBase
    {
        private readonly SigpaContext _context;

        public TareasController(SigpaContext context)
        {
            _context = context;
        }

        // 1. Crear una nueva tarjeta en el tablero del proyecto
        [HttpPost("proyectos/{proyectoId}/tareas")]
        public async Task<IActionResult> CrearTarea(Guid proyectoId, [FromBody] CrearTareaDto request)
        {
            var proyectoExiste = await _context.Proyectos.AnyAsync(p => p.Id == proyectoId);
            if (!proyectoExiste) return NotFound("Proyecto no encontrado.");

            var nuevaTarea = new TareaKanban
            {
                ProyectoId = proyectoId,
                Titulo = request.Titulo,
                ResponsableId = request.ResponsableId,
                FechaVencimiento = request.FechaVencimiento?.ToUniversalTime(),
                Estado = "Por Hacer" // Siempre inicia en la primera columna
            };

            _context.TareasKanban.Add(nuevaTarea);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(CrearTarea), new { id = nuevaTarea.Id }, nuevaTarea);
        }

        // 2. Simular el "Arrastrar y Soltar" (Drag & Drop)
        [HttpPatch("tareas/{id}/estado")]
        public async Task<IActionResult> ActualizarEstado(Guid id, [FromBody] ActualizarEstadoTareaDto request)
        {
            var tarea = await _context.TareasKanban.FindAsync(id);
            if (tarea == null) return NotFound("Tarea no encontrada.");

            // Validamos que no intenten inyectar columnas falsas
            var estadosValidos = new[] { "Por Hacer", "En Proceso", "Completado" };
            if (!estadosValidos.Contains(request.Estado))
            {
                return BadRequest("Estado no válido. Debe ser 'Por Hacer', 'En Proceso' o 'Completado'.");
            }

            tarea.Estado = request.Estado;
            await _context.SaveChangesAsync();

            return Ok(new { mensaje = "Tarjeta movida exitosamente", estadoActual = tarea.Estado });
        }
    }
}
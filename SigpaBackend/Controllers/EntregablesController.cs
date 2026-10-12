using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SigpaBackend.Data;
using SigpaBackend.Models;

namespace SigpaBackend.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class EntregablesController : ControllerBase
    {
        private readonly SigpaContext _context;

        public EntregablesController(SigpaContext context)
        {
            _context = context;
        }

        [HttpPost]
        public async Task<IActionResult> CrearEntregable([FromBody] Entregable entregable)
        {
            // Aseguramos que la fecha tenga el formato UTC que exige PostgreSQL
            entregable.FechaLimite = entregable.FechaLimite.ToUniversalTime();
            
            _context.Entregables.Add(entregable);
            await _context.SaveChangesAsync();
            
            return CreatedAtAction(nameof(CrearEntregable), new { id = entregable.Id }, entregable);
        }

        [HttpPost("{id}/entregar")]
        public async Task<IActionResult> SubirEntregable(Guid id, [FromBody] SubirAvanceDto avance)
        {
            var entregable = await _context.Entregables.FindAsync(id);
            if (entregable == null) return NotFound("Entregable no encontrado.");

            var fechaActual = DateTime.UtcNow;

            // Verificación de RN-01: Bloqueo de entregas extemporáneas
            if (fechaActual > entregable.FechaLimite)
            {
                return BadRequest("La fecha límite ha pasado. No se permiten entregas extemporáneas (RN-01).");
            }

            entregable.ArchivoUrl = avance.ArchivoUrl;
            entregable.FechaEntrega = fechaActual;
            entregable.Estado = "Entregado a Tiempo";

            await _context.SaveChangesAsync();

            return Ok(new { mensaje = "Avance registrado correctamente.", fecha = fechaActual });
        }
    }
}
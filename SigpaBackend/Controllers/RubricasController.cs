using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SigpaBackend.Data;
using SigpaBackend.Models;
using System.Linq;

namespace SigpaBackend.Controllers
{
    [Route("api/entregables")] // Usamos la misma ruta base de entregables por convención REST
    [ApiController]
    [Authorize]
    public class RubricasController : ControllerBase
    {
        private readonly SigpaContext _context;

        public RubricasController(SigpaContext context)
        {
            _context = context;
        }

        [HttpPost("{id}/rubrica")]
        public async Task<IActionResult> CrearRubrica(Guid id, [FromBody] CrearRubricaDto request)
        {
            // 1. Verificamos que el entregable exista
            var entregable = await _context.Entregables.FindAsync(id);
            if (entregable == null) return NotFound("Entregable no encontrado.");

            // 2. Verificamos que se hayan enviado criterios
            if (request.Criterios == null || request.Criterios.Count == 0)
            {
                return BadRequest("Debe enviar al menos un criterio para la rúbrica.");
            }

            // 3. Aplicamos la regla RN-03: Sumar porcentajes
            decimal totalPorcentaje = request.Criterios.Sum(c => c.PesoPorcentual);

            if (totalPorcentaje != 100m)
            {
                return BadRequest($"La suma de los porcentajes es {totalPorcentaje}%. Debe ser exactamente 100% (RN-03).");
            }

            // 4. Transformamos el DTO al modelo de base de datos
            var nuevosCriterios = request.Criterios.Select(c => new CriterioRubrica
            {
                EntregableId = id,
                Nombre = c.Nombre,
                Descripcion = c.Descripcion,
                PesoPorcentual = c.PesoPorcentual
            }).ToList();

            // 5. Guardamos en bloque
            _context.CriteriosRubrica.AddRange(nuevosCriterios);
            await _context.SaveChangesAsync();

            return Ok(new { mensaje = "Rúbrica creada exitosamente.", criteriosRegistrados = nuevosCriterios.Count });
        }
    }
}
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SigpaBackend.Data;
using SigpaBackend.Models;
using SigpaBackend.Services;
using System.Linq;

namespace SigpaBackend.Controllers
{
    [Route("api/entregables")] // Usamos la misma ruta base de entregables por convención REST
    [ApiController]
    [Authorize]
    public class RubricasController : ControllerBase
    {
        private readonly SigpaContext _context;
        private readonly UserAccessService _access;
        private readonly AuditService _audit;

        public RubricasController(SigpaContext context, UserAccessService access, AuditService audit)
        {
            _context = context;
            _access = access;
            _audit = audit;
        }

        [HttpGet("{id:guid}/rubrica")]
        public async Task<IActionResult> ObtenerRubrica(Guid id)
        {
            var deliverable = await _context.Entregables.FindAsync(id);
            if (deliverable is null) return NotFound("Entregable no encontrado.");

            var userId = UserAccessService.GetUserId(User);
            if (userId is null) return Unauthorized();
            if (!await _access.CanReadProjectAsync(deliverable.ProyectoId, userId.Value))
                return NotFound();

            var criteria = await _context.CriteriosRubrica
                .Where(criterion => criterion.EntregableId == id)
                .OrderBy(criterion => criterion.Nombre)
                .ToListAsync();
            var criterionIds = criteria.Select(criterion => criterion.Id).ToArray();
            var levels = await _context.NivelesRubrica
                .Where(level => criterionIds.Contains(level.CriterioId))
                .OrderBy(level => level.Nivel)
                .ToListAsync();

            return Ok(criteria.Select(criterion => new
            {
                criterion.Id,
                criterion.Nombre,
                criterion.Descripcion,
                criterion.PesoPorcentual,
                Niveles = levels.Where(level => level.CriterioId == criterion.Id)
            }));
        }

        [HttpPost("{id}/rubrica")]
        public async Task<IActionResult> CrearRubrica(Guid id, [FromBody] CrearRubricaDto request)
        {
            var userId = UserAccessService.GetUserId(User);
            if (userId is null) return Unauthorized();

            // 1. Verificamos que el entregable exista
            var entregable = await _context.Entregables.FindAsync(id);
            if (entregable == null) return NotFound("Entregable no encontrado.");
            if (!await _access.CanReviewProjectAsync(entregable.ProyectoId, userId.Value)) return Forbid();
            if (await _context.CriteriosRubrica.AnyAsync(criterion => criterion.EntregableId == id))
                return Conflict("La rúbrica ya existe. Para modificarla debe crearse una nueva versión.");

            // 2. Verificamos que se hayan enviado criterios
            if (request.Criterios == null || request.Criterios.Count == 0)
            {
                return BadRequest("Debe enviar al menos un criterio para la rúbrica.");
            }
            if (request.Criterios.Any(criterion =>
                    string.IsNullOrWhiteSpace(criterion.Nombre) ||
                    criterion.PesoPorcentual <= 0 ||
                    criterion.PesoPorcentual > 100 ||
                    criterion.Niveles is null ||
                    criterion.Niveles.Count < 2 ||
                    criterion.Niveles.Any(level =>
                        level.Nivel <= 0 ||
                        string.IsNullOrWhiteSpace(level.Etiqueta) ||
                        level.Puntaje is < 0 or > 100) ||
                    criterion.Niveles.Select(level => level.Nivel).Distinct().Count() != criterion.Niveles.Count))
            {
                return BadRequest("Cada criterio requiere nombre, peso válido y al menos dos niveles únicos con puntaje entre 0 y 100.");
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
            for (var index = 0; index < nuevosCriterios.Count; index++)
            {
                var criterio = nuevosCriterios[index];
                var niveles = request.Criterios[index].Niveles
                    .Select(level => new NivelRubrica
                    {
                        CriterioId = criterio.Id,
                        Nivel = level.Nivel,
                        Etiqueta = level.Etiqueta.Trim(),
                        Descripcion = level.Descripcion.Trim(),
                        Puntaje = level.Puntaje
                    });
                _context.NivelesRubrica.AddRange(niveles);
            }
            _audit.Record(userId, "rubrica.creada", "entregable", id);
            await _context.SaveChangesAsync();

            return Ok(new { mensaje = "Rúbrica creada exitosamente.", criteriosRegistrados = nuevosCriterios.Count });
        }
    }
}
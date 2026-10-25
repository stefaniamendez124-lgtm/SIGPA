using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SigpaBackend.Data;
using SigpaBackend.Models;
using SigpaBackend.Services;

namespace SigpaBackend.Controllers;

[Route("api")]
[ApiController]
[Authorize]
public class CalificacionesController : ControllerBase
{
    private readonly SigpaContext _context;
    private readonly UserAccessService _access;
    private readonly AuditService _audit;

    public CalificacionesController(SigpaContext context, UserAccessService access, AuditService audit)
    {
        _context = context;
        _access = access;
        _audit = audit;
    }

    [HttpPost("entregables/{entregableId:guid}/evaluaciones")]
    public async Task<IActionResult> EvaluarEntrega(Guid entregableId, [FromBody] EvaluarEntregaDto request)
    {
        var userId = UserAccessService.GetUserId(User);
        if (userId is null) return Unauthorized();

        var deliverable = await _context.Entregables.FindAsync(entregableId);
        if (deliverable is null) return NotFound("Entregable no encontrado.");
        if (!await _access.CanReviewProjectAsync(deliverable.ProyectoId, userId.Value)) return Forbid();
        if (deliverable.FechaEntrega is null) return Conflict("No se puede calificar un entregable aún no enviado.");
        if (await _context.Evaluaciones.AnyAsync(evaluation => evaluation.EntregableId == entregableId))
            return Conflict("Este entregable ya tiene una evaluación registrada.");

        var criteria = await _context.CriteriosRubrica
            .Where(criterion => criterion.EntregableId == entregableId)
            .ToListAsync();
        if (criteria.Count == 0) return Conflict("El entregable todavía no tiene una rúbrica.");
        if (request.Criterios is null ||
            request.Criterios.Count != criteria.Count ||
            request.Criterios.Select(item => item.CriterioId).Distinct().Count() != criteria.Count ||
            criteria.Any(criterion => request.Criterios.All(item => item.CriterioId != criterion.Id)))
        {
            return BadRequest("Debe incluir exactamente una calificación para cada criterio de la rúbrica.");
        }
        var selectedLevelIds = request.Criterios.Select(item => item.NivelId).ToArray();
        var selectedLevels = await _context.NivelesRubrica
            .Where(level => selectedLevelIds.Contains(level.Id))
            .ToDictionaryAsync(level => level.Id);
        if (selectedLevels.Count != criteria.Count ||
            request.Criterios.Any(item =>
                !selectedLevels.TryGetValue(item.NivelId, out var level) ||
                level.CriterioId != item.CriterioId))
            return BadRequest("Cada criterio debe recibir un nivel de logro perteneciente a su rúbrica.");

        var weightedScore = criteria.Sum(criterion =>
        {
            var awarded = request.Criterios.Single(item => item.CriterioId == criterion.Id);
            return selectedLevels[awarded.NivelId].Puntaje * criterion.PesoPorcentual / 100m;
        });
        var finalScore = Math.Round(
            weightedScore * (100m - deliverable.PenalizacionAplicadaPct) / 100m,
            2,
            MidpointRounding.AwayFromZero);

        var evaluation = new Evaluacion
        {
            EntregableId = entregableId,
            EvaluadorId = userId.Value,
            PuntajeFinal = finalScore,
            ObservacionGeneral = request.ObservacionGeneral.Trim()
        };

        _context.Evaluaciones.Add(evaluation);
        _context.EvaluacionCriterios.AddRange(request.Criterios.Select(item =>
            new EvaluacionCriterio
            {
                EvaluacionId = evaluation.Id,
                CriterioId = item.CriterioId,
                NivelId = item.NivelId,
                PuntajeObtenido = selectedLevels[item.NivelId].Puntaje,
                Observacion = item.Observacion?.Trim() ?? string.Empty
            }));
        _audit.Record(userId, "calificacion.evaluada", "evaluacion", evaluation.Id);
        await _context.SaveChangesAsync();

        return CreatedAtAction(
            nameof(ObtenerCalificacionProyecto),
            new { proyectoId = (await _context.Entregables.FindAsync(entregableId))!.ProyectoId },
            new { evaluation.Id, evaluation.PuntajeFinal, evaluation.Publicada });
    }

    [HttpPost("evaluaciones/{evaluacionId:guid}/publicar")]
    public async Task<IActionResult> PublicarCalificacion(Guid evaluacionId)
    {
        var userId = UserAccessService.GetUserId(User);
        if (userId is null) return Unauthorized();

        var evaluation = await _context.Evaluaciones.FindAsync(evaluacionId);
        if (evaluation is null) return NotFound("Evaluación no encontrada.");
        var deliverable = await _context.Entregables.FindAsync(evaluation.EntregableId);
        if (deliverable is null ||
            !await _access.CanReviewProjectAsync(deliverable.ProyectoId, userId.Value))
            return Forbid();
        if (evaluation.Publicada)
        {
            return Ok(new
            {
                evaluation.Id,
                evaluation.PuntajeFinal,
                evaluation.PublicadaAt,
                yaPublicada = true
            });
        }

        evaluation.Publicada = true;
        evaluation.PublicadaAt = DateTime.UtcNow;
        _audit.Record(userId, "calificacion.publicada", "evaluacion", evaluacionId);
        await _context.SaveChangesAsync();

        return Ok(new
        {
            evaluation.Id,
            evaluation.PuntajeFinal,
            evaluation.PublicadaAt,
            notificaciones = "Pendientes de integración"
        });
    }

    [HttpGet("proyectos/{proyectoId:guid}/calificaciones")]
    public async Task<IActionResult> ObtenerCalificacionProyecto(Guid proyectoId)
    {
        var userId = UserAccessService.GetUserId(User);
        if (userId is null) return Unauthorized();
        if (!await _access.CanReadProjectAsync(proyectoId, userId.Value)) return NotFound();

        var isStaff = await _access.IsStaffAsync(userId.Value);
        var evaluations = await _context.Evaluaciones
            .Where(evaluation =>
                _context.Entregables.Any(deliverable =>
                    deliverable.Id == evaluation.EntregableId &&
                    deliverable.ProyectoId == proyectoId) &&
                (isStaff || evaluation.Publicada))
            .OrderByDescending(evaluation => evaluation.EvaluadaAt)
            .ToListAsync();
        var evaluationIds = evaluations.Select(evaluation => evaluation.Id).ToArray();
        var details = await _context.EvaluacionCriterios
            .Where(detail => evaluationIds.Contains(detail.EvaluacionId))
            .ToListAsync();
        var criterionIds = details.Select(detail => detail.CriterioId).Distinct().ToArray();
        var levelIds = details.Select(detail => detail.NivelId).Distinct().ToArray();
        var criteria = await _context.CriteriosRubrica
            .Where(criterion => criterionIds.Contains(criterion.Id))
            .ToDictionaryAsync(criterion => criterion.Id);
        var levels = await _context.NivelesRubrica
            .Where(level => levelIds.Contains(level.Id))
            .ToDictionaryAsync(level => level.Id);

        return Ok(evaluations.Select(evaluation => new
        {
            evaluation.Id,
            evaluation.EntregableId,
            evaluation.PuntajeFinal,
            evaluation.ObservacionGeneral,
            evaluation.Publicada,
            evaluation.EvaluadaAt,
            evaluation.PublicadaAt,
            Criterios = details
                .Where(detail => detail.EvaluacionId == evaluation.Id)
                .Select(detail => new
                {
                    Criterio = criteria[detail.CriterioId].Nombre,
                    detail.PuntajeObtenido,
                    detail.Observacion,
                    Nivel = levels[detail.NivelId].Etiqueta,
                    DescripcionNivel = levels[detail.NivelId].Descripcion
                })
        }));
    }
}

public sealed class EvaluarEntregaDto
{
    public string ObservacionGeneral { get; set; } = string.Empty;
    public List<EvaluarCriterioDto> Criterios { get; set; } = [];
}

public sealed class EvaluarCriterioDto
{
    public Guid CriterioId { get; set; }
    public Guid NivelId { get; set; }
    public string Observacion { get; set; } = string.Empty;
}

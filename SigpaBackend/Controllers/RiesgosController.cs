using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SigpaBackend.Data;
using SigpaBackend.Models;
using SigpaBackend.Services;

namespace SigpaBackend.Controllers;

[Route("api/proyectos/{proyectoId:guid}/riesgos")]
[ApiController]
[Authorize]
public class RiesgosController : ControllerBase
{
    private static readonly string[] SeveridadesValidas = ["Baja", "Media", "Alta", "Crítica"];
    private readonly SigpaContext _context;
    private readonly UserAccessService _access;
    private readonly AuditService _audit;

    public RiesgosController(SigpaContext context, UserAccessService access, AuditService audit)
    {
        _context = context;
        _access = access;
        _audit = audit;
    }

    [HttpGet]
    public async Task<IActionResult> ObtenerRiesgos(Guid proyectoId)
    {
        var userId = UserAccessService.GetUserId(User);
        if (userId is null) return Unauthorized();
        if (!await _access.CanReadProjectAsync(proyectoId, userId.Value)) return NotFound();

        var riesgos = await _context.Riesgos
            .Where(risk => risk.ProyectoId == proyectoId)
            .OrderByDescending(risk => risk.CreatedAt)
            .ToListAsync();

        return Ok(riesgos);
    }

    [HttpPost]
    public async Task<IActionResult> ReportarRiesgo(Guid proyectoId, [FromBody] CrearRiesgoDto request)
    {
        var userId = UserAccessService.GetUserId(User);
        if (userId is null) return Unauthorized();
        if (!await _access.IsProjectMemberAsync(proyectoId, userId.Value)) return NotFound();

        if (string.IsNullOrWhiteSpace(request.Titulo) || string.IsNullOrWhiteSpace(request.Descripcion))
        {
            return BadRequest("El título y la descripción del riesgo son obligatorios.");
        }

        if (!SeveridadesValidas.Contains(request.Severidad))
        {
            return BadRequest("La severidad debe ser Baja, Media, Alta o Crítica.");
        }

        var risk = new Riesgo
        {
            ProyectoId = proyectoId,
            ReportadoPor = userId.Value,
            Titulo = request.Titulo.Trim(),
            Descripcion = request.Descripcion.Trim(),
            Severidad = request.Severidad
        };

        _context.Riesgos.Add(risk);
        _audit.Record(userId, "riesgo.reportado", "riesgo", risk.Id);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(ObtenerRiesgos), new { proyectoId }, risk);
    }

    [HttpPatch("{riesgoId:guid}/resolver")]
    public async Task<IActionResult> ResolverRiesgo(Guid proyectoId, Guid riesgoId)
    {
        var userId = UserAccessService.GetUserId(User);
        if (userId is null) return Unauthorized();
        if (!await _access.CanReadProjectAsync(proyectoId, userId.Value)) return NotFound();

        var risk = await _context.Riesgos
            .FirstOrDefaultAsync(item => item.Id == riesgoId && item.ProyectoId == proyectoId);
        if (risk is null) return NotFound();

        if (!await _access.IsStaffAsync(userId.Value) && risk.ReportadoPor != userId.Value)
        {
            return Forbid();
        }

        if (risk.Estado == "Resuelto") return Ok(risk);

        risk.Estado = "Resuelto";
        risk.ResueltoAt = DateTime.UtcNow;
        _audit.Record(userId, "riesgo.resuelto", "riesgo", risk.Id);
        await _context.SaveChangesAsync();

        return Ok(risk);
    }
}

public sealed class CrearRiesgoDto
{
    public string Titulo { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public string Severidad { get; set; } = "Media";
}

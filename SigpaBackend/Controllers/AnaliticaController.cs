using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SigpaBackend.Data;
using SigpaBackend.Services;

namespace SigpaBackend.Controllers;

[ApiController]
[Route("api/analitica")]
[Authorize]
public sealed class AnaliticaController : ControllerBase
{
    private readonly SigpaContext _context;

    public AnaliticaController(SigpaContext context)
    {
        _context = context;
    }

    [HttpGet("opciones")]
    public async Task<IActionResult> ObtenerOpciones(CancellationToken cancellationToken)
    {
        var userId = UserAccessService.GetUserId(User);
        if (userId is null) return Unauthorized();
        if (!await EsCoordinador(userId.Value)) return Forbid();

        if (!await TablaExisteAsync("cursos", cancellationToken) ||
            !await ColumnaExisteAsync("proyectos", "curso_id", cancellationToken))
            return Ok(new { cursos = Array.Empty<object>(), docentes = Array.Empty<object>() });

        var cursos = await _context.Cursos
            .Where(course => course.Activo)
            .OrderBy(course => course.Nombre)
            .Select(course => new
            {
                course.Id,
                course.Codigo,
                course.Nombre,
                course.DocenteId,
                Docente = _context.Usuarios
                    .Where(user => user.Id == course.DocenteId)
                    .Select(user => user.NombreCompleto)
                    .FirstOrDefault()
            })
            .ToListAsync(cancellationToken);
        var hasTeacherAssignments = await TablaExisteAsync("docentes_cursos", cancellationToken);
        var docentesQuery = _context.Usuarios
            .Where(user => user.Rol == "Profesor");
        docentesQuery = hasTeacherAssignments
            ? docentesQuery.Where(user =>
                _context.Cursos.Any(course => course.DocenteId == user.Id && course.Activo) ||
                _context.DocentesCursos.Any(assignment =>
                    assignment.UsuarioId == user.Id &&
                    _context.Cursos.Any(course => course.Id == assignment.CursoId && course.Activo)))
            : docentesQuery.Where(user =>
                _context.Cursos.Any(course => course.DocenteId == user.Id && course.Activo));
        var docentes = await docentesQuery
            .OrderBy(user => user.NombreCompleto)
            .Select(user => new { user.Id, user.NombreCompleto })
            .ToListAsync(cancellationToken);

        return Ok(new { cursos, docentes });
    }

    [HttpGet("kpis")]
    public async Task<IActionResult> ObtenerIndicadores(
        [FromQuery] Guid? cursoId,
        [FromQuery] Guid? docenteId,
        CancellationToken cancellationToken)
    {
        var userId = UserAccessService.GetUserId(User);
        if (userId is null) return Unauthorized();
        if (!await EsCoordinador(userId.Value)) return Forbid();

        var proyectos = _context.Proyectos.AsNoTracking().AsQueryable();
        var hasCourses = await TablaExisteAsync("cursos", cancellationToken);
        if (cursoId.HasValue && hasCourses &&
            await ColumnaExisteAsync("proyectos", "curso_id", cancellationToken))
            proyectos = proyectos.Where(project => project.CursoId == cursoId.Value);
        var hasTeacherAssignments = await TablaExisteAsync("docentes_cursos", cancellationToken);
        if (docenteId.HasValue && hasCourses &&
            await ColumnaExisteAsync("proyectos", "curso_id", cancellationToken))
        {
            proyectos = hasTeacherAssignments
                ? proyectos.Where(project => project.CursoId.HasValue &&
                    _context.Cursos.Any(course =>
                        course.Id == project.CursoId.Value &&
                        (course.DocenteId == docenteId.Value ||
                         _context.DocentesCursos.Any(assignment =>
                             assignment.UsuarioId == docenteId.Value && assignment.CursoId == course.Id))))
                : proyectos.Where(project => project.CursoId.HasValue &&
                    _context.Cursos.Any(course =>
                        course.Id == project.CursoId.Value && course.DocenteId == docenteId.Value));
        }

        var projectIds = await proyectos.Select(project => project.Id).ToListAsync(cancellationToken);
        var projectIdSet = projectIds.ToArray();
        var entregas = _context.Entregables
            .AsNoTracking()
            .Where(deliverable => projectIdSet.Contains(deliverable.ProyectoId));

        var entregasATiempo = await entregas.CountAsync(
            item => item.Estado == "Entregado a Tiempo", cancellationToken);
        var entregasAtrasadas = await entregas.CountAsync(
            item => item.Estado == "Entregado Tardío", cancellationToken);
        var pendientesAtrasadas = await entregas.CountAsync(item =>
            item.Estado == "Pendiente" && item.FechaLimite < DateTimeOffset.UtcNow, cancellationToken);

        decimal? promedio = null;
        var evaluacionesDisponibles = await TablaExisteAsync("evaluaciones", cancellationToken);
        if (evaluacionesDisponibles)
        {
            promedio = await (
                from evaluation in _context.Evaluaciones.AsNoTracking()
                join deliverable in _context.Entregables.AsNoTracking()
                    on evaluation.EntregableId equals deliverable.Id
                where evaluation.Publicada && projectIdSet.Contains(deliverable.ProyectoId)
                select (decimal?)evaluation.PuntajeFinal)
                .AverageAsync(cancellationToken);
        }

        var riesgosAbiertos = 0;
        var alertasCriticas = 0;
        var riesgosDisponibles = await TablaExisteAsync("riesgos", cancellationToken);
        if (riesgosDisponibles)
        {
            riesgosAbiertos = await _context.Riesgos
                .AsNoTracking()
                .CountAsync(risk => projectIdSet.Contains(risk.ProyectoId) &&
                                    risk.Estado != "Resuelto", cancellationToken);
            alertasCriticas = await _context.Riesgos
                .AsNoTracking()
                .CountAsync(risk => projectIdSet.Contains(risk.ProyectoId) &&
                                    risk.Estado != "Resuelto" &&
                                    (risk.Severidad == "Alta" || risk.Severidad == "Crítica"),
                    cancellationToken);
        }

        var totalEntregas = entregasATiempo + entregasAtrasadas;
        return Ok(new
        {
            totalProyectos = projectIds.Count,
            entregasATiempo,
            entregasAtrasadas,
            pendientesAtrasadas,
            tasaEntregasATiempo = totalEntregas == 0
                ? 0m
                : Math.Round(entregasATiempo * 100m / totalEntregas, 2),
            promedioGeneral = Math.Round(promedio ?? 0m, 2),
            riesgosAbiertos,
            alertasCriticas,
            evaluacionesDisponibles,
            riesgosDisponibles
        });
    }

    private Task<bool> EsCoordinador(Guid userId) =>
        _context.Usuarios.AnyAsync(user => user.Id == userId && user.Rol == "Coordinador");

    private Task<bool> TablaExisteAsync(string tableName, CancellationToken cancellationToken) =>
        _context.Database.SqlQueryRaw<bool>(
                "SELECT to_regclass({0}) IS NOT NULL AS \"Value\"",
                $"public.{tableName}")
            .SingleAsync(cancellationToken);

    private Task<bool> ColumnaExisteAsync(
        string tableName,
        string columnName,
        CancellationToken cancellationToken) =>
        _context.Database.SqlQueryRaw<bool>(
                """
                SELECT EXISTS (
                    SELECT 1
                    FROM information_schema.columns
                    WHERE table_schema = 'public'
                      AND table_name = {0}
                      AND column_name = {1}
                ) AS "Value"
                """,
                tableName,
                columnName)
            .SingleAsync(cancellationToken);
}

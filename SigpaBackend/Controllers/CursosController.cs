using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SigpaBackend.Data;
using SigpaBackend.Services;

namespace SigpaBackend.Controllers;

[ApiController]
[Route("api/cursos")]
[Authorize]
public sealed class CursosController : ControllerBase
{
    private readonly SigpaContext _context;

    public CursosController(SigpaContext context)
    {
        _context = context;
    }

    [HttpGet("mis-matriculas")]
    public async Task<IActionResult> ObtenerMisCursos()
    {
        var userId = UserAccessService.GetUserId(User);
        if (userId is null) return Unauthorized();

        var cursos = await _context.Matriculas
            .Where(enrollment => enrollment.EstudianteId == userId.Value &&
                                 enrollment.Estado == "Activa")
            .Join(_context.Cursos.Where(course => course.Activo),
                enrollment => enrollment.CursoId,
                course => course.Id,
                (enrollment, course) => new
                {
                    course.Id,
                    course.Codigo,
                    course.Nombre
                })
            .OrderBy(course => course.Nombre)
            .ToListAsync();

        return Ok(cursos);
    }

    [HttpGet("{cursoId:guid}/companeros")]
    public async Task<IActionResult> ObtenerCompaneros(Guid cursoId)
    {
        var userId = UserAccessService.GetUserId(User);
        if (userId is null) return Unauthorized();

        var tieneMatriculaActiva = await _context.Matriculas.AnyAsync(enrollment =>
            enrollment.CursoId == cursoId &&
            enrollment.EstudianteId == userId.Value &&
            enrollment.Estado == "Activa");
        if (!tieneMatriculaActiva) return NotFound("No existe una matrícula activa en esta asignatura.");

        var estudiantesOcupados = _context.ProyectoIntegrantes
            .Where(member => member.Estado == "Invitado" || member.Estado == "Aceptado")
            .Select(member => member.UsuarioId);

        var peers = await _context.Matriculas
            .Where(enrollment => enrollment.CursoId == cursoId &&
                                 enrollment.Estado == "Activa" &&
                                 enrollment.EstudianteId != userId.Value &&
                                 !estudiantesOcupados.Contains(enrollment.EstudianteId))
            .Join(_context.Usuarios.Where(user => user.Rol == "Estudiante"),
                enrollment => enrollment.EstudianteId,
                student => student.Id,
                (enrollment, student) => new
                {
                    student.Id,
                    student.NombreCompleto,
                    student.CorreoInstitucional
                })
            .OrderBy(student => student.NombreCompleto)
            .ToListAsync();

        return Ok(peers);
    }
}

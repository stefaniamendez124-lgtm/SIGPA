using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SigpaBackend.Data;

namespace SigpaBackend.Services;

public sealed class UserAccessService
{
    private static readonly string[] StaffRoles = ["Profesor", "Coordinador", "Administrador"];
    private readonly SigpaContext _context;

    public UserAccessService(SigpaContext context)
    {
        _context = context;
    }

    public static Guid? GetUserId(ClaimsPrincipal principal)
    {
        var claim = principal.FindFirstValue(ClaimTypes.NameIdentifier) ??
                    principal.FindFirstValue("sub");

        return Guid.TryParse(claim, out var userId) ? userId : null;
    }

    public async Task<bool> HasRoleAsync(Guid userId, params string[] roles)
    {
        return await _context.Usuarios
            .AnyAsync(user => user.Id == userId && roles.Contains(user.Rol));
    }

    public async Task<bool> IsStaffAsync(Guid userId)
    {
        return await HasRoleAsync(userId, StaffRoles);
    }

    public async Task<bool> IsProjectMemberAsync(Guid projectId, Guid userId)
    {
        if (!await _context.Usuarios.AnyAsync(user => user.Id == userId))
            return false;

        return await _context.Proyectos.AnyAsync(project =>
            project.Id == projectId &&
            (project.CreadorId == userId ||
             _context.ProyectoIntegrantes.Any(member =>
                 member.ProyectoId == projectId &&
                 member.UsuarioId == userId &&
                 member.Estado == "Aceptado")));
    }

    public async Task<bool> CanReadProjectAsync(Guid projectId, Guid userId)
    {
        if (await IsProjectMemberAsync(projectId, userId))
            return true;
        if (await HasRoleAsync(userId, "Coordinador", "Administrador"))
            return true;

        return await CanReviewProjectAsync(projectId, userId);
    }

    public async Task<bool> CanReviewProjectAsync(Guid projectId, Guid userId)
    {
        var isTeacher = await HasRoleAsync(userId, "Profesor");
        if (!isTeacher) return false;

        return await _context.Proyectos.AnyAsync(project =>
            project.Id == projectId &&
            project.CursoId.HasValue &&
            _context.Cursos.Any(course =>
                course.Id == project.CursoId.Value &&
                course.Activo &&
                (course.DocenteId == userId ||
                 _context.DocentesCursos.Any(assignment =>
                     assignment.UsuarioId == userId && assignment.CursoId == course.Id))));
    }
}
